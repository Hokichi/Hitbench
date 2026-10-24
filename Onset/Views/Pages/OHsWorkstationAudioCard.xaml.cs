using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Onset.Views.Pages;

public partial class OHsWorkstationAudioCard : UserControl
{
    public static readonly DependencyProperty TrackSideColumnWidthProperty = DependencyProperty.Register(
        nameof(TrackSideColumnWidth), typeof(GridLength), typeof(OHsWorkstationAudioCard), new PropertyMetadata(new GridLength(180)));
    public GridLength TrackSideColumnWidth { get => (GridLength)GetValue(TrackSideColumnWidthProperty); set => SetValue(TrackSideColumnWidthProperty, value); }

    public static readonly DependencyProperty IsCompactLayoutProperty = DependencyProperty.Register(
        nameof(IsCompactLayout), typeof(bool), typeof(OHsWorkstationAudioCard), new PropertyMetadata(false));
    public bool IsCompactLayout { get => (bool)GetValue(IsCompactLayoutProperty); set => SetValue(IsCompactLayoutProperty, value); }
    public static readonly DependencyProperty IsShortLayoutProperty = DependencyProperty.Register(
        nameof(IsShortLayout), typeof(bool), typeof(OHsWorkstationAudioCard), new PropertyMetadata(false));
    public bool IsShortLayout { get => (bool)GetValue(IsShortLayoutProperty); set => SetValue(IsShortLayoutProperty, value); }

    public OHsWorkstationAudioCard() { InitializeComponent(); }
    public OHsWorkstationTimeline Timeline => TimelineControl;
    public OHsWorkstationFooter Footer => FooterControls;
    public Thickness AudioCardBorderThickness => SongAudioCard.BorderThickness;
    public static readonly DependencyProperty TrackScrollBarGutterProperty = DependencyProperty.Register(nameof(TrackScrollBarGutter), typeof(double), typeof(OHsWorkstationAudioCard), new PropertyMetadata(0d));
    public double TrackScrollBarGutter { get => (double)GetValue(TrackScrollBarGutterProperty); set => SetValue(TrackScrollBarGutterProperty, value); }
    public bool ContainsInputOrigin(DependencyObject? origin) => IsWithin(origin, SongAudioCard);
    public bool IsCollapseActionOrigin(DependencyObject? origin) => IsWithin(origin, ToggleTracksCollapsedButton) || IsWithin(origin, ProjectActionsButton);
    private static bool IsWithin(DependencyObject? origin, DependencyObject owner)
    {
        for (var current = origin; current is not null; current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (ReferenceEquals(current, owner)) return true;
        return false;
    }
    public bool FocusCollapseAction()
    {
        var target = IsCompactLayout ? (UIElement)ProjectActionsButton : ToggleTracksCollapsedButton;
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(target), target);
        return target.Focus();
    }
    private bool synchronizingAggregate;
    private void OnAggregateToggleChanged(object sender, RoutedEventArgs e)
    {
        if (synchronizingAggregate || sender is not ToggleButton toggle) return;
        var binding = toggle.GetBindingExpression(ToggleButton.IsCheckedProperty);
        if (binding is null) return;
        synchronizingAggregate = true;
        try
        {
            var requested = toggle.IsChecked;
            binding.UpdateTarget();
            if (requested != toggle.IsChecked && toggle.IsEnabled && toggle.Tag is ICommand command && command.CanExecute(null))
                command.Execute(null);
            binding.UpdateTarget();
        }
        finally { synchronizingAggregate = false; }
    }

}
