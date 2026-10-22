using Soundie.Core.Entities;

namespace Soundie.Services;

public sealed record CopiedSampleNode(Track Track, string FileName, long RelativeTimestamp);
