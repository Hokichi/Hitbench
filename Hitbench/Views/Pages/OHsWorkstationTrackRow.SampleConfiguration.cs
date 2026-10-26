using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Views.Pages;

public partial class OHsWorkstationTrackRow
{
    internal Action<bool, UIElement?>? PresentSampleConfiguration { get; set; }
    internal bool IsSampleConfigurationOpen { get; private set; }
    private Guid? configurationToken;
    private WorkstationPageVM? configurationOwner;
    private Window? configurationWindow;
    private DispatcherOperation? configurationDismissal;
    private DispatcherOperation? configurationOpening;
    private bool awaitingConfigurationInput;

    internal bool OpenSampleConfiguration()
    {
        if (IsSampleConfigurationOpen) return true;
        if (PresentSampleConfiguration is null && PresentationSource.FromVisual(this) is not HwndSource) return false;
        InvalidateSampleConfiguration();
        if (Track is not { } track || ViewModel is not { } owner || owner.BeginSampleConfiguration(track) is not { } token) return false;
        configurationOwner = owner; configurationToken = token; IsSampleConfigurationOpen = true;
        RefreshSampleConfigurationSelectors();
        var invoker = FindVisualChild<StackPanel>(this, "TrackLeftControls");
        configurationWindow = Window.GetWindow(this);
        if (configurationWindow is not null) configurationWindow.Deactivated += OnConfigurationWindowDeactivated;
        try
        {
            if (PresentSampleConfiguration is { } present) present(true, invoker);
            else
            {
                SampleConfigurationPopup.PlacementTarget = invoker ?? (UIElement)this;
                SampleConfigurationPopup.IsOpen = true;
            }
            return true;
        }
        catch { InvalidateSampleConfiguration(); throw; }
    }

    internal void CloseSampleConfiguration(bool restoreFocus = true)
    {
        if (IsSampleConfigurationOpen)
        {
            IsSampleConfigurationOpen = false;
            foreach (var selector in ConfigurationSelectors()) selector.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
            if (PresentSampleConfiguration is { } present) present(false, this);
            else SampleConfigurationPopup.IsOpen = false;
            SampleConfigurationPopup.PlacementTarget = null;
        }
        if (restoreFocus)
        {
            CompleteSampleConfigurationDismissal();
            FindVisualChild<Resources.CustomControls.BalloonComboBox>(this)?.Focus();
        }
        else if (configurationToken is not null && PresentSampleConfiguration is null && !awaitingConfigurationInput)
        {
            awaitingConfigurationInput = true;
            InputManager.Current.PreProcessInput += OnConfigurationDismissalInput;
            ScheduleConfigurationDismissal();
        }
    }

    internal void CompleteSampleConfigurationDismissal()
    {
        if (IsSampleConfigurationOpen) return;
        configurationDismissal?.Abort(); configurationDismissal = null;
        if (awaitingConfigurationInput) InputManager.Current.PreProcessInput -= OnConfigurationDismissalInput;
        awaitingConfigurationInput = false;
        if (configurationWindow is not null) configurationWindow.Deactivated -= OnConfigurationWindowDeactivated;
        configurationWindow = null;
        var token = configurationToken; var owner = configurationOwner;
        configurationToken = null; configurationOwner = null;
        if (token is { } value) owner?.EndSampleConfiguration(value);
    }

    private void InvalidateSampleConfiguration()
    {
        configurationOpening?.Abort(); configurationOpening = null;
        CloseSampleConfiguration(false);
        CompleteSampleConfigurationDismissal();
    }

    private void ScheduleConfigurationDismissal()
    {
        if (Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed || Mouse.MiddleButton == MouseButtonState.Pressed) return;
        var token = configurationToken;
        configurationDismissal?.Abort();
        configurationDismissal = Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (configurationToken == token && !IsSampleConfigurationOpen) CompleteSampleConfigurationDismissal();
        });
    }

    private void OnConfigurationDismissalInput(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is MouseButtonEventArgs mouse)
        {
            mouse.Handled = true;
            if (mouse.ButtonState == MouseButtonState.Released) ScheduleConfigurationDismissal();
        }
    }

    internal bool HandleSampleConfigurationEscape(bool? dropdownIsOpen = null)
    {
        if (!IsSampleConfigurationOpen) return false;
        // A nested native ComboBox handles its own first Escape before the containing popup closes.
        if (dropdownIsOpen ?? ConfigurationSelectors().Any(selector => selector.IsDropDownOpen)) return false;
        CloseSampleConfiguration(); return true;
    }

    private ComboBox[] ConfigurationSelectors() => [TrackSampleFileSelector, TrackAdditionSelector, TrackSampleTypeSelector];
    private void RefreshSampleConfigurationSelectors()
    {
        if (ViewModel is null || Track is null) return;
        OnSampleSelectorLoaded(TrackSampleFileSelector, new RoutedEventArgs());
        OnTrackMetadataSelectorLoaded(TrackAdditionSelector, new RoutedEventArgs());
        OnTrackMetadataSelectorLoaded(TrackSampleTypeSelector, new RoutedEventArgs());
    }
    private void OnConfigSampleClick(object sender, RoutedEventArgs e)
    {
        var track = Track; var owner = ViewModel;
        configurationOpening?.Abort();
        // The menu releases native capture at Input priority before this anchored popup takes it.
        configurationOpening = Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            configurationOpening = null;
            if (ReferenceEquals(Track, track) && ReferenceEquals(ViewModel, owner)) OpenSampleConfiguration();
        });
        e.Handled = true;
    }
    private void OnSampleConfigurationOpened(object? sender, EventArgs e) => TrackSampleFileSelector.Focus();
    private void OnSampleConfigurationClosed(object? sender, EventArgs e) => CloseSampleConfiguration(false);
    private void OnConfigurationWindowDeactivated(object? sender, EventArgs e) => InvalidateSampleConfiguration();
    private void OnSampleConfigurationKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && HandleSampleConfigurationEscape()) e.Handled = true;
    }
}
