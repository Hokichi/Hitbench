namespace osuHitsounder.Test.Services;

public sealed class TestProjectFolder : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"osuHitsounder-{Guid.NewGuid():N}");

    public TestProjectFolder(params string[] sampleNames)
    {
        Directory.CreateDirectory(Path.Combine(folder, "Samples"));
        foreach (var name in sampleNames) File.WriteAllBytes(Path.Combine(folder, "Samples", name), [0]);
    }

    public string ProjectPath => Path.Combine(folder, "project.json");
    public void Dispose() => Directory.Delete(folder, true);
}
