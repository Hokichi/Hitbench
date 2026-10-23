using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Soundie.Views.Shell;

public partial class OHsShellHeader : UserControl
{
    public OHsShellHeader() { InitializeComponent(); UpdateState(); }
    public static readonly DependencyProperty IsWorkstationVisibleProperty = DependencyProperty.Register(nameof(IsWorkstationVisible), typeof(bool), typeof(OHsShellHeader), new PropertyMetadata(false, (d, _) => ((OHsShellHeader)d).UpdateState()));
    public bool IsWorkstationVisible { get => (bool)GetValue(IsWorkstationVisibleProperty); set => SetValue(IsWorkstationVisibleProperty, value); }
    public static readonly DependencyProperty CanGoBackProperty = DependencyProperty.Register(nameof(CanGoBack), typeof(bool), typeof(OHsShellHeader), new PropertyMetadata(false, (d, _) => ((OHsShellHeader)d).UpdateState()));
    public bool CanGoBack { get => (bool)GetValue(CanGoBackProperty); set => SetValue(CanGoBackProperty, value); }
    public static readonly DependencyProperty IsMaximizedProperty = DependencyProperty.Register(nameof(IsMaximized), typeof(bool), typeof(OHsShellHeader), new PropertyMetadata(false, (d, _) => ((OHsShellHeader)d).UpdateState()));
    public bool IsMaximized { get => (bool)GetValue(IsMaximizedProperty); set => SetValue(IsMaximizedProperty, value); }
    public static readonly RoutedEvent GoBackRequestedEvent = EventManager.RegisterRoutedEvent(nameof(GoBackRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(OHsShellHeader));
    public event RoutedEventHandler GoBackRequested { add => AddHandler(GoBackRequestedEvent, value); remove => RemoveHandler(GoBackRequestedEvent, value); }
    private void OnGoBackClick(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(GoBackRequestedEvent, this));
    public static readonly RoutedEvent MinimizeRequestedEvent = EventManager.RegisterRoutedEvent(nameof(MinimizeRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(OHsShellHeader));
    public event RoutedEventHandler MinimizeRequested { add => AddHandler(MinimizeRequestedEvent, value); remove => RemoveHandler(MinimizeRequestedEvent, value); }
    private void OnMinimizeClick(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(MinimizeRequestedEvent, this));
    public static readonly RoutedEvent MaximizeRestoreRequestedEvent = EventManager.RegisterRoutedEvent(nameof(MaximizeRestoreRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(OHsShellHeader));
    public event RoutedEventHandler MaximizeRestoreRequested { add => AddHandler(MaximizeRestoreRequestedEvent, value); remove => RemoveHandler(MaximizeRestoreRequestedEvent, value); }
    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(MaximizeRestoreRequestedEvent, this));
    public static readonly RoutedEvent CloseRequestedEvent = EventManager.RegisterRoutedEvent(nameof(CloseRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(OHsShellHeader));
    public event RoutedEventHandler CloseRequested { add => AddHandler(CloseRequestedEvent, value); remove => RemoveHandler(CloseRequestedEvent, value); }
    private void OnCloseClick(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(CloseRequestedEvent, this));
    public static readonly DependencyProperty IsLightThemeProperty = DependencyProperty.Register(nameof(IsLightTheme), typeof(bool), typeof(OHsShellHeader));
    public bool IsLightTheme { get => (bool)GetValue(IsLightThemeProperty); set => SetValue(IsLightThemeProperty, value); }
    public static readonly RoutedEvent ThemeToggleRequestedEvent = EventManager.RegisterRoutedEvent(nameof(ThemeToggleRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(OHsShellHeader));
    public event RoutedEventHandler ThemeToggleRequested { add => AddHandler(ThemeToggleRequestedEvent, value); remove => RemoveHandler(ThemeToggleRequestedEvent, value); }
    private void OnThemeToggleClick(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(ThemeToggleRequestedEvent, this));
    private void UpdateState()
    {
        if (!IsInitialized) return;
        WorkstationActions.Visibility = IsWorkstationVisible ? Visibility.Visible : Visibility.Collapsed;
        var label = IsMaximized ? "Restore" : "Maximize";
        MaximizeRestoreButton.ButtonText = label;
        MaximizeRestoreButton.ButtonIcon = (Geometry)FindResource(IsMaximized ? "CompressAlt" : "ExpandAlt");
        MaximizeRestoreButton.ToolTip = label;
        AutomationProperties.SetName(MaximizeRestoreButton, label);
    }
}
