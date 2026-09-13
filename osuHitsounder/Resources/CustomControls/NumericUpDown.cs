using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace osuHitsounder.Resources.CustomControls;

[TemplatePart(Name = PartTextBox, Type = typeof(TextBox))]
[TemplatePart(Name = PartUpButton, Type = typeof(RepeatButton))]
[TemplatePart(Name = PartDownButton, Type = typeof(RepeatButton))]
public class NumericUpDown : Control
{
    private const string PartTextBox = "PART_TextBox";
    private const string PartUpButton = "PART_UpButton";
    private const string PartDownButton = "PART_DownButton";

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(decimal),
        typeof(NumericUpDown),
        new FrameworkPropertyMetadata(
            decimal.Zero,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValueChanged,
            CoerceValue));

    public static readonly DependencyProperty LowerLimitProperty = DependencyProperty.Register(
        nameof(LowerLimit),
        typeof(decimal),
        typeof(NumericUpDown),
        new PropertyMetadata(decimal.Zero, OnLimitChanged));

    public static readonly DependencyProperty UpperLimitProperty = DependencyProperty.Register(
        nameof(UpperLimit),
        typeof(decimal),
        typeof(NumericUpDown),
        new PropertyMetadata(decimal.MaxValue, OnLimitChanged));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step),
        typeof(decimal),
        typeof(NumericUpDown),
        new PropertyMetadata(decimal.One, OnStepChanged, CoerceStep));

    private TextBox? _textBox;
    private RepeatButton? _upButton;
    private RepeatButton? _downButton;
    private bool _isUpdatingText;

    static NumericUpDown()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(NumericUpDown),
            new FrameworkPropertyMetadata(typeof(NumericUpDown)));
    }

    public decimal Value
    {
        get => (decimal)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public decimal LowerLimit
    {
        get => (decimal)GetValue(LowerLimitProperty);
        set => SetValue(LowerLimitProperty, value);
    }

    public decimal UpperLimit
    {
        get => (decimal)GetValue(UpperLimitProperty);
        set => SetValue(UpperLimitProperty, value);
    }

    public decimal Step
    {
        get => (decimal)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public override void OnApplyTemplate()
    {
        UnwireTemplateParts();

        base.OnApplyTemplate();

        _textBox = GetTemplateChild(PartTextBox) as TextBox;
        _upButton = GetTemplateChild(PartUpButton) as RepeatButton;
        _downButton = GetTemplateChild(PartDownButton) as RepeatButton;

        WireTemplateParts();
        UpdateText();
    }

    internal static decimal CoerceValueWithinLimits(decimal value, decimal lowerLimit, decimal upperLimit)
    {
        if (lowerLimit > upperLimit)
            return lowerLimit;

        if (value < lowerLimit)
            return lowerLimit;

        if (value > upperLimit)
            return upperLimit;

        return value;
    }

    internal static bool TryParseValueText(string text, decimal lowerLimit, decimal upperLimit, out decimal value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = default;
            return false;
        }

        var styles = NumberStyles.AllowLeadingSign |
                     NumberStyles.AllowDecimalPoint |
                     NumberStyles.AllowThousands |
                     NumberStyles.AllowLeadingWhite |
                     NumberStyles.AllowTrailingWhite;

        if (!decimal.TryParse(text, styles, CultureInfo.CurrentCulture, out value) &&
            !decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        value = CoerceValueWithinLimits(value, lowerLimit, upperLimit);
        return true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Handled)
            return;

        if (e.Key == Key.Up)
        {
            ChangeValue(Step);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            ChangeValue(-Step);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitText();
            e.Handled = true;
        }
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);

        if (!IsKeyboardFocusWithin || e.Handled)
            return;

        ChangeValue(e.Delta > 0 ? Step : -Step);
        e.Handled = true;
    }

    private static object CoerceValue(DependencyObject dependencyObject, object baseValue)
    {
        var numericUpDown = (NumericUpDown)dependencyObject;
        return CoerceValueWithinLimits(
            (decimal)baseValue,
            numericUpDown.LowerLimit,
            numericUpDown.UpperLimit);
    }

    private static object CoerceStep(DependencyObject dependencyObject, object baseValue)
    {
        var step = (decimal)baseValue;
        return step > decimal.Zero ? step : decimal.One;
    }

    private static void OnValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ((NumericUpDown)dependencyObject).UpdateText();
    }

    private static void OnLimitChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var numericUpDown = (NumericUpDown)dependencyObject;
        numericUpDown.CoerceValue(ValueProperty);
        numericUpDown.UpdateText();
    }

    private static void OnStepChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ((NumericUpDown)dependencyObject).CoerceValue(StepProperty);
    }

    private void WireTemplateParts()
    {
        if (_textBox is not null)
        {
            _textBox.LostKeyboardFocus += OnTextBoxLostKeyboardFocus;
            _textBox.PreviewTextInput += OnTextBoxPreviewTextInput;
            DataObject.AddPastingHandler(_textBox, OnTextBoxPasting);
        }

        if (_upButton is not null)
            _upButton.Click += OnUpButtonClick;

        if (_downButton is not null)
            _downButton.Click += OnDownButtonClick;
    }

    private void UnwireTemplateParts()
    {
        if (_textBox is not null)
        {
            _textBox.LostKeyboardFocus -= OnTextBoxLostKeyboardFocus;
            _textBox.PreviewTextInput -= OnTextBoxPreviewTextInput;
            DataObject.RemovePastingHandler(_textBox, OnTextBoxPasting);
        }

        if (_upButton is not null)
            _upButton.Click -= OnUpButtonClick;

        if (_downButton is not null)
            _downButton.Click -= OnDownButtonClick;
    }

    private void OnUpButtonClick(object sender, RoutedEventArgs e)
    {
        ChangeValue(Step);
    }

    private void OnDownButtonClick(object sender, RoutedEventArgs e)
    {
        ChangeValue(-Step);
    }

    private void OnTextBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CommitText();
    }

    private void OnTextBoxPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_textBox is null)
            return;

        e.Handled = !IsValidProposedText(_textBox.Text ?? string.Empty, _textBox.SelectionStart, _textBox.SelectionLength, e.Text);
    }

    private void OnTextBoxPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (_textBox is null || !e.SourceDataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pastedText = e.SourceDataObject.GetData(DataFormats.Text) as string ?? string.Empty;
        if (!IsValidProposedText(_textBox.Text ?? string.Empty, _textBox.SelectionStart, _textBox.SelectionLength, pastedText))
            e.CancelCommand();
    }

    private void ChangeValue(decimal delta)
    {
        CommitText();
        Value = CoerceValueWithinLimits(Value + delta, LowerLimit, UpperLimit);
    }

    private void CommitText()
    {
        if (_textBox is null || _isUpdatingText)
            return;

        if (TryParseValueText(_textBox.Text ?? string.Empty, LowerLimit, UpperLimit, out var value))
            Value = value;

        UpdateText();
    }

    private void UpdateText()
    {
        if (_textBox is null)
            return;

        var nextText = Value.ToString("G29", CultureInfo.CurrentCulture);
        if (_textBox.Text == nextText)
            return;

        _isUpdatingText = true;
        _textBox.Text = nextText;
        _textBox.CaretIndex = _textBox.Text.Length;
        _isUpdatingText = false;
    }

    private static bool IsValidProposedText(
        string existingText,
        int selectionStart,
        int selectionLength,
        string newText)
    {
        var proposed = selectionLength > 0
            ? existingText.Remove(selectionStart, selectionLength).Insert(selectionStart, newText)
            : existingText.Insert(selectionStart, newText);

        if (string.IsNullOrWhiteSpace(proposed))
            return true;

        var currentDecimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        var currentGroupSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator;
        var hasDecimalSeparator = false;

        for (var index = 0; index < proposed.Length; index++)
        {
            var character = proposed[index];
            if (char.IsDigit(character) || char.IsWhiteSpace(character))
                continue;

            if (character == '-' && index == 0)
                continue;

            if (IsSeparatorAt(proposed, index, currentGroupSeparator) || character == ',')
                continue;

            if (IsSeparatorAt(proposed, index, currentDecimalSeparator) || character == '.')
            {
                if (hasDecimalSeparator)
                    return false;

                hasDecimalSeparator = true;
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsSeparatorAt(string text, int index, string separator)
    {
        return separator.Length > 0 &&
               index + separator.Length <= text.Length &&
               string.Compare(text, index, separator, 0, separator.Length, StringComparison.Ordinal) == 0;
    }
}


