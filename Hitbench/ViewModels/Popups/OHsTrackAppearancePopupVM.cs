using CommunityToolkit.Mvvm.ComponentModel;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
namespace Hitbench.ViewModels.Popups;
public partial class OHsTrackAppearancePopupVM : ObservableObject
{
    private readonly OHsTrackAppearanceRequest original;
    private readonly RgbColor initialColor;
    public OHsTrackAppearancePopupVM(OHsTrackAppearanceRequest request, string effectiveColor)
    {
        if (!RgbColor.TryParse(request.Color ?? effectiveColor, out initialColor)) throw new ArgumentException("Invalid initial color.",nameof(effectiveColor));
        original=request; color=initialColor.ToHex();
    }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDirty)), NotifyPropertyChangedFor(nameof(CanSave))] private string color;
    public bool CanSave => RgbColor.TryParse(Color,out _);
    public bool IsDirty => !RgbColor.TryParse(Color,out var rgb) || rgb != initialColor;
    public OHsTrackAppearanceResult CreateResult()
    {
        if (!RgbColor.TryParse(Color,out var rgb)) throw new InvalidOperationException("Invalid color draft.");
        return new(original.Color is null && rgb == initialColor ? null : rgb.ToHex());
    }
}
