using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using osuHitsounder.Resources.Helpers;

namespace osuHitsounder.Resources.CustomControls;

/// <summary>
///     A container that reveals left or right action panels when the user
///     presses and drags horizontally (swipe gesture).
///     Swipe left  â†’ reveals the <see cref="RightContent" /> panel.
///     Swipe right â†’ reveals the <see cref="LeftContent" /> panel.
/// </summary>
[TemplatePart(Name = PartContentBorder, Type = typeof(Border))]
public class SwipeRevealContainer : ContentControl
{
    private const string PartContentBorder = "PART_ContentBorder";
    private const double SwipeThreshold = 40;
    private const double RevealWidth = 52;
    private static readonly Duration AnimationDuration = new(TimeSpan.FromMilliseconds(200));

    public static readonly DependencyProperty LeftContentProperty =
        DependencyProperty.Register(nameof(LeftContent), typeof(object), typeof(SwipeRevealContainer));

    public static readonly DependencyProperty RightContentProperty =
        DependencyProperty.Register(nameof(RightContent), typeof(object), typeof(SwipeRevealContainer));

    public static readonly DependencyProperty IsRevealedProperty =
        DependencyProperty.Register(nameof(IsRevealed), typeof(bool), typeof(SwipeRevealContainer),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsLeftContentRevealedProperty =
        DependencyProperty.Register(nameof(IsLeftContentRevealed), typeof(bool), typeof(SwipeRevealContainer),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsRightContentRevealedProperty =
        DependencyProperty.Register(nameof(IsRightContentRevealed), typeof(bool), typeof(SwipeRevealContainer),
            new PropertyMetadata(false));

    private static SwipeRevealContainer? _currentlyRevealed;
    private readonly TranslateTransform _translateTransform = new();

    private Border? _contentBorder;
    private double _currentOffset;
    private bool _isDragging;
    private bool _isPointerDown;
    private Point _startPoint;

    static SwipeRevealContainer()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SwipeRevealContainer),
            new FrameworkPropertyMetadata(typeof(SwipeRevealContainer)));
    }

    public object LeftContent
    {
        get => GetValue(LeftContentProperty);
        set => SetValue(LeftContentProperty, value);
    }

    public object RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }

    public bool IsRevealed
    {
        get => (bool)GetValue(IsRevealedProperty);
        set => SetValue(IsRevealedProperty, value);
    }

    public bool IsLeftContentRevealed
    {
        get => (bool)GetValue(IsLeftContentRevealedProperty);
        set => SetValue(IsLeftContentRevealedProperty, value);
    }

    public bool IsRightContentRevealed
    {
        get => (bool)GetValue(IsRightContentRevealedProperty);
        set => SetValue(IsRightContentRevealedProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _contentBorder = GetTemplateChild(PartContentBorder) as Border;
        if (_contentBorder is not null)
            _contentBorder.RenderTransform = _translateTransform;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);

        // Don't start swipe if the click is on a button
        if (e.OriginalSource is DependencyObject source && DependencyObjectTree.FindAncestor<ButtonBase>(source) is not null)
            return;

        _startPoint = e.GetPosition(this);
        _isDragging = false;
        _isPointerDown = true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);

        if (!_isPointerDown)
            return;

        var currentPoint = e.GetPosition(this);
        var deltaX = currentPoint.X - _startPoint.X;

        if (!_isDragging && Math.Abs(deltaX) > 5)
        {
            _isDragging = true;
            CaptureMouse();
        }

        if (!_isDragging)
            return;

        // Clamp offset: negative = swipe left (reveal right), positive = swipe right (reveal left)
        var offset = _currentOffset + deltaX;
        offset = Math.Max(-RevealWidth, Math.Min(RevealWidth, offset));
        _translateTransform.X = offset;

        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);

        if (!_isPointerDown)
            return;

        var wasDragging = _isDragging;
        _isPointerDown = false;
        _isDragging = false;

        if (IsMouseCaptured)
            ReleaseMouseCapture();

        if (!wasDragging)
        {
            // Click (no drag): toggle swipe state
            // If currently revealed â†’ restore; otherwise â†’ swipe right to reveal left panel
            if (e.OriginalSource is DependencyObject source && DependencyObjectTree.FindAncestor<ButtonBase>(source) is not null)
                return;

            var targetOffset = _currentOffset != 0 ? 0 : RevealWidth;
            AnimateTo(targetOffset);
            e.Handled = true;
            return;
        }

        var currentPoint = e.GetPosition(this);
        var deltaX = currentPoint.X - _startPoint.X;
        var totalOffset = _currentOffset + deltaX;

        double snapTarget;
        if (totalOffset < -SwipeThreshold)
            snapTarget = -RevealWidth; // Reveal right panel
        else if (totalOffset > SwipeThreshold)
            snapTarget = RevealWidth; // Reveal left panel
        else
            snapTarget = 0; // Snap back

        AnimateTo(snapTarget);
        e.Handled = true;
    }

    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonDown(e);

        if (e.OriginalSource is DependencyObject source && DependencyObjectTree.FindAncestor<ButtonBase>(source) is not null)
            return;

        AnimateTo(-RevealWidth);
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        if (!_isPointerDown)
            return;

        _isPointerDown = false;
        _isDragging = false;

        if (IsMouseCaptured)
            ReleaseMouseCapture();

        AnimateTo(0);
    }

    /// <summary>
    ///     Resets the swipe position back to center.
    /// </summary>
    public void ResetSwipe()
    {
        AnimateTo(0);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Right when LeftContent is not null:
                AnimateTo(RevealWidth);
                e.Handled = true;
                break;
            case Key.Left when RightContent is not null:
                AnimateTo(-RevealWidth);
                e.Handled = true;
                break;
            case Key.Escape when IsRevealed:
                AnimateTo(0);
                e.Handled = true;
                break;
        }
    }

    private void AnimateTo(double targetX)
    {
        // Reset the previously revealed item if a different one is being revealed
        if (targetX != 0 && _currentlyRevealed is not null && _currentlyRevealed != this)
            _currentlyRevealed.ResetSwipe();

        if (!SystemParameters.ClientAreaAnimation)
        {
            _translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _translateTransform.X = targetX;
            UpdateRevealState(targetX);
            return;
        }

        var animation = new DoubleAnimation(targetX, AnimationDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        animation.Completed += (_, _) => _currentOffset = targetX;
        _translateTransform.BeginAnimation(TranslateTransform.XProperty, animation);
        UpdateRevealState(targetX);
    }

    private void UpdateRevealState(double targetX)
    {
        _currentOffset = targetX;
        IsRevealed = targetX != 0;
        IsLeftContentRevealed = targetX > 0;
        IsRightContentRevealed = targetX < 0;
        _currentlyRevealed = targetX != 0 ? this : null;
    }
}
