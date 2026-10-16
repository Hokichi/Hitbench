using System.Windows;
using System.Windows.Controls;
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
    private void OnClearTrackMutesUnchecked(object sender, RoutedEventArgs e)
    {
        if (viewModel.ClearTrackMutesCommand.CanExecute(null)) viewModel.ClearTrackMutesCommand.Execute(null);
    }
    private void OnClearTrackSolosUnchecked(object sender, RoutedEventArgs e)
    {
        if (viewModel.ClearTrackSolosCommand.CanExecute(null)) viewModel.ClearTrackSolosCommand.Execute(null);
    }

    private void OnEditingModeChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radio) viewModel.IsAdditionMode = radio.Name == "AdditionModeButton";
    }

}
