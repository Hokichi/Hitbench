using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.Services.ProjectFiles;
namespace Hitbench.Test.Services;
public class UninheritedPointTests
{
 [Test] public void LegacyTimingConvertsAndSerializesOnlyPoints() {
 var p=ProjectDataSerializer.Deserialize("{\"Offsets\":[-100,800],\"BPMs\":[120,240]}", out var migrate);
 Assert.That(migrate,Is.True);
 Assert.That(p.UninheritedPoints.Select(x=>(x.Offset,x.BPM)),Is.EqualTo(new[]{(-100,120),(800,240)}));
 var json=Newtonsoft.Json.Linq.JObject.Parse(ProjectDataSerializer.Serialize(p));
 Assert.That(json["Offsets"],Is.Null); Assert.That(json["BPMs"],Is.Null);
 }
 [Test] public void MarkerSelectsBoundaryAndPreviousSection() {
 var clock=new PlaybackMarkerClock(new UninheritedPoint[]{new(){Offset=100,BPM=120},new(){Offset=800,BPM=180},new(){Offset=800,BPM=240}});
 Assert.That(clock.GetBpm(TimeSpan.Zero),Is.EqualTo(120));
 Assert.That(clock.GetBpm(TimeSpan.FromMilliseconds(800)),Is.EqualTo(240));
 Assert.That(clock.GetBpm(TimeSpan.FromMilliseconds(799)),Is.EqualTo(120));
 }

 [TestCase("{\"Offsets\":[0],\"BPMs\":[]}")]
 [TestCase("{\"Offsets\":[0.5],\"BPMs\":[120]}")]
 [TestCase("{\"UninheritedPoints\":[null]}")]
 [TestCase("{\"UninheritedPoints\":[{\"Offset\":0}]}")]
 [TestCase("{\"UninheritedPoints\":[{\"Offset\":0,\"BPM\":2147483648}]}")]
 [TestCase("{\"UninheritedPoints\":[{\"Offset\":0,\"BPM\":120,\"bpm\":120}]}")]
 public void MalformedTimingIsRejected(string json) => Assert.Throws<InvalidDataException>(()=>ProjectDataSerializer.Deserialize(json));
 [Test] public void NewFormatWinsAndNormalizesCase() {
 var project=ProjectDataSerializer.Deserialize("{\"uninheritedpoints\":[{\"offset\":-100,\"bpm\":180}],\"Offsets\":false,\"BPMs\":null,\"ControlPoints\":[{\"Timestamp\":-100}]}",out var migrated);
 Assert.That(migrated,Is.True); Assert.That(project.UninheritedPoints.Single().BPM,Is.EqualTo(180));
 var reopened=ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(project),out var again);
 Assert.That(again,Is.False);Assert.That(reopened.ControlPoints.Single().Timestamp,Is.EqualTo(-100));
 }
 [Test] public async Task FailedAtomicReplacementPreservesOriginal() {
 var folder=Directory.CreateTempSubdirectory("timing-atomic-");
 try {
 var path=Path.Combine(folder.FullName,"project.json"); await File.WriteAllTextAsync(path,"original");
 using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
 Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new ProjectFileService(folder.FullName).SaveAsync(path,"replacement",default));
 Assert.That(await File.ReadAllTextAsync(path),Is.EqualTo("original"));
 Assert.That(Directory.GetFiles(folder.FullName).Length,Is.EqualTo(1));
 }finally{folder.Delete(true);}
 }
}
