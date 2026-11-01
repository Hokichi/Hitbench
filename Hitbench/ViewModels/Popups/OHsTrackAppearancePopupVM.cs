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
        original=request; name=request.Name; color=initialColor.ToHex();
    }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDirty)), NotifyPropertyChangedFor(nameof(CanSave))] private string color;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDirty)), NotifyPropertyChangedFor(nameof(CanSave))] private string name;
    public bool CanSave => Name is not null && RgbColor.TryParse(Color,out _);
    public bool IsDirty => Name != original.Name || !RgbColor.TryParse(Color,out var rgb) || rgb != initialColor;
    public OHsTrackAppearanceResult CreateResult()
    {
        if (Name is null || !RgbColor.TryParse(Color,out var rgb)) throw new InvalidOperationException("Invalid color draft.");
        return new(original.Color is null && rgb == initialColor ? null : rgb.ToHex()) { Name = Name };
    }
}
