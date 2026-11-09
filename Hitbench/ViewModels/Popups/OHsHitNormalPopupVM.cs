using CommunityToolkit.Mvvm.ComponentModel;
using Hitbench.Resources.DataModels;

namespace Hitbench.ViewModels.Popups;

public partial class OHsHitNormalPopupVM : ObservableObject
{
    public OHsHitNormalPopupVM(OHsHitNormalConfiguration current, IReadOnlyList<OHsHitNormalChoice> choices)
    {
        Choices = Array.AsReadOnly(new[] { new OHsHitNormalChoice("", "Default") }.Concat(choices.Where(c => c.Path.Length > 0))
            .Concat(new[] { current.NormalPath, current.SoftPath, current.DrumPath }.Where(p => p.Length > 0).Select(p => new OHsHitNormalChoice(p, p)))
            .DistinctBy(c => c.Path, StringComparer.OrdinalIgnoreCase).ToArray());
        string Match(string path) => Choices.First(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)).Path;
        normalPath = Match(current.NormalPath); softPath = Match(current.SoftPath); drumPath = Match(current.DrumPath);
    }

    public IReadOnlyList<OHsHitNormalChoice> Choices { get; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] private string normalPath;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] private string softPath;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] private string drumPath;
    public bool CanSave => new[] { NormalPath, SoftPath, DrumPath }.All(path => Choices.Any(c => c.Path == path));
    public OHsHitNormalConfiguration CreateResult() => CanSave ? new(NormalPath, SoftPath, DrumPath) : throw new InvalidOperationException("Invalid hitnormal selection.");
}
