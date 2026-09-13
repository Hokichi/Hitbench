using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
namespace osuHitsounder.Resources.CustomControls;

public partial class OHsMessageBoxPopup : BasePopup
{
    private MessageBoxButton _buttons;

    protected override bool ShouldBubbleApplicationClose => false;

    public OHsMessageBoxPopup(string message, string title, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None)
    {
        InitializeComponent();

        PopupTitle = string.IsNullOrWhiteSpace(title) ? "Message" : title;
        SetFormattedMessage(message);

        ConfigureButtons(buttons);
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
                PrimaryButton.IsCancel = true;
                break;

            case MessageBoxButton.OKCancel:
                ConfigureButton(PrimaryButton, "OK", MessageBoxResult.OK, true);
                ConfigureButton(SecondaryButton, "Cancel", MessageBoxResult.Cancel, isCancel: true);
                break;

            case MessageBoxButton.YesNo:
                ConfigureButton(PrimaryButton, "Yes", MessageBoxResult.Yes, true);
                ConfigureButton(SecondaryButton, "No", MessageBoxResult.No, isCancel: true);
                break;

            case MessageBoxButton.YesNoCancel:
                ConfigureButton(PrimaryButton, "Yes", MessageBoxResult.Yes, true);
                ConfigureButton(SecondaryButton, "No", MessageBoxResult.No);
                ConfigureButton(TertiaryButton, "Cancel", MessageBoxResult.Cancel, isCancel: true);
                break;

            default:
                ConfigureButton(PrimaryButton, "OK", MessageBoxResult.OK, true);
                PrimaryButton.IsCancel = true;
                break;
        }
    }

    private void ConfigureButton(Button button, string content, MessageBoxResult result,
        bool isDefault = false, bool isCancel = false)
    {
        button.Content = content;
        button.Tag = result;
        button.Visibility = Visibility.Visible;
        button.IsDefault = isDefault;
        button.IsCancel = isCancel;
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
        if (sender is Button { Tag: MessageBoxResult result })
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
            MessageTextBlock.Inlines.Add(new Run(boldText) { FontFamily = boldFont });
            startIndex = markerEnd + 2;
        }
    }
}

