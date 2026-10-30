using System.Windows.Controls;
using Hitbench.Views.Pages;
using Hitbench.Views.Shell;
namespace Hitbench.Test.Presentation;
internal static class HomeTestReadiness
{
    public static async Task LoadAllAsync(MainWindow window)
    {
        await window.LoadAsync();
        if (((ContentControl)window.FindName("PageHost")).Content is HomePage page)
            await page.LoadSongsPresentationAsync(default);
    }
}
