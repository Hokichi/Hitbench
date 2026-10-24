using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Onset.Resources.CustomControls;
using Onset.Services.Ui;

namespace Onset.Views.Pages;

public partial class HomePage
{
    private static readonly DependencyPropertyKey IsCompactLayoutPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsCompactLayout), typeof(bool), typeof(HomePage), new PropertyMetadata(false));
    public static readonly DependencyProperty IsCompactLayoutProperty = IsCompactLayoutPropertyKey.DependencyProperty;
    public bool IsCompactLayout => (bool)GetValue(IsCompactLayoutProperty);

    private void UpdateResponsiveLayout(Size availableSize)
    {
        if (!double.IsFinite(availableSize.Width) || availableSize.Width <= 0) return;
        var compact = availableSize.Width < 960;
        if (compact == IsCompactLayout) return;
        SetValue(IsCompactLayoutPropertyKey, compact);
        PageLayout.ColumnDefinitions[0].Width = new GridLength(compact ? 1 : 7, GridUnitType.Star);
        PageLayout.ColumnDefinitions[1].MinWidth = compact ? 0 : 320;
        PageLayout.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(3, GridUnitType.Star);
        PageLayout.RowDefinitions[1].Height = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        lastSongsLayout = null;
        tileLayout = null;
    }

    public void UpdateSongsLayout(Size maximizedSize)
    {
        UpdateResponsiveLayout(RenderSize);
        if (updatingLayout || !double.IsFinite(maximizedSize.Width) || !double.IsFinite(maximizedSize.Height) ||
            maximizedSize.Width <= 0 || maximizedSize.Height <= 0 || SongsList.ActualWidth <= 0 ||
            !GetSongsParts()) return;
        var panel = songsPanel!;
        var viewer = songsViewer!;
        var scrollbar = viewer.Template?.FindName("PART_VerticalScrollBar", viewer) as ScrollBar;
        var lane = scrollbar?.ActualWidth ?? SystemParameters.VerticalScrollBarWidth;
        if (lane <= 0) lane = SystemParameters.VerticalScrollBarWidth;
        var nativeLane = Math.Max(0, SongsList.ActualWidth - panel.ViewportWidth -
            SongsList.Padding.Left - SongsList.Padding.Right);
        lane = Math.Max(lane, nativeLane);
        var headerHeight = SongsContent.RowDefinitions[0].ActualHeight + SongsContent.RowDefinitions[1].ActualHeight +
            SongsContent.RowDefinitions[2].ActualHeight;
        var inputs = (RenderSize, SongsList.RenderSize, maximizedSize, headerHeight, lane, nativeLane,
            SongsList.Padding, FlowDirection, (object)panel, (object?)viewer.Template,
            SongsPanel.Padding, SongsPanel.BorderThickness, SongsPanel.Margin, PageLayout.Margin,
            (IsCompactLayout ? 0 : PageLayout.ColumnDefinitions[1].ActualWidth), IsCompactLayout, panel.HorizontalGap, panel.VerticalGap);
        if (lastSongsLayout == inputs) return;
        lastSongsLayout = inputs;
        CancelSongsScrollMotion();
        updatingLayout = true;
        SongsLayoutCalculationCount++;
        try
        {
            // Native viewers reserve their own lane; the shared overlay viewer needs padding instead.
            var reservedPadding = Math.Max(0, lane - nativeLane);
            var padding = FlowDirection == FlowDirection.RightToLeft
                ? new Thickness(reservedPadding, 0, 0, 0) : new Thickness(0, 0, reservedPadding, 0);
            if (SongsList.Padding != padding) SongsList.Padding = padding;

            var horizontalChrome = SongsPanel.Padding.Left + SongsPanel.Padding.Right +
                SongsPanel.BorderThickness.Left + SongsPanel.BorderThickness.Right;
            var verticalChrome = SongsPanel.Padding.Top + SongsPanel.Padding.Bottom +
                SongsPanel.BorderThickness.Top + SongsPanel.BorderThickness.Bottom;
            var outsideWidth = PageLayout.Margin.Left + PageLayout.Margin.Right +
                (IsCompactLayout ? 0 : PageLayout.ColumnDefinitions[1].ActualWidth) + SongsPanel.Margin.Left + SongsPanel.Margin.Right;
            var outsideHeight = PageLayout.Margin.Top + PageLayout.Margin.Bottom +
                SongsPanel.Margin.Top + SongsPanel.Margin.Bottom;
            if (tileLayout is null || maximizedPageSize != maximizedSize ||
                (Math.Abs(ActualWidth - maximizedSize.Width) < .1 && Math.Abs(baselineHeaderHeight - headerHeight) > .01))
            {
                var width = maximizedSize.Width - outsideWidth - horizontalChrome - lane;
                var height = maximizedSize.Height - outsideHeight - verticalChrome - headerHeight;
                if (width <= 3 * panel.HorizontalGap || height <= 3 * panel.VerticalGap) return;
                tileLayout = new SongTileLayout(width, height, panel.HorizontalGap, panel.VerticalGap);
                baselineHeaderHeight = headerHeight;
                maximizedPageSize = maximizedSize;
            }
            var viewportWidth = Math.Max(1, SongsList.ActualWidth - lane);
            SetLayoutValue(panel, SpacedUniformGrid.ColumnsProperty, tileLayout.GetColumns(viewportWidth));
            SetLayoutValue(panel, SpacedUniformGrid.ItemWidthProperty, Math.Min(viewportWidth, tileLayout.GetTileWidth(viewportWidth)));
            SetLayoutValue(panel, SpacedUniformGrid.ItemHeightProperty, tileLayout.TileHeight);
        }
        finally { updatingLayout = false; }
    }

    private static void SetLayoutValue(DependencyObject target, DependencyProperty property, object value)
    {
        if (!Equals(target.GetValue(property), value)) target.SetCurrentValue(property, value);
    }
}
