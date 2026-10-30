namespace Hitbench.Services.FileSystem;
internal readonly record struct SampleFileStamp(string Path, long Length, DateTime LastWriteTimeUtc);
