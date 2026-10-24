using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace Hitbench.Resources.CustomControls;

public sealed class StepNavigatorControl : Control
{
    private int _visibleWindowStart;

    static StepNavigatorControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(StepNavigatorControl),
            new FrameworkPropertyMetadata(typeof(StepNavigatorControl)));
    }

    public static readonly DependencyProperty StepCountProperty =
        DependencyProperty.Register(
            nameof(StepCount),
            typeof(int),
            typeof(StepNavigatorControl),
            new PropertyMetadata(0, OnStepCountChanged));

    public static readonly DependencyProperty CurrentStepProperty =
        DependencyProperty.Register(
            nameof(CurrentStep),
            typeof(int),
            typeof(StepNavigatorControl),
            new PropertyMetadata(0, OnCurrentStepChanged));

    public static readonly DependencyProperty ShouldIndicateProgressProperty =
        DependencyProperty.Register(
            nameof(ShouldIndicateProgress),
            typeof(bool),
            typeof(StepNavigatorControl),
            new PropertyMetadata(true));

    public static readonly DependencyProperty PaginationCountProperty =
        DependencyProperty.Register(
            nameof(PaginationCount),
            typeof(int),
            typeof(StepNavigatorControl),
            new PropertyMetadata(5, OnPaginationChanged));

    public static readonly DependencyProperty ShouldPaginateProperty =
        DependencyProperty.Register(
            nameof(ShouldPaginate),
            typeof(bool),
            typeof(StepNavigatorControl),
            new PropertyMetadata(false, OnPaginationChanged));

    private static readonly DependencyPropertyKey DotsPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(Dots),
            typeof(ObservableCollection<StepNavigatorDotVM>),
            typeof(StepNavigatorControl),
            new PropertyMetadata(null));

    private static readonly DependencyPropertyKey StepCounterTextPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(StepCounterText),
            typeof(string),
            typeof(StepNavigatorControl),
            new PropertyMetadata("0/0"));

    public static readonly DependencyProperty DotsProperty = DotsPropertyKey.DependencyProperty;
    public static readonly DependencyProperty StepCounterTextProperty = StepCounterTextPropertyKey.DependencyProperty;

    public StepNavigatorControl()
    {
        SetValue(DotsPropertyKey, new ObservableCollection<StepNavigatorDotVM>());
    }

    public int StepCount
    {
        get => (int)GetValue(StepCountProperty);
        set => SetValue(StepCountProperty, value);
    }

    public int CurrentStep
    {
        get => (int)GetValue(CurrentStepProperty);
        set => SetValue(CurrentStepProperty, value);
    }

    public bool ShouldIndicateProgress
    {
        get => (bool)GetValue(ShouldIndicateProgressProperty);
        set => SetValue(ShouldIndicateProgressProperty, value);
    }

    public int PaginationCount
    {
        get => (int)GetValue(PaginationCountProperty);
        set => SetValue(PaginationCountProperty, value);
    }

    public bool ShouldPaginate
    {
        get => (bool)GetValue(ShouldPaginateProperty);
        set => SetValue(ShouldPaginateProperty, value);
    }

    public ObservableCollection<StepNavigatorDotVM> Dots =>
        (ObservableCollection<StepNavigatorDotVM>)GetValue(DotsProperty);

    public string StepCounterText => (string)GetValue(StepCounterTextProperty);

    private static void OnStepCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((StepNavigatorControl)d).RefreshDots();
    }

    private static void OnCurrentStepChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((StepNavigatorControl)d).RefreshDots();
    }

    private static void OnPaginationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((StepNavigatorControl)d).RefreshDots();
    }

    private void RefreshDots()
    {
        SetValue(StepCounterTextPropertyKey, BuildStepCounterText(StepCount, CurrentStep));

        var dots = Dots;
        var visibleWindow = CalculateVisibleWindow(StepCount, CurrentStep, PaginationCount, ShouldPaginate);

        if (dots.Count != visibleWindow.Count || _visibleWindowStart != visibleWindow.Start)
        {
            dots.Clear();
            _visibleWindowStart = visibleWindow.Start;

            for (var i = 0; i < visibleWindow.Count; i++)
                dots.Add(new StepNavigatorDotVM { IsFirst = i == 0 });
        }

        UpdateDotStates(dots, CurrentStep, _visibleWindowStart);
    }

    internal static string BuildStepCounterText(int stepCount, int currentStep)
    {
        var safeStepCount = Math.Max(0, stepCount);
        if (safeStepCount == 0)
            return "0/0";

        var safeCurrentStep = Math.Clamp(currentStep, 1, safeStepCount);
        return $"{safeCurrentStep}/{safeStepCount}";
    }

    internal static (int Start, int Count) CalculateVisibleWindow(
        int stepCount,
        int currentStep,
        int paginationCount,
        bool shouldPaginate)
    {
        var safeStepCount = Math.Max(0, stepCount);
        if (safeStepCount == 0)
            return (0, 0);

        var safePaginationCount = Math.Max(1, paginationCount);
        if (!shouldPaginate || safeStepCount <= safePaginationCount)
            return (0, safeStepCount);

        var safeCurrentStep = Math.Clamp(currentStep, 1, safeStepCount);
        var cycleIndex = (safeCurrentStep - 1) / safePaginationCount;
        var start = cycleIndex * safePaginationCount;
        var count = Math.Min(safePaginationCount, safeStepCount - start);

        return (start, count);
    }

    internal static void UpdateDotStates(ObservableCollection<StepNavigatorDotVM> dots, int currentStep)
        => UpdateDotStates(dots, currentStep, windowStart: 0);

    internal static void UpdateDotStates(
        ObservableCollection<StepNavigatorDotVM> dots,
        int currentStep,
        int windowStart)
    {
        var currentStepIndex = currentStep - 1;

        for (var i = 0; i < dots.Count; i++)
        {
            var absoluteDotIndex = windowStart + i;
            dots[i].IsCompleted = absoluteDotIndex < currentStepIndex;
            dots[i].IsActive = absoluteDotIndex == currentStepIndex;
        }
    }
}


