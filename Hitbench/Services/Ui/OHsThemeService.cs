using CommunityToolkit.Mvvm.ComponentModel;
using Hitbench.Services.FileSystem;
using System.Windows;

namespace Hitbench.Services.Ui;

public sealed partial class OHsThemeService(ResourceDictionary resources, IAppSettingsService settings, IUiScheduler scheduler) : ObservableObject
{
    private readonly SemaphoreSlim changes = new(1, 1);
    private int theme;
    public int Theme => theme;
    public bool IsLightTheme => theme == 1;
    [ObservableProperty] private bool isChangingTheme;

    public void ApplyInitialTheme(int value)
    {
        if (value is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(value));
        var dictionary = new ResourceDictionary { Source = new Uri($"/Hitbench;component/Resources/Themes/{(value == 1 ? "Light" : "Dark")}.xaml", UriKind.Relative) };
        var index = resources.MergedDictionaries.ToList().FindIndex(d => d.Source?.OriginalString.EndsWith("/Dark.xaml", StringComparison.OrdinalIgnoreCase) == true || d.Source?.OriginalString.EndsWith("/Light.xaml", StringComparison.OrdinalIgnoreCase) == true);
        if (index < 0) resources.MergedDictionaries.Insert(0, dictionary);
        else resources.MergedDictionaries[index] = dictionary;
        theme = value; OnPropertyChanged(nameof(Theme)); OnPropertyChanged(nameof(IsLightTheme));
    }

    public async Task ChangeThemeAsync(int value, CancellationToken token)
    {
        if (value is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(value));
        await changes.WaitAsync(token);
        var previous = theme;
        try
        {
            await scheduler.InvokeAsync(() => { IsChangingTheme = true; ApplyInitialTheme(value); }, token);
            await settings.SaveThemeAsync(value, token);
        }
        catch { await scheduler.InvokeAsync(() => ApplyInitialTheme(previous), CancellationToken.None); throw; }
        finally { await scheduler.InvokeAsync(() => IsChangingTheme = false, CancellationToken.None); changes.Release(); }
    }
}
