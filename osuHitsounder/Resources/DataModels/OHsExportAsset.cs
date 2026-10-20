using System.Security.Cryptography;
using System.Text;
namespace osuHitsounder.Resources.DataModels;
public sealed record OHsExportAsset
{
    public string Id { get; }
    public IReadOnlyList<(string FileName, int GainPercent)> Sources { get; }
    public OHsExportAsset(string id, IReadOnlyList<(string FileName, int GainPercent)> sources)
    { Id = id; Sources = Array.AsReadOnly(sources.ToArray()); }
    public static OHsExportAsset Create(IEnumerable<(string FileName, int GainPercent)> sources)
    {
        var merged = sources.GroupBy(s => s.FileName.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .Select(g => (FileName: g.Key, GainPercent: checked(g.Sum(s => s.GainPercent))))
            .OrderBy(s => s.FileName, StringComparer.OrdinalIgnoreCase).ToArray();
        if (merged.All(s => s.GainPercent == 0)) merged = [];
        var canonical = string.Join("|", merged.Select(s => $"{s.FileName.ToUpperInvariant().Length}:{s.FileName.ToUpperInvariant()}:{s.GainPercent}"));
        return new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))), merged);
    }
}
