using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.Services.FileSystem;
using Hitbench.Services.Ui;
using System.Collections.ObjectModel;
using System.IO;

namespace Hitbench.ViewModels.Popups;

public partial class OHsHitNormalPopupVM : ObservableObject
{
    private readonly List<OHsHitNormalSample> samples;
    private readonly Dictionary<string, OHsHitNormalSample> defaults = [];
    private readonly string? projectPath;
    private readonly string? mapsetDirectory;
    private readonly ISampleBrowserService? waveformReader;
    private readonly IUiScheduler? scheduler;

    public OHsHitNormalPopupVM(OHsHitNormalConfiguration current, IReadOnlyList<OHsHitNormalChoice> choices,
        string? projectPath = null, string? mapsetDirectory = null, ISampleBrowserService? waveformReader = null,
        IUiScheduler? scheduler = null)
    {
        if (waveformReader is not null && (projectPath is null || scheduler is null))
            throw new ArgumentException("Waveform loading requires a project path and UI scheduler.");
        this.projectPath = projectPath; this.mapsetDirectory = mapsetDirectory;
        this.waveformReader = waveformReader; this.scheduler = scheduler;
        Choices = Array.AsReadOnly(new[] { new OHsHitNormalChoice("", "Default") }.Concat(choices.Where(c => c.Path.Length > 0))
            .Concat(new[] { current.NormalPath, current.SoftPath, current.DrumPath }.Where(p => p.Length > 0).Select(p => new OHsHitNormalChoice(p, p)))
            .DistinctBy(c => PathKey(c.Path), StringComparer.OrdinalIgnoreCase).ToArray());
        string Match(string path) => Choices.First(c => SamePath(c.Path, path)).Path;
        normalPath = Match(current.NormalPath); softPath = Match(current.SoftPath); drumPath = Match(current.DrumPath);
        Groups = Array.AsReadOnly(new OHsHitNormalSampleGroup[]
            { new("", true), new("Skin Defaults"), new("Mapset Samples"), new("Project Samples") });
        samples = Choices.Where(c => c.Path.Length > 0).Select(c =>
        {
            var source = InferSource(c);
            return new OHsHitNormalSample(c.Path, c.DisplayName, source, SourceGroup(source));
        }).ToList();
        var skinPaths = samples.Where(s => s.Source == OHsHitNormalSampleSource.Skin).Select(s => s.Path).ToArray();
        foreach (var name in new[] { "Normal", "Soft", "Drum" })
        {
            var path = OHsDefaultSamples.Resolve(skinPaths, name, SampleType.Normal);
            defaults[name] = path is not null ? samples.First(s => SamePath(s.Path, path)) :
                new("", OHsDefaultSamples.GetStem(name, SampleType.Normal), OHsHitNormalSampleSource.Skin, Groups[0]);
        }
        Slots = Array.AsReadOnly(new[] { "Normal", "Soft", "Drum" }.Select(name =>
            new OHsHitNormalSlot(name, SampleFor(name, CurrentPath(name)))).ToArray());
        selectedSlot = Slots[0];
        RefreshCatalog();
    }

    public IReadOnlyList<OHsHitNormalChoice> Choices { get; }
    public IReadOnlyList<OHsHitNormalSlot> Slots { get; }
    public IReadOnlyList<OHsHitNormalSampleGroup> Groups { get; }
    public ObservableCollection<OHsHitNormalSample> CatalogSamples { get; } = [];
    public IReadOnlyList<OHsHitNormalSample> PinnedSamples => CatalogSamples.Where(s => s.Group.IsPinned).ToArray();
    [ObservableProperty] private OHsHitNormalSlot selectedSlot;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] private string normalPath;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] private string softPath;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] private string drumPath;
    public bool CanSave => !deactivated && new[] { NormalPath, SoftPath, DrumPath }
        .All(path => OHsHitNormalSamples.GetPathError(path) is null && Choices.Any(c => SamePath(c.Path, path)));
    public OHsHitNormalConfiguration CreateResult() => CanSave ? new(NormalPath, SoftPath, DrumPath) : throw new InvalidOperationException("Invalid hitnormal selection.");

    private static string PathKey(string path) => path.Replace('\\', '/');
    private static bool SamePath(string left, string right) => string.Equals(PathKey(left), PathKey(right), StringComparison.OrdinalIgnoreCase);
    private static OHsHitNormalSampleSource InferSource(OHsHitNormalChoice choice) =>
        Path.IsPathFullyQualified(choice.Path) || OHsMapsetSamples.IsMapsetSamplePath(choice.Path) ? OHsHitNormalSampleSource.Mapset :
        PathKey(choice.Path).StartsWith(OHsDefaultSamples.FolderName + "/", StringComparison.OrdinalIgnoreCase) ? OHsHitNormalSampleSource.Skin : choice.Source;
    private OHsHitNormalSampleGroup SourceGroup(OHsHitNormalSampleSource source) => Groups[source switch
    {
        OHsHitNormalSampleSource.Skin => 1, OHsHitNormalSampleSource.Mapset => 2, _ => 3
    }];
    private string CurrentPath(string name) => name switch { "Normal" => NormalPath, "Soft" => SoftPath, _ => DrumPath };
    private OHsHitNormalSample SampleFor(string name, string path) =>
        path.Length == 0 ? defaults[name] : samples.FirstOrDefault(s => SamePath(s.Path, path)) ?? defaults[name];

    partial void OnSelectedSlotChanged(OHsHitNormalSlot value)
    {
        if (!Slots.Contains(value)) { SelectedSlot = Slots[0]; return; }
        RefreshCatalog();
    }
    partial void OnNormalPathChanged(string value) => RefreshCatalog();
    partial void OnSoftPathChanged(string value) => RefreshCatalog();
    partial void OnDrumPathChanged(string value) => RefreshCatalog();

    private bool CanAssignSample(OHsHitNormalSample? sample) =>
        !deactivated && !lifetime.IsCancellationRequested && sample is not null && CatalogSamples.Contains(sample);

    [RelayCommand(CanExecute = nameof(CanAssignSample))]
    private void AssignSample(OHsHitNormalSample sample)
    {
        if (!CanAssignSample(sample)) return;
        var path = ReferenceEquals(sample, defaults[SelectedSlot.Name]) ? "" : sample.Path;
        switch (SelectedSlot.Name)
        {
            case "Normal": NormalPath = path; break;
            case "Soft": SoftPath = path; break;
            case "Drum": DrumPath = path; break;
        }
    }

    private void RefreshCatalog()
    {
        foreach (var slot in Slots) slot.Sample = SampleFor(slot.Name, CurrentPath(slot.Name));
        var matchingDefault = defaults[SelectedSlot.Name];
        var current = SelectedSlot.Sample;
        foreach (var sample in samples.Concat(defaults.Values).Distinct())
        {
            sample.IsDefault = ReferenceEquals(sample, matchingDefault);
            sample.IsCurrent = ReferenceEquals(sample, current);
            sample.Group = sample.IsDefault || sample.IsCurrent ? Groups[0] : SourceGroup(sample.Source);
        }
        CatalogSamples.Clear();
        CatalogSamples.Add(matchingDefault);
        if (!ReferenceEquals(current, matchingDefault)) CatalogSamples.Add(current);
        foreach (var sample in samples.Where(s => !ReferenceEquals(s, matchingDefault) && !ReferenceEquals(s, current))
            .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.DisplayName, StringComparer.Ordinal))
            CatalogSamples.Add(sample);
        OnPropertyChanged(nameof(PinnedSamples));
        AssignSampleCommand.NotifyCanExecuteChanged();
    }
}
