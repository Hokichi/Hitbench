namespace Hitbench.Services.FileSystem;

public sealed record SampleImportResult
{
    public SampleImportResult(IReadOnlyList<string> importedFiles, IReadOnlyList<string> failures, bool wasCancelled)
    { ImportedFiles = Array.AsReadOnly(importedFiles.ToArray()); Failures = Array.AsReadOnly(failures.ToArray()); WasCancelled = wasCancelled; }
    public IReadOnlyList<string> ImportedFiles { get; }
    public IReadOnlyList<string> Failures { get; }
    public bool WasCancelled { get; }
}
