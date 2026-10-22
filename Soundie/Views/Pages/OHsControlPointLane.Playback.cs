using System.Windows;
using System.Windows.Media;
using Soundie.Resources.Helpers;

namespace Soundie.Views.Pages;

public partial class OHsControlPointLane
{
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(nameof(Position), typeof(TimeSpan), typeof(OHsControlPointLane),
        new PropertyMetadata(TimeSpan.Zero, (d, _) => ((OHsControlPointLane)d).UpdatePlaybackMarker()));
    public static readonly DependencyProperty PlaybackMarkerBrushProperty = DependencyProperty.Register(nameof(PlaybackMarkerBrush), typeof(Brush), typeof(OHsControlPointLane));
    public static readonly DependencyProperty PlaybackMarkerOutlineBrushProperty = DependencyProperty.Register(nameof(PlaybackMarkerOutlineBrush), typeof(Brush), typeof(OHsControlPointLane));

    public TimeSpan Position { get => (TimeSpan)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public Brush? PlaybackMarkerBrush { get => (Brush?)GetValue(PlaybackMarkerBrushProperty); set => SetValue(PlaybackMarkerBrushProperty, value); }
    public Brush? PlaybackMarkerOutlineBrush { get => (Brush?)GetValue(PlaybackMarkerOutlineBrushProperty); set => SetValue(PlaybackMarkerOutlineBrushProperty, value); }
    internal double? PlaybackMarkerX => OHsControlPointLayout.GetPlaybackMarkerX(Position.TotalMilliseconds, DurationMilliseconds, ContentWidth, HorizontalOffset, ViewportWidth);

    private void UpdatePlaybackMarker()
    {
        if (PlaybackMarker is null) return;
        var x = PlaybackMarkerX;
        PlaybackMarker.Visibility = x.HasValue ? Visibility.Visible : Visibility.Collapsed;
        PlaybackMarkerTranslation.X = (x ?? 0) - PlaybackMarker.Width / 2;
    }
}
