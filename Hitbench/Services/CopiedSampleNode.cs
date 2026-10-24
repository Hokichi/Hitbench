using Hitbench.Core.Entities;

namespace Hitbench.Services;

public sealed record CopiedSampleNode(Track Track, string FileName, long RelativeTimestamp);
