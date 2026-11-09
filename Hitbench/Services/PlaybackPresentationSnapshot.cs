using Hitbench.Core.Entities;

namespace Hitbench.Services;

public sealed record PlaybackPresentationSnapshot(long Revision, AudioPlaybackSnapshot Playback);
