using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Hitbench.Services.FileSystem;
using Hitbench.Services.ProjectFiles;
using Hitbench.Services.Dialogs;
using Hitbench.Services.Ui;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
using Hitbench.ViewModels.Shell.HomePage;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Pages;

namespace Hitbench;

public partial class App : Application
{
    private ServiceProvider? services;

    public static ServiceProvider CreateServices(string? asioDriverName = null, AudioOutputFactory? audioOutputs = null)
        => CreateServices(asioDriverName, audioOutputs, null);

    internal static ServiceProvider CreateServices(string? asioDriverName, AudioOutputFactory? audioOutputs, IUiScheduler? uiScheduler, int applicationVolume = 100)
    {
        audioOutputs ??= new AudioOutputFactory();
        var registrations = new ServiceCollection();
        registrations.AddSingleton<Func<IWorkstationAudioService>>(provider => () => new WorkstationAudioService(
            AudioDecoder.Open, audioOutputs.CreateCandidates(asioDriverName), provider.GetRequiredService<OHsAudioOperationProgress>()));
        registrations.AddSingleton<IMessenger, WeakReferenceMessenger>();
        registrations.AddSingleton(provider => new OHsAudioOperationProgress(provider.GetRequiredService<IMessenger>(), () => provider.GetRequiredService<IUiScheduler>()));
        registrations.AddSingleton<IProjectFileService>(_ => new ProjectFileService(AppContext.BaseDirectory));
        registrations.AddSingleton<IWorkstationSourceService, WorkstationSourceService>();
        registrations.AddSingleton(provider => new PlaybackVolumeSettings(provider.GetRequiredService<IAppSettingsService>(), applicationVolume));
        registrations.AddSingleton<ISampleFileService>(_ => new SampleFileService(AppContext.BaseDirectory));
        registrations.AddSingleton<SampleImportBatch>();
        registrations.AddSingleton<Func<Hitbench.Resources.DataModels.SampleManagementContext, ISampleFileDialogService, ISampleRemovalDialogService, ManageSamplesPopupVM>>(provider => (context, picker, removal) => new ManageSamplesPopupVM(context, provider.GetRequiredService<ISampleFileService>(), provider.GetRequiredService<SampleImportBatch>(), picker, removal, provider.GetRequiredService<IMessenger>(), provider.GetRequiredService<IUiScheduler>(), new SamplePreviewService(AudioDecoder.Open, audioOutputs.CreateCandidates(null), provider.GetRequiredService<OHsAudioOperationProgress>()), provider.GetRequiredService<PlaybackVolumeSettings>(), waveformReader: new SampleBrowserService()));
        registrations.AddSingleton<ISampleManagementDialogService>(provider => new SampleManagementDialogService(() => Current.MainWindow, Current.Dispatcher, provider.GetRequiredService<Func<SampleLoaderPopupVM>>(), provider.GetRequiredService<Func<Hitbench.Resources.DataModels.SampleManagementContext, ISampleFileDialogService, ISampleRemovalDialogService, ManageSamplesPopupVM>>()));
        registrations.AddSingleton<ISampleBrowserService>(provider => new SampleBrowserService(operationProgress: provider.GetRequiredService<OHsAudioOperationProgress>(), applicationDirectory: AppContext.BaseDirectory));
        registrations.AddSingleton<Func<SampleLoaderPopupVM>>(provider => () => new SampleLoaderPopupVM(
            provider.GetRequiredService<ISampleBrowserService>(),
            new SamplePreviewService(AudioDecoder.Open, audioOutputs.CreateCandidates(null), provider.GetRequiredService<OHsAudioOperationProgress>()),
            provider.GetRequiredService<IUiScheduler>(), provider.GetRequiredService<PlaybackVolumeSettings>()));
        registrations.AddSingleton<ISampleFileDialogService>(provider =>
            new SampleFileDialogService(() => Current.MainWindow, Current.Dispatcher,
                provider.GetRequiredService<Func<SampleLoaderPopupVM>>()));
        registrations.AddSingleton<IMappingConflictDialogService>(_ => new MappingConflictDialogService(() => Current.MainWindow, Current.Dispatcher));
        registrations.AddSingleton<IInstrumentalTrackDialogService>(provider => new InstrumentalTrackDialogService(
            () => Current.MainWindow, Current.Dispatcher,
            CreateInstrumentEditorFactory(provider),
            CreateKeyMappingsFactory(provider)));
        registrations.AddSingleton<OHsExportPublisher>();
        registrations.AddSingleton<IOHsExportService, OHsExportService>();
        registrations.AddSingleton<IOHsExportDirectoryLauncher, OHsExportDirectoryLauncher>();
        registrations.AddSingleton<Func<Hitbench.Resources.DataModels.OHsExportRequest, OHsExportPopupVM>>(provider =>
            request => new OHsExportPopupVM(request, provider.GetRequiredService<IOHsExportService>(), provider.GetRequiredService<IOHsExportDirectoryLauncher>(), provider.GetRequiredService<IMessenger>()));
        registrations.AddSingleton<IOHsExportDialogService>(provider => new OHsExportDialogService(() => Current.MainWindow, Current.Dispatcher,
            provider.GetRequiredService<Func<Hitbench.Resources.DataModels.OHsExportRequest, OHsExportPopupVM>>()));
        registrations.AddSingleton<INodePasteDialogService>(_ => new NodePasteDialogService(
            () => Current.MainWindow, Current.Dispatcher));
        registrations.AddSingleton<IOHsTrackAppearanceDialogService>(_ => new OHsTrackAppearanceDialogService(() => Current.MainWindow, Current.Dispatcher, (request, color) => new OHsTrackAppearancePopupVM(request, color)));
        registrations.AddSingleton<ITrackRemovalDialogService>(_ =>
            new TrackRemovalDialogService(() => Current.MainWindow, Current.Dispatcher));
        registrations.AddSingleton<IProjectRemovalDialogService>(_ =>
            new ProjectRemovalDialogService(() => Current.MainWindow, Current.Dispatcher));
        registrations.AddSingleton<Func<WorkstationPage>>(provider => () => new WorkstationPage(
            new WorkstationPageVM(provider.GetRequiredService<IWorkstationSourceService>(), new Hitbench.Services.MarkerPlaybackCoordinator(provider.GetRequiredService<Func<IWorkstationAudioService>>()(), MarkerPlaybackCoordinator.DelayAsync), provider.GetRequiredService<IUiScheduler>(),
                provider.GetRequiredService<ISampleFileService>(), provider.GetRequiredService<ISampleFileDialogService>(),
                provider.GetRequiredService<IProjectFileService>(), provider.GetRequiredService<IMessenger>(),
                provider.GetRequiredService<ITrackRemovalDialogService>(),
                provider.GetRequiredService<PlaybackVolumeSettings>(), nodePasteDialog: provider.GetRequiredService<INodePasteDialogService>(), sampleManagementDialog: provider.GetRequiredService<ISampleManagementDialogService>(), sampleImports: provider.GetRequiredService<SampleImportBatch>(), instrumentDialogs: provider.GetRequiredService<IInstrumentalTrackDialogService>(), directoryLauncher: provider.GetRequiredService<IOHsExportDirectoryLauncher>(), exportDialog: provider.GetRequiredService<IOHsExportDialogService>(), trackAppearanceDialog: provider.GetRequiredService<IOHsTrackAppearanceDialogService>())));
        registrations.AddSingleton<IHomeFolderService>(provider => new HomeFolderService(AppContext.BaseDirectory, sampleFiles: provider.GetRequiredService<ISampleFileService>()));
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
        registrations.AddSingleton(provider => new OHsThemeService(Current.Resources, provider.GetRequiredService<IAppSettingsService>(), provider.GetRequiredService<IUiScheduler>()));
        registrations.AddSingleton<MainWindow>();
        return registrations.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        Resources["OHsTextLanguage"] = System.Windows.Markup.XmlLanguage.GetLanguage(
            System.Globalization.CultureInfo.CurrentUICulture.IetfLanguageTag);
        base.OnStartup(e);
        MainWindow? window = null;
        try
        {
            using var startupSettings = new AppSettingsService(AppContext.BaseDirectory);
            var settings = await startupSettings.LoadAsync(CancellationToken.None);
            services = CreateServices(settings.AsioDriverName, null, null, settings.Volume);
            services.GetRequiredService<OHsThemeService>().ApplyInitialTheme(settings.Theme);
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
                MessageBox.Show(window, error, "Hitbench", MessageBoxButton.OK, MessageBoxImage.Error);
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
            var message = $"Unable to start Hitbench: {error.Message}";
            if (window is null)
                MessageBox.Show(message, "Hitbench", MessageBoxButton.OK, MessageBoxImage.Error);
            else if (window.IsVisible)
                MessageBox.Show(window, message, "Hitbench", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        services?.Dispose();
        base.OnExit(e);
    }
}
