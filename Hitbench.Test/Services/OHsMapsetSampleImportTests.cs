using Hitbench.Services.FileSystem;
using Hitbench.Test.Presentation;

namespace Hitbench.Test.Services;

public sealed class OHsMapsetSampleImportTests
{
    [TestCase("Mapset/DRUM-hitclap.wav", true)]
    [TestCase("drum-hitclap.wav", false)]
    [TestCase("Piano/drum-hitclap.wav", false)]
    [TestCase("SkinDefault/drum-hitclap.wav", false)]
    public async Task DuplicateClassificationUsesOnlyDirectMapsetChildren(string existing, bool duplicate)
    {
        var root = Directory.CreateTempSubdirectory("Hitbench-duplicate-");
        try
        {
            var source = Path.Combine(root.FullName, "drum-hitclap.wav");
            await File.WriteAllTextAsync(source, "new");
            var dialog = new StubOHsMapsetSampleImportDialogService();
            dialog.Select = (paths, _) =>
            {
                Assert.That(paths, Is.EqualTo(new[] { source }));
                Assert.That(dialog.DuplicatePaths.Contains(source), Is.EqualTo(duplicate));
                return Task.FromResult<IReadOnlyList<string>>([]);
            };
            var result = await new OHsMapsetSampleImportService(new StubSampleFileService([existing]), dialog)
                .ImportAsync(Path.Combine(root.FullName, "project.json"), root.FullName, default);
            Assert.That(result.ImportedFiles, Is.Empty);
        }
        finally { root.Delete(true); }
    }
    [Test]
    public async Task SelectionCopiesOnlyCheckedFilesAndSelectedDuplicateReplacesOriginalCopy()
    {
        var root = Directory.CreateTempSubdirectory("Hitbench-mapset-import-");
        try
        {
            var mapset = root.CreateSubdirectory("song");
            var project = root.CreateSubdirectory("project");
            var source = Path.Combine(mapset.FullName, "drum-hitnormal9.wav");
            await File.WriteAllTextAsync(source, "original");
            await File.WriteAllTextAsync(Path.Combine(mapset.FullName, "soft-hitclap.wav"), "unchecked");
            var dialog = new StubOHsMapsetSampleImportDialogService
            {
                Select = (paths, _) => Task.FromResult<IReadOnlyList<string>>(paths.Where(p => p == source).ToArray())
            };
            var service = new OHsMapsetSampleImportService(new SampleFileService(root.FullName), dialog);
            var path = Path.Combine(project.FullName, "project.json");
            var first = await service.ImportAsync(path, mapset.FullName, default);
            Assert.That(first.ImportedFiles, Is.EqualTo(new[] { "Mapset/drum-hitnormal9.wav" }));
            await File.WriteAllTextAsync(source, "changed");
            var second = await service.ImportAsync(path, mapset.FullName, default);
            Assert.That(second.ImportedFiles, Is.EqualTo(first.ImportedFiles));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(project.FullName, "Samples", "Mapset", "drum-hitnormal9.wav")), Is.EqualTo("changed"));
            Assert.That(Directory.GetFiles(Path.Combine(project.FullName, "Samples", "Mapset")), Has.Length.EqualTo(1));
        }
        finally { root.Delete(true); }
    }

    [TestCase("empty")] [TestCase("no")] [TestCase("disappear")] [TestCase("foreign")]
    public async Task UnavailableOrUnselectedSourcesCannotCopy(string mode)
    {
        var root=Directory.CreateTempSubdirectory("Hitbench-mapset-cases-");
        try
        {
            var song=root.CreateSubdirectory("song");var source=Path.Combine(song.FullName,"normal-hitnormal.wav");
            if(mode!="empty") await File.WriteAllTextAsync(source,"sample");
            var calls=0; var attempts=0;
            var files=new StubSampleFileService([]) { Import=(_,p,_)=>{attempts++;return Task.FromResult(p);} };
            var dialog=new StubOHsMapsetSampleImportDialogService { Select=(paths,_)=>
            {
                calls++;if(mode=="disappear") File.Delete(source);
                return Task.FromResult<IReadOnlyList<string>>(mode=="no"?[]:mode=="foreign"?[Path.Combine(root.FullName,"drum-hitclap.wav")]:paths);
            }};
            var result=await new OHsMapsetSampleImportService(files,dialog).ImportAsync(Path.Combine(root.FullName,"project.json"),song.FullName,default);
            Assert.That(attempts,Is.Zero);Assert.That(result.ImportedFiles,Is.Empty);
            Assert.That(calls,Is.EqualTo(mode=="empty"?0:1));
            Assert.That(result.Failures.Count,Is.EqualTo(mode=="disappear"?1:0));
        }
        finally {root.Delete(true);}
    }
    [Test]
    public async Task PartialFailureContinuesAndCancellationPreservesCompletedCopies()
    {
        var root=Directory.CreateTempSubdirectory("Hitbench-mapset-partial-");
        using var cancellation=new CancellationTokenSource();
        try
        {
            foreach(var name in new[]{"drum-hitclap.wav","normal-hitnormal.wav","soft-hitfinish.wav"})
                await File.WriteAllTextAsync(Path.Combine(root.FullName,name),"sample");
            var files=new StubSampleFileService([]) { Import=(_,path,_)=>
            {
                if(Path.GetFileName(path)=="drum-hitclap.wav") throw new IOException("copy denied");
                cancellation.Cancel();return Task.FromResult("Mapset/"+Path.GetFileName(path));
            }};
            var result=await new OHsMapsetSampleImportService(files,new StubOHsMapsetSampleImportDialogService()).ImportAsync("project.json",root.FullName,cancellation.Token);
            Assert.That(result.Failures,Has.Count.EqualTo(1));
            Assert.That(result.ImportedFiles,Is.EqualTo(new[]{"Mapset/normal-hitnormal.wav"}));Assert.That(result.WasCancelled,Is.True);
        }
        finally {root.Delete(true);}
    }

    [TestCase(false)] [TestCase(true)]
    public async Task LinkedSourceIsExcludedBeforeOrAfterSelection(bool afterSelection)
    {
        var root=Directory.CreateTempSubdirectory("Hitbench-mapset-link-");
        try
        {
            var source=Path.Combine(root.FullName,"drum-hitnormal9.wav");var target=Path.Combine(root.FullName,"keep.bin");
            await File.WriteAllTextAsync(source,"sample");await File.WriteAllTextAsync(target,"keep");
            void Link() {File.Delete(source);File.CreateSymbolicLink(source,target);}
            if(!afterSelection) Link();
            var copies=0;var files=new StubSampleFileService([]) {Import=(_,p,_)=>{copies++;return Task.FromResult(p);}};
            var dialog=new StubOHsMapsetSampleImportDialogService {Select=(paths,_)=>{if(afterSelection)Link();return Task.FromResult(paths);}};
            var result=await new OHsMapsetSampleImportService(files,dialog).ImportAsync("project.json",root.FullName,default);
            Assert.That(copies,Is.Zero);Assert.That(result.Failures.Count,Is.EqualTo(afterSelection?1:0));
            Assert.That(await File.ReadAllTextAsync(target),Is.EqualTo("keep"));
        }
        finally {root.Delete(true);}
    }
}
