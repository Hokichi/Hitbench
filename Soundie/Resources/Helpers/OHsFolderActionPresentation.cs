using System.Windows;

namespace Soundie.Resources.Helpers;

public static class OHsFolderActionPresentation
{
    public static readonly DependencyProperty IsPressOffsetEnabledProperty = DependencyProperty.RegisterAttached(
        "IsPressOffsetEnabled", typeof(bool), typeof(OHsFolderActionPresentation), new PropertyMetadata(true));
    public static bool GetIsPressOffsetEnabled(DependencyObject owner) => (bool)owner.GetValue(IsPressOffsetEnabledProperty);
    public static void SetIsPressOffsetEnabled(DependencyObject owner, bool value) => owner.SetValue(IsPressOffsetEnabledProperty, value);
}
