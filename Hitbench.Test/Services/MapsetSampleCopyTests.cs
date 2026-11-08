using Hitbench.Services.FileSystem;
namespace Hitbench.Test.Services;
public sealed class MapsetSampleCopyTests
{
    [TestCase(false)] [TestCase(true)]
    public async Task DestinationRaceNeverOverwritesOrCreatesNumberedCopy(bool directory)
    {
        var root=Directory.CreateTempSubdirectory("Hitbench-mapset-race-");
        try
        {
            var source=Path.Combine(root.FullName,"drum-hitnormal9.wav");await File.WriteAllTextAsync(source,"source");
            var project=Path.Combine(root.FullName,"project","project.json");
            var destination=Path.Combine(root.FullName,"project","Samples","Mapset","drum-hitnormal9.wav");
            var service=new SampleFileService(root.FullName,async(input,output,token)=>
            {
                await input.CopyToAsync(output,token);
                if(directory) Directory.CreateDirectory(destination);else await File.WriteAllTextAsync(destination,"keep",token);
            });
            if(directory) Assert.ThrowsAsync<IOException>(()=>service.ImportAsync(project,source,default,"Mapset",skipExisting:true));
            else
            {
                Assert.That(await service.ImportAsync(project,source,default,"Mapset",skipExisting:true),Is.EqualTo("Mapset/drum-hitnormal9.wav"));
                Assert.That(await File.ReadAllTextAsync(destination),Is.EqualTo("keep"));
            }
            Assert.That(Directory.GetFileSystemEntries(Path.GetDirectoryName(destination)!),Has.Length.EqualTo(1));
            Assert.That(await File.ReadAllTextAsync(source),Is.EqualTo("source"));
        }
        finally {root.Delete(true);}
    }
    [Test]
    public async Task NewInstrumentalFoldersAvoidReservedMapsetName()
    {
        var root=Directory.CreateTempSubdirectory("Hitbench-mapset-folder-");
        try
        {
            var service=new SampleFileService(root.FullName);
            Assert.That(await service.EnsureFolderAsync(Path.Combine(root.FullName,"project.json"),"Mapset","",[],default),Is.EqualTo("Mapset (2)"));
        }
        finally {root.Delete(true);}
    }
}
