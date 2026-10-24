using System.Windows;
using Hitbench.Resources.DataModels;
using Hitbench.Services.Ui;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class FollowupContractTests
{
    [TestCase(false), TestCase(true)]
    public async Task InstrumentalAppearanceCoordinatesFolderAndColor(bool fail)
    {
        var track = new Hitbench.Core.Entities.InstrumentalTrack { Name = "Piano", FolderName = "Piano", Color = "#ABCDEF", KeyFiles = new() { ["C3"] = "Piano/C3.wav" } };
        var files = new StubSampleFileService(["Piano/C3.wav"]) { FolderRename = (_, _, _, _, _) => fail ? Task.FromException<string>(new IOException("rename failed")) : Task.FromResult("Soft") };
        var vm = new Hitbench.ViewModels.Shell.WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new Hitbench.Core.Entities.WorkstationSource("map", "song", new Hitbench.Core.Entities.ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) },
            new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), sampleFiles: files,
            trackAppearanceDialog: new StubOHsTrackAppearanceDialogService { Request = (_, _) => Task.FromResult<OHsTrackAppearanceResult?>(new("Soft", "#123456")) });
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            await vm.ChangeTrackNameAndColorCommand.ExecuteAsync(track);
            Assert.That(track.Name, Is.EqualTo(fail ? "Piano" : "Soft"));
            Assert.That(track.FolderName, Is.EqualTo(fail ? "Piano" : "Soft"));
            Assert.That(track.KeyFiles["C3"], Is.EqualTo(fail ? "Piano/C3.wav" : "Soft/C3.wav"));
            Assert.That(track.Color, Is.EqualTo(fail ? "#ABCDEF" : "#123456"));
            if (fail) Assert.That(vm.ErrorMessage, Does.Contain("rename failed"));
        }
        finally { await vm.DeactivateAsync(); }
    }
    [TestCase("#123456"), TestCase(null)]
    public void InstrumentalAppearanceRoundTrips(string? color)
    {
        var project = new Hitbench.Core.Entities.ProjectData { Tracks = [new Hitbench.Core.Entities.InstrumentalTrack { Name = "Soft", Color = color }] };
        var restored = Hitbench.Services.ProjectFiles.ProjectDataSerializer.Deserialize(Hitbench.Services.ProjectFiles.ProjectDataSerializer.Serialize(project)).Tracks.Single();
        Assert.That(restored.Name, Is.EqualTo("Soft")); Assert.That(restored.Color, Is.EqualTo(color));
    }
    [Test]
    public void ClearingPianoRollColorRestoresLivePalette()
    {
        var roll = new Hitbench.Resources.CustomControls.PianoRollView();
        var original = new System.Windows.Media.SolidColorBrush();
        roll.Resources["Brush.Track.1"] = original;
        roll.TrackColor = "#123456";
        Assert.That(roll.MarkerBrush, Is.Not.SameAs(original));
        roll.TrackColor = null;
        Assert.That(roll.MarkerBrush, Is.SameAs(original));
        var replacement = new System.Windows.Media.SolidColorBrush();
        roll.Resources["Brush.Track.1"] = replacement;
        Assert.That(roll.MarkerBrush, Is.SameAs(replacement));
    }
    [Test]
    public async Task TimestampStartsWithMilliseconds()
    {
        var vm = new Hitbench.ViewModels.Shell.WorkstationPageVM(new StubWorkstationSourceService(),
            new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        try { Assert.That(vm.ElapsedText, Is.EqualTo("00:00.000")); }
        finally { await vm.DeactivateAsync(); }
    }
    [TestCase("C1", "C1"), TestCase("C8", "C8"), TestCase("C#1", ""), TestCase("D1", "")]
    public void KeyLabelsPreserveIdentity(string key, string label)
    {
        var entry = new KeyMappingEntry(key);
        Assert.That(entry.KeyboardLabel, Is.EqualTo(label));
        Assert.That(entry.Key, Is.EqualTo(key));
    }
    [Test]
    public void ThemeChangesRefreshFrameUntilDisposed()
    {
        var service = new OHsThemeService(new ResourceDictionary(), new StubAppSettingsService(), new ImmediateUiScheduler());
        int calls = 0;
        using var frame = new OHsWindowFrame(new Window(), _ => calls++, themeChanges: service);
        service.ApplyInitialTheme(1);
        Assert.That(calls, Is.EqualTo(1));
        frame.Dispose(); var prior = calls;
        service.ApplyInitialTheme(0);
        Assert.That(calls, Is.EqualTo(prior));
    }
}
