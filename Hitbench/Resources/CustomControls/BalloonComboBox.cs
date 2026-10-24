using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Hitbench.Resources.CustomControls;

/// <summary>A balloon action trigger. Supply an instance-owned ContextMenu using OHsActionContextMenuStyle.</summary>
public class BalloonComboBox : BalloonButton
{
    private static readonly DependencyPropertyKey IsDropDownOpenPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsDropDownOpen), typeof(bool), typeof(BalloonComboBox), new PropertyMetadata(false));
    public static readonly DependencyProperty IsDropDownOpenProperty = IsDropDownOpenPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey IsMenuOwnerActivePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsMenuOwnerActive), typeof(bool), typeof(BalloonComboBox), new PropertyMetadata(false));
    public static readonly DependencyProperty IsMenuOwnerActiveProperty = IsMenuOwnerActivePropertyKey.DependencyProperty;

    private bool attached;
    private ContextMenu? menu;
    private UIElement? previousPlacementTarget;
    private DispatcherOperation? pendingClose;
    private long menuSession;

    public bool IsDropDownOpen => (bool)GetValue(IsDropDownOpenProperty);
    public bool IsMenuOwnerActive => (bool)GetValue(IsMenuOwnerActiveProperty);
    internal long MenuOwnerVersion { get; private set; }

    static BalloonComboBox() => DefaultStyleKeyProperty.OverrideMetadata(typeof(BalloonComboBox),
        new FrameworkPropertyMetadata(typeof(BalloonComboBox)));

    public BalloonComboBox()
    {
        Loaded += (_, _) => { attached = true; RefreshOwner(); };
        Unloaded += (_, _) => { attached = false; DeactivateOwner(); };
        IsEnabledChanged += (_, _) => RefreshOwner();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new BalloonComboBoxAutomationPeer(this);

    public override void OnApplyTemplate()
    {
        DeactivateOwner();
        base.OnApplyTemplate();
        RefreshOwner();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property != ContextMenuProperty) return;
        DeactivateOwner();
        RefreshOwner();
    }

    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        // Only trigger activation opens this menu, always below the balloon.
        e.Handled = true;
        base.OnContextMenuOpening(e);
    }

    internal void InvokeActionMenu()
    {
        if (IsMenuOwnerActive) OnClick();
    }

    protected override void OnClick()
    {
        if (!IsMenuOwnerActive || menu is not { HasItems: true } || IsDropDownOpen) return;
        base.OnClick();
        // A consumer Click/Command can detach the owner or replace its menu.
        if (!IsMenuOwnerActive || menu is not { HasItems: true }) return;
        menuSession++;
        SetValue(IsDropDownOpenPropertyKey, true);
        try { SetMenuOpen(menu, true); }
        catch
        {
            SetValue(IsDropDownOpenPropertyKey, false);
            throw;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape && IsDropDownOpen)
        {
            CloseMenu(restoreFocus: true);
            e.Handled = true;
        }
        else if (key is Key.Down or Key.Enter && IsMenuOwnerActive)
        {
            InvokeActionMenu();
            e.Handled = true;
        }
        else base.OnKeyDown(e);
    }

    /// <summary>Native presentation boundary; non-displaying tests replace this operation.</summary>
    protected virtual void SetMenuOpen(ContextMenu actionMenu, bool isOpen) =>
        actionMenu.SetCurrentValue(ContextMenu.IsOpenProperty, isOpen);

    protected virtual void RestoreTriggerFocus() => Focus();

    private void RefreshOwner()
    {
        if (!attached || !IsEnabled) { DeactivateOwner(); return; }
        SetValue(IsMenuOwnerActivePropertyKey, true);
        if (menu is not null || ContextMenu is null) return;
        menu = ContextMenu;
        previousPlacementTarget = menu.PlacementTarget;
        menu.SetCurrentValue(ContextMenu.PlacementTargetProperty, this);
        menu.SetCurrentValue(ContextMenu.PlacementProperty, PlacementMode.Bottom);
        menu.Closed += OnMenuClosed;
        menu.PreviewKeyDown += OnMenuKeyDown;
        menu.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler(OnItemClick), handledEventsToo: true);
    }

    private void DeactivateOwner()
    {
        MenuOwnerVersion++;
        SetValue(IsMenuOwnerActivePropertyKey, false);
        pendingClose?.Abort();
        pendingClose = null;
        CloseMenu(restoreFocus: false);
        if (menu is null) return;
        menu.Closed -= OnMenuClosed;
        menu.PreviewKeyDown -= OnMenuKeyDown;
        menu.RemoveHandler(MenuItem.ClickEvent, new RoutedEventHandler(OnItemClick));
        if (ReferenceEquals(menu.PlacementTarget, this))
            menu.SetCurrentValue(ContextMenu.PlacementTargetProperty, previousPlacementTarget);
        previousPlacementTarget = null;
        menu = null;
    }

    private void CloseMenu(bool restoreFocus)
    {
        if (!IsDropDownOpen) return;
        SetValue(IsDropDownOpenPropertyKey, false);
        if (menu is not null) SetMenuOpen(menu, false);
        if (restoreFocus && IsMenuOwnerActive) RestoreTriggerFocus();
    }

    private void OnMenuClosed(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, menu)) SetValue(IsDropDownOpenPropertyKey, false);
    }

    private void OnMenuKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CloseMenu(restoreFocus: true);
        e.Handled = true;
    }

    private void OnItemClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not MenuItem { HasItems: false } || !IsMenuOwnerActive) return;
        var version = MenuOwnerVersion;
        var session = menuSession;
        // Native Click precedes command execution. Close/focus afterward; never interfere with that command.
        pendingClose?.Abort();
        pendingClose = Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            pendingClose = null;
            if (!IsMenuOwnerActive || version != MenuOwnerVersion || session != menuSession) return;
            CloseMenu(restoreFocus: false);
            // A command may have opened another workflow. Do not take its keyboard focus.
            if (Keyboard.FocusedElement is null || IsKeyboardFocusWithin || menu?.IsKeyboardFocusWithin == true)
                RestoreTriggerFocus();
        });
    }
}
