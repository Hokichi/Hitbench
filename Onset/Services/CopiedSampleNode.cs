using Onset.Core.Entities;

namespace Onset.Services;

public sealed record CopiedSampleNode(Track Track, string FileName, long RelativeTimestamp);
