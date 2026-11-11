using Melanchall.DryWetMidi.Core;

namespace Hitbench.Test.Services;

internal static class OHsMidiTestData
{
    public static MemoryStream File(int ppq, params TrackChunk[] tracks)
    {
        var file = new MidiFile(tracks) { TimeDivision = new TicksPerQuarterNoteTimeDivision((short)ppq) };
        var stream = new MemoryStream();
        file.Write(stream, format: tracks.Length == 1 ? MidiFileFormat.SingleTrack : MidiFileFormat.MultiTrack,
            settings: new WritingSettings { TextEncoding = System.Text.Encoding.UTF8 }); stream.Position = 0; return stream;
    }
    public static TrackChunk Track(params MidiEvent[] events) => new(events);
}
