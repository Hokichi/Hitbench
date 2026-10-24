using System.Windows;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
namespace Hitbench.Resources.CustomControls;

public partial class OHsMessageBoxPopup : BasePopup
{
    private MessageBoxButton _buttons;
    private BalloonButton? defaultButton;
    private readonly bool destructiveConfirmation;

    protected override bool ShouldBubbleApplicationClose => false;

    public OHsMessageBoxPopup(string message, string title, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None, bool destructiveConfirmation = false,
        string? destructiveButtonText = null, string? destructiveButtonAutomationName = null,
        string? destructiveButtonIconKey = null, string? cancelButtonText = null)
    {
        InitializeComponent();
        this.destructiveConfirmation = destructiveConfirmation && buttons == MessageBoxButton.OKCancel;

        PopupTitle = string.IsNullOrWhiteSpace(title) ? "Message" : title;
        SetFormattedMessage(message);

        ConfigureButtons(buttons);
        if (cancelButtonText is not null && buttons == MessageBoxButton.OKCancel)
        {
            SecondaryButton.ButtonText = cancelButtonText;
            AutomationProperties.SetName(SecondaryButton, cancelButtonText);
        }
        if (this.destructiveConfirmation)
        {
            PrimaryButton.ButtonText = destructiveButtonText ?? "Remove Track";
            PrimaryButton.ButtonIcon = FindResource(destructiveButtonIconKey ?? "Close");
            PrimaryButton.Style = (Style)FindResource("OHsBalloonDestructiveSolidActionStyle");
            AutomationProperties.SetName(PrimaryButton, destructiveButtonAutomationName ?? "Remove track");
            SecondaryButton.Style = (Style)FindResource("OHsBalloonNeutralActionStyle");
            defaultButton = SecondaryButton;
            Loaded += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
                () => SecondaryButton.Focus());
        }
        ConfigureIcon(icon);
    }

    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    private void ConfigureButtons(MessageBoxButton buttons)
    {
        _buttons = buttons;

        switch (buttons)
        {
            case MessageBoxButton.OK:
                ConfigureButton(PrimaryButton, "OK", MessageBoxResult.OK, true);
                break;

            case MessageBoxButton.OKCancel:
                ConfigureButton(PrimaryButton, "OK", MessageBoxResult.OK, true);
                ConfigureButton(SecondaryButton, "Cancel", MessageBoxResult.Cancel);
                break;

            case MessageBoxButton.YesNo:
                ConfigureButton(PrimaryButton, "Yes", MessageBoxResult.Yes, true);
                ConfigureButton(SecondaryButton, "No", MessageBoxResult.No);
                break;

            case MessageBoxButton.YesNoCancel:
                ConfigureButton(PrimaryButton, "Yes", MessageBoxResult.Yes, true);
                ConfigureButton(SecondaryButton, "No", MessageBoxResult.No);
                ConfigureButton(TertiaryButton, "Cancel", MessageBoxResult.Cancel);
                break;

            default:
                ConfigureButton(PrimaryButton, "OK", MessageBoxResult.OK, true);
                break;
        }
    }

    private void ConfigureButton(BalloonButton button, string content, MessageBoxResult result,
        bool isDefault = false)
    {
        var affirmative = result is MessageBoxResult.OK or MessageBoxResult.Yes;
        button.ButtonText = content;
        button.ButtonIcon = FindResource(affirmative ? "Check" : "Ban");
        button.Style = (Style)FindResource(affirmative
            ? "OHsBalloonConfirmActionStyle"
            : "OHsBalloonDestructiveActionStyle");
        AutomationProperties.SetName(button, content);
        button.Tag = result;
        button.Visibility = Visibility.Visible;
        if (isDefault)
            defaultButton = button;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None &&
            defaultButton is { IsEnabled: true } button)
        {
            e.Handled = true;
            SetResultFrom(destructiveConfirmation && ReferenceEquals(Keyboard.FocusedElement, PrimaryButton)
                ? PrimaryButton : button);
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private void ConfigureIcon(MessageBoxImage icon)
    {
        var (geometryKey, accentBrushKey) = icon switch
        {
            MessageBoxImage.Error or MessageBoxImage.Stop or MessageBoxImage.Hand => ("Ban", "Brush.Danger"),
            MessageBoxImage.Warning or MessageBoxImage.Exclamation => ("Info", "Brush.Warning"),
            MessageBoxImage.Question => ("Info", "Brush.BalloonButton.Background.Default.Hovered"),
            MessageBoxImage.Information or MessageBoxImage.Asterisk => ("Info", "Brush.Info"),
            _ => ("Info", "Brush.Text.Secondary")
        };

        var accentBrush = TryFindResource(accentBrushKey) as Brush ?? Brushes.White;
        MessageIcon.Path = TryFindResource(geometryKey) as Geometry;
        MessageIcon.Color = accentBrush;
        IconBadge.Background = CreateBadgeBackground(accentBrush);
    }

    protected override void OnCloseButtonClick()
    {
        Result = GetDismissResult();
        Close();
    }

    private void OnPrimaryButtonClick(object sender, RoutedEventArgs e)
    {
        SetResultFrom(sender);
    }

    private void OnSecondaryButtonClick(object sender, RoutedEventArgs e)
    {
        SetResultFrom(sender);
    }

    private void OnTertiaryButtonClick(object sender, RoutedEventArgs e)
    {
        SetResultFrom(sender);
    }

    private void SetResultFrom(object sender)
    {
        if (sender is BalloonButton { Tag: MessageBoxResult result })
            Result = result;

        Close();
    }

    private MessageBoxResult GetDismissResult()
    {
        return _buttons switch
        {
            MessageBoxButton.OK => MessageBoxResult.OK,
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.None
        };
    }

    private static Brush CreateBadgeBackground(Brush accentBrush)
    {
        if (accentBrush is not SolidColorBrush solidColorBrush)
            return accentBrush;

        return new SolidColorBrush(solidColorBrush.Color)
        {
            Opacity = 0.16
        };
    }

    private void SetFormattedMessage(string? message)
    {
        MessageTextBlock.Inlines.Clear();

        if (string.IsNullOrEmpty(message))
            return;

        var boldFont = TryFindResource("Bold") as FontFamily ?? MessageTextBlock.FontFamily;
        var startIndex = 0;

        while (startIndex < message.Length)
        {
            var markerStart = message.IndexOf("**", startIndex, StringComparison.Ordinal);
            if (markerStart < 0)
            {
                MessageTextBlock.Inlines.Add(new Run(message[startIndex..]));
                break;
            }

            if (markerStart > startIndex)
                MessageTextBlock.Inlines.Add(new Run(message[startIndex..markerStart]));

            var markerEnd = message.IndexOf("**", markerStart + 2, StringComparison.Ordinal);
            if (markerEnd < 0)
            {
                MessageTextBlock.Inlines.Add(new Run(message[markerStart..]));
                break;
            }

            var boldText = message.Substring(markerStart + 2, markerEnd - (markerStart + 2));
            MessageTextBlock.Inlines.Add(new Run(boldText) { FontFamily = boldFont, FontWeight = FontWeights.Bold });
            startIndex = markerEnd + 2;
        }
    }
}
