using Soundie.Resources.DataModels;
using Soundie.Services.FileSystem;
namespace Soundie.Test.Services;
public class OHsExportDestinationTests
{
    [Test]
    public async Task OverwriteReplacesAllContent()
    {
        using var folder = new TestProjectFolder();
        var output = Path.Combine(Path.GetDirectoryName(folder.ProjectPath)!, "Export");
        Directory.CreateDirectory(Path.Combine(output,"notes"));
        File.WriteAllText(Path.Combine(output,"notes","keep.txt"),"old");
        File.WriteAllText(Path.Combine(output,".osuhitsounder-export.json"),"invalid");
        await new OHsExportPublisher().PublishAsync(folder.ProjectPath, new Dictionary<string,string>{{"new.wav","sample"}},
            (stage, token) => File.WriteAllTextAsync(Path.Combine(stage,"new.wav"),"new",token), default, overwriteEverything:true);
        Assert.That(Directory.GetDirectories(output), Is.Empty);
        Assert.That(File.ReadAllText(Path.Combine(output,"new.wav")),Is.EqualTo("new"));
    }
    [TestCase(false)] [TestCase(true)]
    public async Task BeatmapConflictNeverBuildsOrOverwrites(bool directory)
    {
        using var folder = new TestProjectFolder();
        var root=Path.GetDirectoryName(folder.ProjectPath)!;
        var conflict=Path.Combine(root,"NEW.wav");
        if(directory) Directory.CreateDirectory(conflict); else File.WriteAllText(conflict,"keep");
        var calls=0;
        Assert.ThrowsAsync<IOException>(()=>new OHsExportPublisher().PublishBeatmapAsync(Path.Combine(root,"map.osu"),new Dictionary<string,string>{{"new.wav","sample"}},
            (_,_)=>{calls++;return Task.CompletedTask;},default));
        Assert.That(calls,Is.Zero);
        if(!directory) Assert.That(await File.ReadAllTextAsync(conflict),Is.EqualTo("keep"));
    }
    [Test]
    public async Task BeatmapSuccessHasNoManifestAndRepeatFails()
    {
        using var folder = new TestProjectFolder(); var root=Path.GetDirectoryName(folder.ProjectPath)!;
        var files=new Dictionary<string,string>{{"new.wav","sample"}};
        Task Build(string stage,CancellationToken token)=>File.WriteAllTextAsync(Path.Combine(stage,"new.wav"),"new",token);
        var publisher=new OHsExportPublisher();
        Assert.That(await publisher.PublishBeatmapAsync(Path.Combine(root,"map.osu"),files,Build,default),Is.EqualTo(root));
        Assert.That(File.Exists(Path.Combine(root,".osuhitsounder-export.json")),Is.False);
        Assert.ThrowsAsync<IOException>(()=>publisher.PublishBeatmapAsync(Path.Combine(root,"map.osu"),files,Build,default));
    }
    [Test]
    public void InvalidDestinationFailsValidation()
    {
        Assert.Throws<ArgumentException>(() => new OHsExportOptions("Hitsounds",1,".wav",".wav",false,(OHsExportDestination)99).Validate());
    }
    [Test]
    public async Task FailedOverwritePreservesPreviousFolder()
    {
        using var folder=new TestProjectFolder(); var output=Path.Combine(Path.GetDirectoryName(folder.ProjectPath)!,"Export");
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output,"keep.txt"),"keep");
        Assert.ThrowsAsync<IOException>(()=>new OHsExportPublisher().PublishAsync(folder.ProjectPath,new Dictionary<string,string>{{"new.wav","sample"}},
            (_,_)=>throw new IOException("build failed"),default,overwriteEverything:true));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output,"keep.txt")),Is.EqualTo("keep"));
    }
    [TestCase(false)] [TestCase(true)]
    public async Task BeatmapPreparationConflictOrMoveFailureRollsBack(bool conflict)
    {
        using var folder=new TestProjectFolder(); var root=Path.GetDirectoryName(folder.ProjectPath)!;
        var files=new Dictionary<string,string>{{"a.wav","sample"},{"b.wav","sample"}};
        var moves=0;
        var publisher=new OHsExportPublisher(Directory.Move,(a,b)=>{if(++moves==2)throw new IOException("move failed");File.Move(a,b);});
        Assert.ThrowsAsync<IOException>(()=>publisher.PublishBeatmapAsync(Path.Combine(root,"map.osu"),files,async(stage,token)=>
        {
            foreach(var name in files.Keys)await File.WriteAllTextAsync(Path.Combine(stage,name),"new",token);
            if(conflict)await File.WriteAllTextAsync(Path.Combine(root,"b.wav"),"keep",token);
        },default));
        Assert.That(File.Exists(Path.Combine(root,"a.wav")),Is.False);
        Assert.That(File.Exists(Path.Combine(root,"b.wav")),Is.EqualTo(conflict));
        if(conflict)Assert.That(await File.ReadAllTextAsync(Path.Combine(root,"b.wav")),Is.EqualTo("keep"));
    }
    [TestCase(false)] [TestCase(true)]
    public async Task ServiceRoutesToBeatmapAndConflictsBeforeDecode(bool overwrite)
    {
        using var project=new TestProjectFolder("a.wav"); using var mapFolder=new TestProjectFolder();
        var root=Path.GetDirectoryName(mapFolder.ProjectPath)!;
        var source=Path.Combine(root,"Song [Easy].osu");
        await File.WriteAllTextAsync(source,"[General]\nAudioFilename: song.wav\n[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        var data=new Soundie.Core.Entities.ProjectData { Tracks=[new(){Addition="Soft",SampleType=Soundie.Core.Enums.SampleType.Clap,Volume=100,
            Samples=[new(){FileName="a.wav",Timestamp=100}]}] };
        var request=new OHsExportRequest(Guid.NewGuid(),project.ProjectPath,source,"song.wav",
            Soundie.Services.ProjectFiles.ProjectDataSerializer.Serialize(data),new("Hitsounds",1,".wav",".wav",false,OHsExportDestination.Beatmap,overwrite));
        var calls=0;
        var exporter=new Soundie.Services.OHsExportService(decode:(_,_)=>{calls++;return Soundie.Services.DecodedAudio.CopyFrom(new float[]{0.2f,-0.2f});});
        var conflict=Path.Combine(root,"Song [Hitsounds].osu"); await File.WriteAllTextAsync(conflict,"keep");
        Assert.ThrowsAsync<IOException>(()=>exporter.ExportAsync(request,default));
        Assert.That(calls,Is.Zero);Assert.That(await File.ReadAllTextAsync(conflict),Is.EqualTo("keep"));
        File.Delete(conflict);
        Assert.That(await exporter.ExportAsync(request,default),Is.EqualTo(root));
        Assert.That(File.Exists(conflict),Is.True);
        Assert.That(Directory.Exists(Path.Combine(Path.GetDirectoryName(project.ProjectPath)!,"Export")),Is.False);
    }
    [TestCase(false)] [TestCase(true)]
    public void BeatmapCancelledOrUnexpectedBuildPublishesNothing(bool cancel)
    {
        using var folder=new TestProjectFolder();var root=Path.GetDirectoryName(folder.ProjectPath)!;
        using var cancellation=new CancellationTokenSource();
        Assert.CatchAsync<Exception>(()=>new OHsExportPublisher().PublishBeatmapAsync(Path.Combine(root,"map.osu"),new Dictionary<string,string>{{"a.wav","sample"}},async(stage,token)=>
        {await File.WriteAllTextAsync(Path.Combine(stage,"a.wav"),"new",token);if(cancel)cancellation.Cancel();else await File.WriteAllTextAsync(Path.Combine(stage,"extra.wav"),"bad",token);},cancellation.Token));
        Assert.That(File.Exists(Path.Combine(root,"a.wav")),Is.False);
    }
}


