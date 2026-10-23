using Soundie.Services.Ui;
using System.Windows;

namespace Soundie;

public partial class MainWindow
{
    public OHsThemeService ThemeService { get; private set; } = null!;
    public Task ThemeChangeCompletion { get; private set; } = Task.CompletedTask;
    private void OnThemeToggle(object sender, RoutedEventArgs e) => ThemeChangeCompletion = ChangeThemeAsync();
    private async Task ChangeThemeAsync()
    {
        try { await ThemeService.ChangeThemeAsync(ThemeService.IsLightTheme ? 0 : 1, lifetime.Token); }
        catch (OperationCanceledException) when (closed) { }
        catch (Exception error) { if (!closed) reportError(error.Message); }
    }
}
