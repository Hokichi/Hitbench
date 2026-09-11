using System.Windows;
using osuHitsounder.Views.Pages;

namespace osuHitsounder;

public partial class MainWindow : Window
{
    private readonly HomePage page;
    private readonly CancellationTokenSource lifetime = new();
    private Task? loading;
    private bool closed;

    public MainWindow(HomePage page)
    {
        this.page = page;
        InitializeComponent();
        PageHost.Content = page;
    }

    public string? FatalError => page.FatalError;

    public Task LoadAsync() => loading ??= LoadPageAsync();

    private async Task LoadPageAsync()
    {
        try { await page.LoadAsync(lifetime.Token); }
        finally
        {
            if (closed)
                lifetime.Dispose();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        closed = true;
        lifetime.Cancel();
        if (loading is null || loading.IsCompleted)
            lifetime.Dispose();
        base.OnClosed(e);
    }
}
