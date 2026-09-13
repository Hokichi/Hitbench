using System.Windows;
namespace osuHitsounder.Resources.CustomControls;

public static class OHsMessageBox
{
    public static MessageBoxResult Show(Window? owner, string message, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        var dialog = new OHsMessageBoxPopup(message, title, buttons, icon);
        var resolvedOwner = ResolveOwnerForDialog(dialog, owner, GetActiveWindow);
        if (resolvedOwner is not null)
            dialog.Owner = resolvedOwner;

        dialog.ShowDialog();
        return dialog.Result;
    }

    internal static Window? ResolveOwnerForDialog(
        Window dialog,
        Window? requestedOwner,
        Func<Window?> fallbackOwnerResolver)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(fallbackOwnerResolver);

        var resolvedOwner = requestedOwner ?? fallbackOwnerResolver();
        return ReferenceEquals(resolvedOwner, dialog) ? null : resolvedOwner;
    }

    private static Window? GetActiveWindow()
    {
        return Application.Current?.Windows
                   .OfType<Window>()
                   .FirstOrDefault(window => window.IsActive)
               ?? Application.Current?.MainWindow;
    }
}

