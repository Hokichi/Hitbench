using Hitbench.Core.Entities;
using Hitbench.Core.Enums;
using Hitbench.Services;
using Hitbench.Services.ProjectFiles;

namespace Hitbench.Test.Services;

public class OHsDefaultSamplesTests
{
    [Test]
    public void SkinStateRoundTripsAndRejectsWrongTypes()
    {
        var project = new ProjectData { AllowDefaults = true, CurrentSkin = "My Skin", Tracks = [new Track { UsesDefaultSample = true }] };
        var copy = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(project));
        Assert.That(copy.AllowDefaults, Is.True);
        Assert.That(copy.CurrentSkin, Is.EqualTo("My Skin"));
        Assert.That(copy.Tracks[0].UsesDefaultSample, Is.True);
        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize("{\"AllowDefaults\":\"yes\"}"));
        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize("{\"CurrentSkin\":12}"));
    }

    [Test]
    public void LegacyCustomTracksKeepTheirSamplesAndEmptyTracksBecomeDefault()
    {
        var project = ProjectDataSerializer.Deserialize("{\"Tracks\":[{\"Samples\":[{\"FileName\":\"a.wav\",\"Timestamp\":25}]},{}]}");
        Assert.That(project.AllowDefaults, Is.False);
        Assert.That(project.CurrentSkin, Is.Empty);
        Assert.That(project.Tracks[0].UsesDefaultSample, Is.False);
        Assert.That(project.Tracks[0].Samples[0].FileName, Is.EqualTo("a.wav"));
        Assert.That(project.Tracks[1].UsesDefaultSample, Is.True);
    }

    [Test]
    public void ExactStemAndExtensionResolution()
    {
        Assert.That(OHsDefaultSamples.GetStem("Drum", SampleType.Clap), Is.EqualTo("drum-hitclap"));
        string[] files = ["SkinDefault/DRUM-hitclap.ogg", "SkinDefault/drum-hitclap.wav", "SkinDefault/soft-hitclap.wav"];
        Assert.That(OHsDefaultSamples.Resolve(files, "Drum", SampleType.Clap), Is.EqualTo(files[1]));
        Assert.That(OHsDefaultSamples.Resolve(files, "Drum", SampleType.Finish), Is.Null);
    }

    [Test]
    public void NoneRequiresOnlyActiveTimelineFamilies()
    {
        var track = new Track { UsesDefaultSample = true, Addition = "None", SampleType = SampleType.Clap };
        var project = new ProjectData { AllowDefaults = true, ControlPoints = [new() { Timestamp = -10, Sampleset = "Soft" }, new() { Timestamp = 100, Sampleset = "Drum" }, new() { Timestamp = 200, Sampleset = "Normal", IsDisabled = true }] };
        string[] files = ["SkinDefault/soft-hitclap.wav"];
        Assert.That(OHsDefaultSamples.IsAvailable(project, track, files, 90), Is.True);
        Assert.That(OHsDefaultSamples.IsAvailable(project, track, files, 300), Is.False);
        Assert.That(OHsDefaultSamples.GetSampleset(project, track, 100), Is.EqualTo("Drum"));
        project.AllowDefaults = false;
        Assert.That(OHsDefaultSamples.IsAvailable(project, track, files, 90), Is.False);
    }
}
