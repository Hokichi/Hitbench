using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;

namespace Hitbench.ViewModels.Popups;

public sealed partial class OHsMidiImportPopupVM : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    private OHsMidiImportMode selectedMode = OHsMidiImportMode.AsIs;
    public bool CanImport => Enum.IsDefined(SelectedMode);
    public string MidiBpmText { get; }
    public string ProjectBpmText { get; }
    public string FullTimingText { get; }

    public OHsMidiImportPopupVM(OHsMidiImportSource source, IReadOnlyList<UninheritedPoint> points)
    {
        var culture = CultureInfo.CurrentCulture;
        var tempos = source.Tempos;
        var effectivePoints = points.OrderBy(p => p.Offset).GroupBy(p => p.Offset).Select(g => g.Last()).ToArray();
        MidiBpmText = string.Format(culture, tempos.Count > 1 ? "{0:0.######} BPM …" : "{0:0.######} BPM",
            tempos.Count > 0 ? 60000000m / tempos[0].MicrosecondsPerQuarterNote : 120);
        ProjectBpmText = string.Format(culture, effectivePoints.Length > 1 ? "{0} BPM …" : "{0} BPM", effectivePoints.Length > 0 ? effectivePoints[0].BPM : 0);
        var origin = effectivePoints.Length > 0 ? effectivePoints[0].Offset : 0;
        FullTimingText = string.Format(culture, "MIDI: {0}. Project: {1}.",
            string.Join("; ", tempos.Select(t => string.Format(culture, "{0:0.######} ms: {1:0.######} BPM ({2} μs/quarter)",
                origin + (decimal)t.ElapsedMicrosecondsNumerator / source.TicksPerQuarterNote / 1000, 60000000m / t.MicrosecondsPerQuarterNote, t.MicrosecondsPerQuarterNote))),
            string.Join("; ", effectivePoints.Select(p => string.Format(culture, "{0} ms: {1} BPM", p.Offset, p.BPM))));
    }
}
