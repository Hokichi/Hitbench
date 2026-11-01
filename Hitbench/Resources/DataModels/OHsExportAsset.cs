using System.Security.Cryptography;
using System.Text;
namespace Hitbench.Resources.DataModels;
public sealed record OHsExportAsset
{
    public string Id { get; }
    public IReadOnlyList<OHsExportSource> Components { get; }
    public IReadOnlyList<(string FileName, int GainPercent)> Sources { get; }
    public OHsExportAsset(string id, IReadOnlyList<(string FileName, int GainPercent)> sources)
    { Id = id; Sources = Array.AsReadOnly(sources.ToArray()); Components = Array.AsReadOnly(sources.Select(s => new OHsExportSource(s.FileName, s.GainPercent)).ToArray()); }
    private OHsExportAsset(string id, OHsExportSource[] components)
    { Id = id; Components = Array.AsReadOnly(components); Sources = Array.AsReadOnly(components.Select(s => (s.FileName, s.GainPercent)).ToArray()); }
    public static OHsExportAsset CreateShaped(IEnumerable<OHsExportSource> sources, IDictionary<string, OHsExportAsset>? cache = null)
    {
        var components = sources.Select(s =>
        {
            if (s.GainPercent < 0) throw new ArgumentException("Invalid export gain.");
            var envelope = s.Envelope?.Scale(s.GainPercent, 100);
            if (envelope is { IsConstant: true })
            {
                var segment = envelope.Segments[0];
                var percent = System.Numerics.BigInteger.DivRem(segment.StartNumerator * 100, segment.Denominator, out var remainder);
                if (remainder.IsZero && percent <= int.MaxValue)
                    return new OHsExportSource(s.FileName.Replace('\\', '/'), (int)percent);
            }
            return envelope is null ? s with { FileName = s.FileName.Replace('\\', '/') } : new OHsExportSource(s.FileName.Replace('\\', '/'), 100, envelope);
        }).Where(s => s.GainPercent != 0).GroupBy(s => (Path: s.FileName.ToUpperInvariant(), Envelope: s.Envelope?.Key ?? ""))
            .Select(g => g.First() with { GainPercent = checked(g.Sum(s => s.GainPercent)) }).OrderBy(s => s.FileName, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Envelope?.Key, StringComparer.Ordinal).ToArray();
        if (components.All(s => s.Envelope is null)) return Create(components.Select(s => (s.FileName, s.GainPercent)), cache);
        var canonical = string.Join("|", components.Select(s => FormattableString.Invariant($"{s.FileName.ToUpperInvariant().Length}:{s.FileName.ToUpperInvariant()}:{s.GainPercent}:{s.Envelope?.Key}")));
        if (cache is not null && cache.TryGetValue(canonical, out var existing)) return existing;
        var asset = new OHsExportAsset(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))), components);
        if (cache is not null) cache[canonical] = asset;
        return asset;
    }
    public static OHsExportAsset Create(IEnumerable<(string FileName, int GainPercent)> sources)
        => Create(sources, null);

    internal static OHsExportAsset Create(IEnumerable<(string FileName, int GainPercent)> sources, IDictionary<string, OHsExportAsset>? cache)
    {
        var merged = sources.GroupBy(s => s.FileName.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .Select(g => (FileName: g.Key, GainPercent: checked(g.Sum(s => s.GainPercent))))
            .OrderBy(s => s.FileName, StringComparer.OrdinalIgnoreCase).ToArray();
        if (merged.All(s => s.GainPercent == 0)) merged = [];
        var canonical = string.Join("|", merged.Select(s => $"{s.FileName.ToUpperInvariant().Length}:{s.FileName.ToUpperInvariant()}:{s.GainPercent}"));
        if (cache is not null && cache.TryGetValue(canonical, out var existing)) return existing;
        var asset = new OHsExportAsset(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))), merged);
        if (cache is not null) cache[canonical] = asset;
        return asset;
    }
}
