using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.Services.Dialogs;
using osuHitsounder.Services.Ui;
using osuHitsounder.ViewModels.Shell.HomePage;
using osuHitsounder.Views.Pages;

namespace osuHitsounder;

public partial class App : Application
{
    private ServiceProvider? services;

    public static ServiceProvider CreateServices()
    {
        var registrations = new ServiceCollection();
        registrations.AddSingleton<IMessenger, WeakReferenceMessenger>();
        registrations.AddSingleton<IProjectFileService>(_ => new ProjectFileService(AppContext.BaseDirectory));
        registrations.AddTransient<WorkstationPage>();
        registrations.AddSingleton<Func<WorkstationPage>>(provider => () => provider.GetRequiredService<WorkstationPage>());
        registrations.AddSingleton<IHomeFolderService>(_ => new HomeFolderService(AppContext.BaseDirectory));
        registrations.AddSingleton<IAppSettingsService>(_ => new AppSettingsService(AppContext.BaseDirectory));
        registrations.AddSingleton<IUiScheduler>(_ => new UiScheduler(Current.Dispatcher));
        registrations.AddSingleton<IInstallationDialogService>(_ =>
            new InstallationDialogService(() => Current.MainWindow, Current.Dispatcher));
        registrations.AddSingleton<HomePageVM>();
        registrations.AddSingleton<HomePage>();
        registrations.AddSingleton<MainWindow>();
        return registrations.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MainWindow? window = null;
        try
        {
            services = CreateServices();
            window = services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Closed += (_, _) => Shutdown();
            window.Show();
            await window.LoadAsync();
            if (!window.IsVisible)
            {
                Shutdown();
                return;
            }
            if (window.FatalError is { } error)
            {
                MessageBox.Show(window, error, "osuHitsounder", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }
            if (window.RequiresShutdown)
                Shutdown();
        }
        catch (OperationCanceledException) when (window is not null && !window.IsVisible)
        {
            Shutdown();
        }
        catch (Exception error)
        {
            var message = $"Unable to start osuHitsounder: {error.Message}";
            if (window is null)
                MessageBox.Show(message, "osuHitsounder", MessageBoxButton.OK, MessageBoxImage.Error);
            else if (window.IsVisible)
                MessageBox.Show(window, message, "osuHitsounder", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        services?.Dispose();
        base.OnExit(e);
    }
}
