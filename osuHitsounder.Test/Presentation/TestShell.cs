using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.ViewModels.Shell.HomePage;
using osuHitsounder.Views.Pages;
using System.Windows;

namespace osuHitsounder.Test.Presentation;

public static class TestShell
{
    public static MainWindow Create(HomePage page, IMessenger messenger, Func<WorkstationPage> createWorkstation,
        HomePageVM viewModel, IAppSettingsService settings, Action<WindowState>? requestWindowState = null) =>
        new(page, messenger, createWorkstation, viewModel, settings,
            error => throw new AssertionException($"Unexpected shell error: {error}"),
            requestWindowState ?? (_ => throw new AssertionException("Unexpected native window-state request.")));
}
