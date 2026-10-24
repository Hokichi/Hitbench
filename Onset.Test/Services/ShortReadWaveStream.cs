using NAudio.Wave;

namespace Onset.Test.Services;

internal sealed class ShortReadWaveStream(byte[] data, WaveFormat format, Action? onRead = null) : WaveStream
{
    private readonly MemoryStream stream = new(data, false);
    public bool WasDisposed { get; private set; }
    public override WaveFormat WaveFormat => format;
    public override long Length => 0; // Deliberately inaccurate duration hint.
    public override long Position { get => stream.Position; set => stream.Position = value; }
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = stream.Read(buffer, offset, Math.Min(count, format.BlockAlign * 17));
        onRead?.Invoke();
        return read;
    }
    protected override void Dispose(bool disposing)
    {
        WasDisposed = true;
        if (disposing) stream.Dispose();
        base.Dispose(disposing);
    }
}
