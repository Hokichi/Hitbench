using System.IO;
using System.Numerics;
using System.Text;
using Hitbench.Resources.DataModels;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

namespace Hitbench.Services.FileSystem;

public sealed class OHsMidiFileReader : IOHsMidiFileReader
{
    public Task<OHsMidiImportSource> ReadAsync(string path, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        using var stream = File.OpenRead(path);
        return Read(stream, Path.GetFileName(path), token);
    }, token);

    public OHsMidiImportSource Read(Stream stream, string fileName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (stream.CanSeek && stream.Position == stream.Length) throw new InvalidDataException("The MIDI file is empty.");
        if (stream.CanSeek)
        {
            var position = stream.Position;
            Span<byte> signature = stackalloc byte[8];
            var count = stream.Read(signature);
            stream.Position = position;
            if (count == 8 && signature.SequenceEqual("SMF2CLIP"u8))
                throw new NotSupportedException("MIDI 2.0 clip files are not supported.");
        }
        MidiFile file;
        try
        {
            file = MidiFile.Read(stream, new ReadingSettings
            {
                TextEncoding = Encoding.UTF8,
                InvalidChunkSizePolicy = InvalidChunkSizePolicy.Abort,
                MissedEndOfTrackPolicy = MissedEndOfTrackPolicy.Abort,
                UnexpectedTrackChunksCountPolicy = UnexpectedTrackChunksCountPolicy.Abort
            });
        }
        catch (MidiException error) { throw new InvalidDataException("The MIDI file is invalid or corrupted.", error); }
        catch (EndOfStreamException error) { throw new InvalidDataException("The MIDI file is incomplete.", error); }
        token.ThrowIfCancellationRequested();
        if (file.OriginalFormat is not (MidiFileFormat.SingleTrack or MidiFileFormat.MultiTrack) ||
            file.TimeDivision is not TicksPerQuarterNoteTimeDivision division)
            throw new NotSupportedException("Only format 0/1 MIDI files with ticks-per-quarter-note timing are supported.");
        if (division.TicksPerQuarterNote <= 0) throw new InvalidDataException("The MIDI file has invalid tick resolution.");

        var events = new List<(long Tick, int Track, int Order, MidiEvent Event)>();
        var names = new Dictionary<int, string?>();
        var tracks = file.GetTrackChunks().ToArray();
        for (var track = 0; track < tracks.Length; track++)
        {
            long tick = 0;
            var order = 0;
            foreach (var item in tracks[track].Events)
            {
                token.ThrowIfCancellationRequested();
                try { tick = checked(tick + item.DeltaTime); }
                catch (OverflowException error) { throw new InvalidDataException("MIDI time exceeds the supported range.", error); }
                events.Add((tick, track, order++, item));
                if (item is SequenceTrackNameEvent name && !names.ContainsKey(track)) names[track] = name.Text;
            }
        }
        var explicitTempo = events.Any(e => e.Event is SetTempoEvent);
        var tempos = new List<OHsMidiTempoEvent>();
        if (!events.Any(e => e.Tick == 0 && e.Event is SetTempoEvent)) tempos.Add(new(0, 500000, 0));
        var parts = new Dictionary<(int Track, int Channel), List<OHsMidiAttack>>();
        var percussion = new HashSet<(int Track, int Channel)>();
        var excludedPitches = 0;
        var elapsed = BigInteger.Zero;
        long previousTick = 0;
        var tempo = 500000;
        foreach (var group in events.OrderBy(e => e.Tick).ThenBy(e => e.Track).ThenBy(e => e.Order).GroupBy(e => e.Tick))
        {
            token.ThrowIfCancellationRequested();
            elapsed += (BigInteger)(group.Key - previousTick) * tempo;
            previousTick = group.Key;
            var changes = group.Where(e => e.Event is SetTempoEvent).ToArray();
            if (changes.Length > 0)
            {
                var values = changes.GroupBy(e => e.Track).Select(g => ((SetTempoEvent)g.Last().Event).MicrosecondsPerQuarterNote).Distinct().ToArray();
                if (values.Length != 1) throw new InvalidDataException("MIDI tracks define conflicting tempos at the same time.");
                if (values[0] <= 0 || values[0] > 0xFFFFFF) throw new InvalidDataException("The MIDI file contains an invalid tempo.");
                tempo = checked((int)values[0]);
                tempos.Add(new(group.Key, tempo, elapsed));
            }
            foreach (var item in group)
            {
                if (item.Event is not NoteOnEvent note || note.Velocity == 0) continue;
                var channel = (int)note.Channel + 1;
                if (channel == 10) { percussion.Add((item.Track, channel)); continue; }
                if (note.NoteNumber < 24) { excludedPitches++; continue; }
                var key = (item.Track, channel);
                if (!parts.TryGetValue(key, out var attacks)) parts[key] = attacks = [];
                attacks.Add(new((int)note.NoteNumber, item.Tick, elapsed));
            }
        }
        if (parts.Count == 0) throw new InvalidDataException("The MIDI file contains no importable melodic notes.");
        return new(fileName, division.TicksPerQuarterNote, explicitTempo, tempos,
            parts.OrderBy(p => p.Key.Track).ThenBy(p => p.Key.Channel).Select(p =>
                new OHsMidiPart(p.Key.Track, p.Key.Channel, names.GetValueOrDefault(p.Key.Track), p.Value)).ToArray(),
            excludedPitches, percussion.OrderBy(p => p.Track).ThenBy(p => p.Channel)
                .Select(p => $"{names.GetValueOrDefault(p.Track) ?? "Track " + (p.Track + 1)} (channel {p.Channel})").ToArray());
    }
}
