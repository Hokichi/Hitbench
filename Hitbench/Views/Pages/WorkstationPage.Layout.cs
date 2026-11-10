using System.Windows;

namespace Hitbench.Views.Pages;

public partial class WorkstationPage
{
    private static readonly DependencyPropertyKey IsCompactLayoutPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsCompactLayout), typeof(bool), typeof(WorkstationPage), new PropertyMetadata(false));
    public static readonly DependencyProperty IsCompactLayoutProperty = IsCompactLayoutPropertyKey.DependencyProperty;
    public bool IsCompactLayout => (bool)GetValue(IsCompactLayoutProperty);

    private static readonly DependencyPropertyKey IsShortLayoutPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsShortLayout), typeof(bool), typeof(WorkstationPage), new PropertyMetadata(false));
    public static readonly DependencyProperty IsShortLayoutProperty = IsShortLayoutPropertyKey.DependencyProperty;
    public bool IsShortLayout => (bool)GetValue(IsShortLayoutProperty);

    private static readonly DependencyPropertyKey TrackSideColumnWidthPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(TrackSideColumnWidth), typeof(GridLength), typeof(WorkstationPage), new PropertyMetadata(new GridLength(240)));
    public static readonly DependencyProperty TrackSideColumnWidthProperty = TrackSideColumnWidthPropertyKey.DependencyProperty;
    public GridLength TrackSideColumnWidth => (GridLength)GetValue(TrackSideColumnWidthProperty);

    private void UpdateResponsiveLayout(Size availableSize)
    {
        if (double.IsFinite(availableSize.Width) && availableSize.Width > 0)
        {
            var compact = availableSize.Width < 960;
            SetValue(IsCompactLayoutPropertyKey, compact);
            SetValue(TrackSideColumnWidthPropertyKey, new GridLength(240));
        }
        if (double.IsFinite(availableSize.Height) && availableSize.Height > 0)
            SetValue(IsShortLayoutPropertyKey, availableSize.Height < 480);
    }
}
