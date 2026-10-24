namespace Onset.Services;

public sealed record AudioOutputCandidate(string Name, Func<IAudioOutput> Create);
