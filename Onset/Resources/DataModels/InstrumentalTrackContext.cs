namespace Onset.Resources.DataModels;

public sealed record InstrumentalTrackContext(string ProjectPath, string? InitialDirectory, Guid ProjectSessionId, Guid EditorSessionId, Guid TrackId);
