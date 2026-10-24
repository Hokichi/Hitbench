using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Hitbench.Resources.CustomControls;

public partial class NumericUpDown
{
    public static readonly DependencyProperty ClampIntegerToRangeProperty = DependencyProperty.Register(
        nameof(ClampIntegerToRange), typeof(bool), typeof(NumericUpDown), new PropertyMetadata(false));
    public static readonly DependencyProperty IsVerticalDragEnabledProperty = DependencyProperty.Register(
        nameof(IsVerticalDragEnabled), typeof(bool), typeof(NumericUpDown), new PropertyMetadata(false,
            (d, e) => { if (!(bool)e.NewValue) ((NumericUpDown)d).CancelVerticalDrag(); }));
    private static readonly DependencyPropertyKey IsVerticalDragActivePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsVerticalDragActive), typeof(bool), typeof(NumericUpDown), new PropertyMetadata(false));
    public static readonly DependencyProperty IsVerticalDragActiveProperty = IsVerticalDragActivePropertyKey.DependencyProperty;
    public bool ClampIntegerToRange { get => (bool)GetValue(ClampIntegerToRangeProperty); set => SetValue(ClampIntegerToRangeProperty, value); }
    public bool IsVerticalDragEnabled { get => (bool)GetValue(IsVerticalDragEnabledProperty); set => SetValue(IsVerticalDragEnabledProperty, value); }
    public bool IsVerticalDragActive => (bool)GetValue(IsVerticalDragActiveProperty);
    private bool _dragPending;
    private double _lastDragY;
    private double _dragRemainder;
    private Window? _dragOwner;

    private void InitializeVerticalDrag()
    {
        IsEnabledChanged += (_, _) => { if (!IsEnabled) CancelVerticalDrag(); };
        Loaded += (_, _) =>
        {
            DetachDragOwner();
            _dragOwner = Window.GetWindow(this);
            if (_dragOwner is not null) _dragOwner.Deactivated += OnDragOwnerDeactivated;
        };
        Unloaded += (_, _) => { CancelVerticalDrag(); DetachDragOwner(); };
    }

    private void OnDragOwnerDeactivated(object? sender, EventArgs e) => CancelVerticalDrag();
    private void DetachDragOwner()
    {
        if (_dragOwner is not null) _dragOwner.Deactivated -= OnDragOwnerDeactivated;
        _dragOwner = null;
    }

    internal void BeginVerticalDrag(double y, int clickCount, bool isArrowSource)
    {
        CancelVerticalDrag();
        if (!IsEnabled || !IsVerticalDragEnabled || clickCount != 1 || isArrowSource) return;
        _dragPending = true;
        _lastDragY = y;
    }

    internal bool ContinueVerticalDrag(double y, bool leftPressed)
    {
        if (!leftPressed || !IsEnabled || !IsVerticalDragEnabled) { CancelVerticalDrag(); return false; }
        if (!_dragPending && !IsVerticalDragActive) return false;
        if (!IsVerticalDragActive)
        {
            if (Math.Abs(y - _lastDragY) < SystemParameters.MinimumVerticalDragDistance) return false;
            if (!TryCaptureVerticalDragPointer()) { CancelVerticalDrag(); return false; }
            SetValue(IsVerticalDragActivePropertyKey, true);
            _dragPending = false;
            if (!CommitInput()) { CancelVerticalDrag(); return false; }
        }

        _dragRemainder += _lastDragY - y;
        _lastDragY = y;
        var steps = Math.Truncate(_dragRemainder / 4);
        if (steps != 0)
        {
            var proposed = Value + (decimal)steps * Step;
            var accepted = CoerceValueWithinLimits(proposed, LowerLimit, UpperLimit);
            _dragRemainder -= steps * 4;
            if (accepted == LowerLimit || accepted == UpperLimit) _dragRemainder = 0;
            SetCurrentValue(ValueProperty, accepted);
        }
        return true;
    }

    internal void CancelVerticalDrag()
    {
        _dragPending = false;
        _dragRemainder = 0;
        if (!IsVerticalDragActive) return;
        SetValue(IsVerticalDragActivePropertyKey, false);
        ReleaseVerticalDragPointer();
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (!e.Handled) BeginVerticalDrag(e.GetPosition(this).Y, e.ClickCount, IsArrowSource(e.OriginalSource as DependencyObject));
    }

    private bool IsArrowSource(DependencyObject? source)
    {
        while (source is not null && source != this)
        {
            if (source == _upButton || source == _downButton || source is RepeatButton) return true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (ContinueVerticalDrag(e.GetPosition(this).Y, e.LeftButton == MouseButtonState.Pressed)) e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (IsVerticalDragActive) e.Handled = true;
        CancelVerticalDrag();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (e.OriginalSource == this) CancelVerticalDrag();
    }
    protected virtual bool TryCaptureVerticalDragPointer() => CaptureMouse();
    protected virtual void ReleaseVerticalDragPointer() => ReleaseMouseCapture();
}
