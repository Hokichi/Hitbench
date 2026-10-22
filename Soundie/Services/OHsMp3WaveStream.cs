using System.Buffers.Binary;
using System.IO;
using NAudio.Wave;

namespace Soundie.Services;

/// <summary>Sequential MP3 decoding with osu! stable gap handling and deterministic seeks.</summary>
public sealed class OHsMp3WaveStream : WaveStream
{
    private readonly Mp3FileReader reader;
    private readonly MemoryStream cache = new();
    private readonly byte[] block = new byte[8192];
    private long skip;
    private long length;
    private long position;
    private bool disposed;

    public OHsMp3WaveStream(string path)
    {
        reader = new Mp3FileReader(path);
        try
        {
            using var input = File.OpenRead(path);
            _ = Id3v2Tag.ReadTag(input);
            var frame = Mp3Frame.LoadFromStream(input);
            var next = frame is null ? null : Mp3Frame.LoadFromStream(input);
            var retainedHeader = frame is not null && next is not null && frame.MpegVersion == MpegVersion.Version25 &&
                frame.SampleRate == next.SampleRate && frame.ChannelMode == next.ChannelMode;
            var (head, tail) = GetGaps(frame, reader.Length / reader.WaveFormat.BlockAlign, retainedHeader);
            skip = checked(head * reader.WaveFormat.BlockAlign);
            length = checked(reader.Length - skip - tail * reader.WaveFormat.BlockAlign);
        }
        catch { reader.Dispose(); cache.Dispose(); throw; }
    }

    private static (long Head, long Tail) GetGaps(Mp3Frame? frame, long frames, bool retainedHeader)
    {
        if (frame is null) return (0, 0);
        var data = frame.RawData.AsSpan();
        var mono = frame.ChannelMode == ChannelMode.Mono;
        var header = 4 + (frame.MpegVersion == MpegVersion.Version1 ? (mono ? 17 : 32) : (mono ? 9 : 17));
        if (data.Length < header + 8 || !(data.Slice(header, 4).SequenceEqual("Xing"u8) || data.Slice(header, 4).SequenceEqual("Info"u8))) return (0, 0);
        var metadata = retainedHeader ? frame.SampleCount : 0;
        var flags = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(header + 4, 4));
        if ((flags & ~15u) != 0) return (metadata, 0);
        var offset = header + 8;
        if ((flags & 1) != 0) offset += 4;
        if ((flags & 2) != 0) offset += 4;
        if ((flags & 4) != 0) offset += 100;
        if ((flags & 8) != 0) offset += 4;
        if (offset > data.Length - 24) return (metadata, 0);
        // BASS accepts a valid gapless extension from other encoder identifiers too.
        if (!char.IsAsciiLetter((char)data[offset])) return (metadata, 0);
        var delay = (data[offset + 21] << 4) | (data[offset + 22] >> 4);
        var padding = ((data[offset + 22] & 15) << 8) | data[offset + 23];
        // Layer III's synthesis delay is 529 native samples, independent of sample rate.
        var head = metadata + delay + 529L;
        var tail = padding - 529L;
        return tail >= 0 && head + tail <= frames ? (head, tail) : (metadata, 0);
    }

    public override WaveFormat WaveFormat => reader.WaveFormat;
    public override long Length { get { ThrowIfDisposed(); return length; } }
    public override long Position
    {
        get { ThrowIfDisposed(); return position; }
        set
        {
            ThrowIfDisposed();
            var bounded = Math.Clamp(value, 0, length);
            position = bounded - bounded % WaveFormat.BlockAlign;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException(nameof(offset));
        count = (int)Math.Min(count, length - position);
        if (count == 0) return 0;
        var target = checked(position + count);
        if (target > Array.MaxLength) throw new IOException("Decoded MP3 exceeds the supported PCM cache size.");
        FillCache(target, CancellationToken.None);
        cache.Position = Math.Min(position, cache.Length);
        var result = cache.Read(buffer, offset, (int)Math.Min(count, Math.Max(0, cache.Length - position)));
        position = Math.Min(length, position + result);
        return result;
    }

    public void PreparePosition(CancellationToken token)
    {
        ThrowIfDisposed();
        FillCache(Math.Min(length, checked(position + WaveFormat.BlockAlign)), token);
    }

    private void FillCache(long target, CancellationToken token)
    {
        if (target > Array.MaxLength) throw new IOException("Decoded MP3 exceeds the supported PCM cache size.");
        while (cache.Length < target)
        {
            token.ThrowIfCancellationRequested();
            var wanted = (int)Math.Min(block.Length, skip > 0 ? skip : target - cache.Length);
            var read = reader.Read(block, 0, wanted);
            if (read == 0) { length = cache.Length; break; }
            if (skip > 0) { skip -= read; continue; }
            // ponytail: retains native PCM (~11 MiB/minute stereo 48 kHz); use bounded paging if measured memory requires it.
            if (cache.Length + read > cache.Capacity)
                cache.Capacity = checked((int)Math.Min(Array.MaxLength, Math.Min(length, Math.Max(cache.Length + read, Math.Max(8192L, cache.Capacity * 2L)))));
            cache.Position = cache.Length;
            cache.Write(block, 0, read);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            reader.Dispose();
            cache.Dispose();
        }
        base.Dispose(disposing);
    }
}
