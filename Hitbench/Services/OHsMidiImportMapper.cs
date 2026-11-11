using System.IO;
using System.Numerics;
using Hitbench.Core.Constants;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;

namespace Hitbench.Services;

public sealed class OHsMidiImportMapper
{
    public bool MatchesTiming(OHsMidiImportSource source, IReadOnlyList<UninheritedPoint> points)
    {
        var grid = Prepare(points);
        if (!source.HasExplicitTempo || source.TicksPerQuarterNote <= 0 || source.Tempos.Count != grid.Length) return false;
        for (var i = 0; i < grid.Length; i++)
        {
            var tempo = source.Tempos[i];
            if ((BigInteger)grid[i].BPM * tempo.MicrosecondsPerQuarterNote != 60000000 ||
                ((BigInteger)grid[i].Offset - grid[0].Offset) * source.TicksPerQuarterNote * 1000 != tempo.ElapsedMicrosecondsNumerator) return false;
        }
        return true;
    }

    public IReadOnlyList<OHsMidiImportedTrack> Map(OHsMidiImportSource source, IReadOnlyList<UninheritedPoint> points,
        int subdivision, double audioDurationMilliseconds, OHsMidiImportMode mode, IReadOnlyCollection<string> existingNames)
    {
        var grid = Prepare(points);
        if (source.TicksPerQuarterNote <= 0 || !Enum.IsDefined(mode) ||
            subdivision is not (1 or 2 or 3 or 4 or 5 or 6 or 8 or 12 or 16) ||
            !double.IsFinite(audioDurationMilliseconds) || audioDurationMilliseconds <= 0)
            throw new InvalidDataException("MIDI import requires valid timing, subdivision, and audio duration.");
        var beats = new BigInteger[grid.Length];
        for (var i = 1; i < grid.Length; i++)
            beats[i] = beats[i - 1] + ((BigInteger)grid[i].Offset - grid[i - 1].Offset) * grid[i - 1].BPM;
        var names = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<OHsMidiImportedTrack>();
        var elapsedDenominator = (BigInteger)source.TicksPerQuarterNote * 1000;
        foreach (var part in source.Parts)
        {
            var notes = new List<OHsMidiImportedNote>();
            var occupied = new HashSet<(string, int)>();
            foreach (var attack in part.Attacks)
            {
                if (attack.NoteNumber is < 24 or > 127 || attack.Tick < 0 || attack.ElapsedMicrosecondsNumerator < 0)
                    throw new InvalidDataException("MIDI import contains an invalid note.");
                var elapsedNumerator = attack.ElapsedMicrosecondsNumerator + grid[0].Offset * elapsedDenominator;
                var time = mode switch
                {
                    OHsMidiImportMode.AsIs => Round(elapsedNumerator, elapsedDenominator, false),
                    OHsMidiImportMode.ClosestBeat => Closest(grid, subdivision, elapsedNumerator, elapsedDenominator),
                    OHsMidiImportMode.ProjectBpm => ProjectBeat(grid, beats, subdivision,
                        Round((BigInteger)attack.Tick * subdivision, source.TicksPerQuarterNote, true)),
                    _ => throw new InvalidDataException("Unknown MIDI import timing mode.")
                };
                if (time < int.MinValue || time > int.MaxValue) throw new InvalidDataException("An imported MIDI timestamp exceeds the supported range.");
                var timestamp = (int)time;
                var key = InstrumentalKeys.GetName(attack.NoteNumber - 24);
                if (occupied.Add((key, timestamp))) notes.Add(new(key, timestamp, timestamp < 0 || timestamp >= audioDurationMilliseconds));
            }
            if (notes.Count == 0) continue;
            var baseName = string.IsNullOrWhiteSpace(part.Name)
                ? $"{Path.GetFileNameWithoutExtension(source.FileName)} · Track {part.TrackIndex + 1} · Channel {part.Channel}"
                : part.Name.Trim();
            var name = baseName;
            var suffix = 1;
            while (!names.Add(name)) name = $"{baseName} ({suffix++})";
            result.Add(new(name, notes));
        }
        if (result.Count == 0) throw new InvalidDataException("The MIDI file contains no importable melodic notes.");
        return Array.AsReadOnly(result.ToArray());
    }

    private static UninheritedPoint[] Prepare(IReadOnlyList<UninheritedPoint> points)
    {
        if (points.Count == 0 || points.Any(p => p.BPM <= 0)) throw new InvalidDataException("MIDI import requires valid project timing points.");
        return points.OrderBy(p => p.Offset).GroupBy(p => p.Offset).Select(g => g.Last()).ToArray();
    }

    private static BigInteger ProjectBeat(UninheritedPoint[] grid, BigInteger[] beats, int subdivision, BigInteger beat)
    {
        var index = 0;
        while (index + 1 < grid.Length && beats[index + 1] * subdivision <= beat * 60000) index++;
        var denominator = (BigInteger)grid[index].BPM * subdivision;
        return Round(grid[index].Offset * denominator + beat * 60000 - beats[index] * subdivision, denominator, false);
    }

    private static BigInteger Closest(UninheritedPoint[] grid, int subdivision, BigInteger target, BigInteger targetDenominator)
    {
        BigInteger best = 0, bestDenominator = 1, bestDistance = 0;
        var found = false;
        for (var i = 0; i < grid.Length; i++)
        {
            var denominator = (BigInteger)grid[i].BPM * subdivision;
            var tick = Floor((target - grid[i].Offset * targetDenominator) * denominator, targetDenominator * 60000);
            BigInteger? last = i + 1 < grid.Length
                ? -Floor(-((BigInteger)grid[i + 1].Offset - grid[i].Offset) * denominator, 60000) - 1 : null;
            for (var adjacent = 0; adjacent <= 1; adjacent++)
            {
                var candidateTick = tick + adjacent;
                if (i > 0) candidateTick = BigInteger.Max(0, candidateTick);
                if (last is { } maximum) candidateTick = BigInteger.Min(maximum, candidateTick);
                if (i > 0 && candidateTick < 0) continue;
                var candidate = grid[i].Offset * denominator + candidateTick * 60000;
                var distance = BigInteger.Abs(candidate * targetDenominator - target * denominator);
                if (found && (distance * bestDenominator > bestDistance * denominator ||
                    distance * bestDenominator == bestDistance * denominator && candidate * bestDenominator >= best * denominator)) continue;
                found = true; best = candidate; bestDenominator = denominator; bestDistance = distance;
            }
        }
        return Round(best, bestDenominator, false);
    }

    private static BigInteger Floor(BigInteger numerator, BigInteger denominator)
    {
        var value = BigInteger.DivRem(numerator, denominator, out var remainder);
        return remainder < 0 ? value - 1 : value;
    }

    private static BigInteger Round(BigInteger numerator, BigInteger denominator, bool earlierTie)
    {
        var lower = Floor(numerator, denominator);
        var remainder = numerator - lower * denominator;
        var comparison = (remainder * 2).CompareTo(denominator);
        return comparison > 0 || comparison == 0 && !earlierTie && numerator.Sign >= 0 ? lower + 1 : lower;
    }
}
