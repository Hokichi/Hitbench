using System.Windows;

namespace Onset.Resources.Helpers;

public static class OHsTrackDragPresentation
{
    public static readonly DependencyProperty IsSourceProperty = DependencyProperty.RegisterAttached(
        "IsSource", typeof(bool), typeof(OHsTrackDragPresentation), new PropertyMetadata(false));
    public static readonly DependencyProperty GapBeforeProperty = DependencyProperty.RegisterAttached(
        "GapBefore", typeof(double), typeof(OHsTrackDragPresentation), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty GapAfterProperty = DependencyProperty.RegisterAttached(
        "GapAfter", typeof(double), typeof(OHsTrackDragPresentation), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static bool GetIsSource(DependencyObject owner) => (bool)owner.GetValue(IsSourceProperty);
    public static void SetIsSource(DependencyObject owner, bool value) => owner.SetValue(IsSourceProperty, value);
    public static double GetGapBefore(DependencyObject owner) => (double)owner.GetValue(GapBeforeProperty);
    public static void SetGapBefore(DependencyObject owner, double value) => owner.SetValue(GapBeforeProperty, value);
    public static double GetGapAfter(DependencyObject owner) => (double)owner.GetValue(GapAfterProperty);
    public static void SetGapAfter(DependencyObject owner, double value) => owner.SetValue(GapAfterProperty, value);
}
