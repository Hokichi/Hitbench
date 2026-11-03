using System.Windows;
using System.Reflection;
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

    internal static ServiceProvider CreateServices(string? asioDriverName, AudioOutputFactory? audioOutputs, IUiScheduler? uiScheduler, int applicationVolume = 100, AudioMemoryBudget? memoryBudget = null)
    {
        audioOutputs ??= new AudioOutputFactory();
        var registrations = new ServiceCollection();
        registrations.AddSingleton(memoryBudget ?? new AudioMemoryBudget());
        registrations.AddSingleton<Func<IWorkstationAudioService>>(provider => () => new WorkstationAudioService(
            path => AudioDecoder.Open(path, provider.GetRequiredService<AudioMemoryBudget>()), audioOutputs.CreateCandidates(asioDriverName), provider.GetRequiredService<OHsAudioOperationProgress>(), provider.GetRequiredService<AudioMemoryBudget>()));
        registrations.AddSingleton<IMessenger, WeakReferenceMessenger>();
        registrations.AddSingleton(provider => new OHsAudioOperationProgress(provider.GetRequiredService<IMessenger>(), () => provider.GetRequiredService<IUiScheduler>()));
        registrations.AddSingleton<IProjectFileService>(_ => new ProjectFileService(AppContext.BaseDirectory));
        registrations.AddSingleton<IWorkstationSourceService, WorkstationSourceService>();
        registrations.AddSingleton(provider => new PlaybackVolumeSettings(provider.GetRequiredService<IAppSettingsService>(), applicationVolume));
        registrations.AddSingleton<ISampleFileService>(_ => new SampleFileService(AppContext.BaseDirectory));
        registrations.AddSingleton<SampleImportBatch>();
        registrations.AddSingleton<IOHsSampleDropImportDialogService>(provider => new OHsSampleDropImportDialogService(
            () => Current.MainWindow, Current.Dispatcher, paths => new OHsSampleDropImportPopupVM(paths,
                new SampleBrowserService(memoryBudget: provider.GetRequiredService<AudioMemoryBudget>()),
                new SamplePreviewService(path => AudioDecoder.Open(path, provider.GetRequiredService<AudioMemoryBudget>()), audioOutputs.CreateCandidates(null)),
                provider.GetRequiredService<IUiScheduler>(), provider.GetRequiredService<PlaybackVolumeSettings>())));

        registrations.AddSingleton<Func<Hitbench.Resources.DataModels.SampleManagementContext, ISampleFileDialogService, ISampleRemovalDialogService, ManageSamplesPopupVM>>(provider => (context, picker, removal) => new ManageSamplesPopupVM(context, provider.GetRequiredService<ISampleFileService>(), provider.GetRequiredService<SampleImportBatch>(), picker, removal, provider.GetRequiredService<IMessenger>(), provider.GetRequiredService<IUiScheduler>(), new SamplePreviewService(path => AudioDecoder.Open(path, provider.GetRequiredService<AudioMemoryBudget>()), audioOutputs.CreateCandidates(null), provider.GetRequiredService<OHsAudioOperationProgress>()), provider.GetRequiredService<PlaybackVolumeSettings>(), waveformReader: new SampleBrowserService(memoryBudget: provider.GetRequiredService<AudioMemoryBudget>()), ownsWaveformReader: true));
        registrations.AddSingleton<ISampleManagementDialogService>(provider => new SampleManagementDialogService(() => Current.MainWindow, Current.Dispatcher, provider.GetRequiredService<Func<SampleLoaderPopupVM>>(), provider.GetRequiredService<Func<Hitbench.Resources.DataModels.SampleManagementContext, ISampleFileDialogService, ISampleRemovalDialogService, ManageSamplesPopupVM>>()));
        registrations.AddSingleton<ISampleBrowserService>(provider => new SampleBrowserService(operationProgress: provider.GetRequiredService<OHsAudioOperationProgress>(), applicationDirectory: AppContext.BaseDirectory, memoryBudget: provider.GetRequiredService<AudioMemoryBudget>()));
        registrations.AddSingleton<Func<SampleLoaderPopupVM>>(provider => () => new SampleLoaderPopupVM(
            provider.GetRequiredService<ISampleBrowserService>(),
            new SamplePreviewService(path => AudioDecoder.Open(path, provider.GetRequiredService<AudioMemoryBudget>()), audioOutputs.CreateCandidates(null), provider.GetRequiredService<OHsAudioOperationProgress>()),
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
                provider.GetRequiredService<PlaybackVolumeSettings>(), nodePasteDialog: provider.GetRequiredService<INodePasteDialogService>(), sampleManagementDialog: provider.GetRequiredService<ISampleManagementDialogService>(), sampleImports: provider.GetRequiredService<SampleImportBatch>(), instrumentDialogs: provider.GetRequiredService<IInstrumentalTrackDialogService>(), directoryLauncher: provider.GetRequiredService<IOHsExportDirectoryLauncher>(), exportDialog: provider.GetRequiredService<IOHsExportDialogService>(), trackAppearanceDialog: provider.GetRequiredService<IOHsTrackAppearanceDialogService>(), sampleDropDialog: provider.GetRequiredService<IOHsSampleDropImportDialogService>(), audioOperationProgress: provider.GetRequiredService<OHsAudioOperationProgress>())));
        registrations.AddSingleton<IHomeFolderService>(provider => new HomeFolderService(AppContext.BaseDirectory, sampleFiles: provider.GetRequiredService<ISampleFileService>()));
        registrations.AddSingleton<IAppSettingsService>(_ => new AppSettingsService(AppContext.BaseDirectory));
        registrations.AddSingleton<IUiScheduler>(_ => uiScheduler ?? new UiScheduler(Current.Dispatcher));
        registrations.AddSingleton<IInstallationDialogService>(_ =>
            new InstallationDialogService(() => Current.MainWindow, Current.Dispatcher));
        registrations.AddTransient<HomePageVM>();
        registrations.AddTransient<HomePage>();
        registrations.AddSingleton<Func<HomePage>>(provider => () => provider.GetRequiredService<HomePage>());
        registrations.AddSingleton(provider => new OHsThemeService(Current.Resources, provider.GetRequiredService<IAppSettingsService>(), provider.GetRequiredService<IUiScheduler>()));
        registrations.AddSingleton<MainWindow>();
        registrations.AddSingleton(_ => new System.Net.Http.HttpClient { Timeout = Timeout.InfiniteTimeSpan });
        registrations.AddSingleton<IOHsUpdateService>(provider =>
        {
            var metadata = typeof(App).Assembly.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>().ToDictionary(item => item.Key, item => item.Value);
            return new OHsUpdateService(provider.GetRequiredService<System.Net.Http.HttpClient>(), AppContext.BaseDirectory, System.IO.Path.GetTempPath(),
                metadata.GetValueOrDefault("OHsReleaseVersion"), metadata.GetValueOrDefault("OHsSourceCommit"));
        });
        registrations.AddSingleton(_ => new OHsUpdateHandoff(AppContext.BaseDirectory));
        registrations.AddSingleton<IOHsUpdateDialogService>(provider => new OHsUpdateDialogService(() => Current.MainWindow, Current.Dispatcher,
            release => new OHsUpdatePopupVM(release, provider.GetRequiredService<IOHsUpdateService>())));
        registrations.AddSingleton<IOHsUpdateRestartService>(provider => new OHsUpdateRestartService(provider.GetRequiredService<OHsUpdateHandoff>(),
            token => Current.Dispatcher.InvokeAsync(() => ((MainWindow)Current.MainWindow).PrepareForUpdateCloseAsync(token)).Task.Unwrap(),
            (commit, token) => Current.Dispatcher.InvokeAsync(() => ((MainWindow)Current.MainWindow).RequestUpdateCloseAsync(commit, token)).Task.Unwrap(),
            () => Current.Dispatcher.Invoke(() => ((MainWindow)Current.MainWindow).CancelUpdateClose())));
        registrations.AddSingleton<OHsUpdateVM>();
        return registrations.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private async Task StartPrimaryAsync(CancellationToken token)
    {
        Resources["OHsTextLanguage"] = System.Windows.Markup.XmlLanguage.GetLanguage(
            System.Globalization.CultureInfo.CurrentUICulture.IetfLanguageTag);
        MainWindow? window = null;
        try
        {
            using var startupSettings = new AppSettingsService(AppContext.BaseDirectory);
            var settings = await startupSettings.LoadAsync(CancellationToken.None);
            services = CreateServices(settings.AsioDriverName, null, null, settings.Volume);
            services.GetRequiredService<OHsThemeService>().ApplyInitialTheme(settings.Theme);
            window = services.GetRequiredService<MainWindow>();
            activeShell = window;
            window.ApplySettings(settings);
            MainWindow = window;
            window.InitializeUpdates(services.GetRequiredService<OHsUpdateVM>());
            window.Closed += (_, _) => Shutdown();
            window.Show();
            window.StartUpdateCheck();
            activationGate!.MarkReady();
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
        // Stop acknowledging activation before the gate closes or cleanup can block.
        try { instanceCoordinator?.StopActivation(); }
        finally
        {
            activationGate?.Deactivate();
            // Canceled background I/O can still unwind after WPF exits. Keep the
            // lifetime mutex until process termination; Windows then releases it.
            try { services?.Dispose(); }
            finally { base.OnExit(e); }
        }
    }
}
