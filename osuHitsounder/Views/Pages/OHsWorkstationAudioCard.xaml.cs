using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Views.Pages;

public partial class OHsWorkstationAudioCard : UserControl
{
    private WorkstationPageVM viewModel => (WorkstationPageVM)DataContext;
    public OHsWorkstationAudioCard() { InitializeComponent(); }
    public OHsWorkstationTimeline Timeline => TimelineControl;
    public Thickness AudioCardBorderThickness => SongAudioCard.BorderThickness;
    public static readonly DependencyProperty TrackScrollBarGutterProperty = DependencyProperty.Register(nameof(TrackScrollBarGutter), typeof(double), typeof(OHsWorkstationAudioCard), new PropertyMetadata(0d));
    public double TrackScrollBarGutter { get => (double)GetValue(TrackScrollBarGutterProperty); set => SetValue(TrackScrollBarGutterProperty, value); }
    public bool ContainsInputOrigin(DependencyObject? origin) => IsWithin(origin, this);
    public bool IsCollapseActionOrigin(DependencyObject? origin) => IsWithin(origin, ToggleTracksCollapsedButton);
    private static bool IsWithin(DependencyObject? origin, DependencyObject owner)
    {
        for (var current = origin; current is not null; current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (ReferenceEquals(current, owner)) return true;
        return false;
    }
    public bool FocusCollapseAction()
    {
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(ToggleTracksCollapsedButton), ToggleTracksCollapsedButton);
        return ToggleTracksCollapsedButton.Focus();
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

    private void OnEditingModeChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radio) viewModel.IsAdditionMode = radio.Name == "AdditionModeButton";
    }

}
