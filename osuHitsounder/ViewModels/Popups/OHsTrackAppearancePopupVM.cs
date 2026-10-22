using CommunityToolkit.Mvvm.ComponentModel;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.ViewModels.Popups;
public partial class OHsTrackAppearancePopupVM : ObservableObject
{
    private readonly OHsTrackAppearanceRequest original;
    private readonly RgbColor initialColor;
    public OHsTrackAppearancePopupVM(OHsTrackAppearanceRequest request, string effectiveColor)
    {
        if (!RgbColor.TryParse(request.Color ?? effectiveColor, out initialColor)) throw new ArgumentException("Invalid initial color.",nameof(effectiveColor));
        original=request; name=request.Name; color=initialColor.ToHex();
    }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDirty))] private string name;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDirty)), NotifyPropertyChangedFor(nameof(CanSave))] private string color;
    public bool CanSave => RgbColor.TryParse(Color,out _);
    public bool IsDirty => Name != original.Name || !RgbColor.TryParse(Color,out var rgb) || rgb != initialColor;
    public OHsTrackAppearanceResult CreateResult()
    {
        if (!RgbColor.TryParse(Color,out var rgb)) throw new InvalidOperationException("Invalid color draft.");
        return new(Name,original.Color is null && rgb == initialColor ? null : rgb.ToHex());
    }
}
