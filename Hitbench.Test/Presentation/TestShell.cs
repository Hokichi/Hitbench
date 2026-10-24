using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Services.FileSystem;
using Hitbench.ViewModels.Shell.HomePage;
using Hitbench.Views.Pages;
using System.Windows;

namespace Hitbench.Test.Presentation;

public static class TestShell
{
    public static MainWindow Create(HomePage page, IMessenger messenger, Func<WorkstationPage> createWorkstation,
        HomePageVM viewModel, IAppSettingsService settings, Action<WindowState>? requestWindowState = null,
        Action? startDrag = null, Action<Rect>? moveRestoredWindow = null, Func<bool>? isLeftButtonPressed = null,
        Func<HomePage>? createHome = null) =>
        new(page, messenger, createWorkstation, viewModel, settings,
            error => throw new AssertionException($"Unexpected shell error: {error}"),
            requestWindowState ?? (_ => throw new AssertionException("Unexpected native window-state request.")),
            _ => new Size(1920, 1080), startDrag: startDrag ?? (() => throw new AssertionException("Unexpected native drag.")),
            moveRestoredWindow: moveRestoredWindow ?? (_ => throw new AssertionException("Unexpected native move.")),
            isLeftButtonPressed: isLeftButtonPressed ?? (() => true), createHome: createHome);
}
