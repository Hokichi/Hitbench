using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.Services.Dialogs;
using osuHitsounder.Services.Ui;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.ViewModels.Shell.HomePage;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Pages;

namespace osuHitsounder;

public partial class App : Application
{
    private ServiceProvider? services;

    public static ServiceProvider CreateServices(string? asioDriverName = null, AudioOutputFactory? audioOutputs = null)
        => CreateServices(asioDriverName, audioOutputs, null);

    internal static ServiceProvider CreateServices(string? asioDriverName, AudioOutputFactory? audioOutputs, IUiScheduler? uiScheduler)
    {
        audioOutputs ??= new AudioOutputFactory();
        var registrations = new ServiceCollection();
        registrations.AddSingleton<Func<IWorkstationAudioService>>(_ => () => new WorkstationAudioService(
            AudioDecoder.Open, audioOutputs.CreateCandidates(asioDriverName)));
        registrations.AddSingleton<IMessenger, WeakReferenceMessenger>();
        registrations.AddSingleton<IProjectFileService>(_ => new ProjectFileService(AppContext.BaseDirectory));
        registrations.AddSingleton<IWorkstationSourceService, WorkstationSourceService>();
        registrations.AddSingleton<PlaybackVolumeSettings>();
        registrations.AddSingleton<ISampleFileService, SampleFileService>();
        registrations.AddSingleton<SampleImportBatch>();
        registrations.AddSingleton<Func<osuHitsounder.Resources.DataModels.SampleManagementContext, ISampleFileDialogService, ISampleRemovalDialogService, ManageSamplesPopupVM>>(provider => (context, picker, removal) => new ManageSamplesPopupVM(context, provider.GetRequiredService<ISampleFileService>(), provider.GetRequiredService<SampleImportBatch>(), picker, removal, provider.GetRequiredService<IMessenger>(), provider.GetRequiredService<IUiScheduler>(), new SamplePreviewService(AudioDecoder.Open, audioOutputs.CreateCandidates(null)), provider.GetRequiredService<PlaybackVolumeSettings>()));
        registrations.AddSingleton<ISampleManagementDialogService>(provider => new SampleManagementDialogService(() => Current.MainWindow, Current.Dispatcher, provider.GetRequiredService<Func<SampleLoaderPopupVM>>(), provider.GetRequiredService<Func<osuHitsounder.Resources.DataModels.SampleManagementContext, ISampleFileDialogService, ISampleRemovalDialogService, ManageSamplesPopupVM>>()));
        registrations.AddSingleton<ISampleBrowserService, SampleBrowserService>();
        registrations.AddSingleton<Func<SampleLoaderPopupVM>>(provider => () => new SampleLoaderPopupVM(
            provider.GetRequiredService<ISampleBrowserService>(),
            new SamplePreviewService(AudioDecoder.Open, audioOutputs.CreateCandidates(null)),
            provider.GetRequiredService<IUiScheduler>(), provider.GetRequiredService<PlaybackVolumeSettings>()));
        registrations.AddSingleton<ISampleFileDialogService>(provider =>
            new SampleFileDialogService(() => Current.MainWindow, Current.Dispatcher,
                provider.GetRequiredService<Func<SampleLoaderPopupVM>>()));
        registrations.AddSingleton<IMappingConflictDialogService>(_ => new MappingConflictDialogService(() => Current.MainWindow, Current.Dispatcher));
        registrations.AddSingleton<IInstrumentalTrackDialogService>(provider => new InstrumentalTrackDialogService(
            () => Current.MainWindow, Current.Dispatcher,
            CreateInstrumentEditorFactory(provider),
            CreateKeyMappingsFactory(provider)));
        registrations.AddSingleton<INodePasteDialogService>(_ => new NodePasteDialogService(
            () => Current.MainWindow, Current.Dispatcher));
        registrations.AddSingleton<ITrackRemovalDialogService>(_ =>
            new TrackRemovalDialogService(() => Current.MainWindow, Current.Dispatcher));
        registrations.AddSingleton<Func<WorkstationPage>>(provider => () => new WorkstationPage(
            new WorkstationPageVM(provider.GetRequiredService<IWorkstationSourceService>(), new osuHitsounder.Services.MarkerPlaybackCoordinator(provider.GetRequiredService<Func<IWorkstationAudioService>>()(), MarkerPlaybackCoordinator.DelayAsync), provider.GetRequiredService<IUiScheduler>(),
                provider.GetRequiredService<ISampleFileService>(), provider.GetRequiredService<ISampleFileDialogService>(),
                provider.GetRequiredService<IProjectFileService>(), provider.GetRequiredService<IMessenger>(),
                provider.GetRequiredService<ITrackRemovalDialogService>(),
                provider.GetRequiredService<PlaybackVolumeSettings>(), nodePasteDialog: provider.GetRequiredService<INodePasteDialogService>(), sampleManagementDialog: provider.GetRequiredService<ISampleManagementDialogService>(), sampleImports: provider.GetRequiredService<SampleImportBatch>(), instrumentDialogs: provider.GetRequiredService<IInstrumentalTrackDialogService>())));
        registrations.AddSingleton<IHomeFolderService>(_ => new HomeFolderService(AppContext.BaseDirectory));
        registrations.AddSingleton<IAppSettingsService>(_ => new AppSettingsService(AppContext.BaseDirectory));
        registrations.AddSingleton<IUiScheduler>(_ => uiScheduler ?? new UiScheduler(Current.Dispatcher));
        registrations.AddSingleton<IInstallationDialogService>(_ =>
            new InstallationDialogService(() => Current.MainWindow, Current.Dispatcher));
        registrations.AddSingleton<Func<string, IReadOnlySet<string>, ProjectNamePopupVM>>(_ =>
            (suggestedName, existingNames) => new ProjectNamePopupVM(suggestedName, existingNames));
        registrations.AddSingleton<IProjectCreationDialogService>(provider =>
            new ProjectCreationDialogService(() => Current.MainWindow, Current.Dispatcher,
                provider.GetRequiredService<Func<string, IReadOnlySet<string>, ProjectNamePopupVM>>()));
        registrations.AddTransient<HomePageVM>();
        registrations.AddTransient<HomePage>();
        registrations.AddSingleton<Func<HomePage>>(provider => () => provider.GetRequiredService<HomePage>());
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
            using var startupSettings = new AppSettingsService(AppContext.BaseDirectory);
            var settings = await startupSettings.LoadAsync(CancellationToken.None);
            services = CreateServices(settings.AsioDriverName);
            window = services.GetRequiredService<MainWindow>();
            window.ApplySettings(settings);
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

