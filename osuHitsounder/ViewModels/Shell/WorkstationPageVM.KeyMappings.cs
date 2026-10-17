using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.Ui;
using osuHitsounder.Services.Dialogs;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.Resources.Messages;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.IO;

using osuHitsounder.Core.Constants;
using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private void RegisterKeyMappingMessages()
    {
        if (messenger is null) return;
        messenger.Register<OHsClearInstrumentalKeyMappingsRequestedMessage>(this, (_, m) => ClearInstrumentKeyMappings(m));
        messenger.Register<SetInstrumentalKeyMappingRequestedMessage>(this, (_, m) => MapInstrumentKey(m));
        messenger.Register<SetInstrumentalKeyMappingsRequestedMessage>(this, (_, m) => MapInstrumentKeys(m));
    }
    private void ClearInstrumentKeyMappings(OHsClearInstrumentalKeyMappingsRequestedMessage request)
    {
        if (ActiveInstrument(request.Context) is not { } track) return;
        if (request.ExpectedRevision != instrumentRevision)
        { InstrumentResult(request.Context, request.RequestId, false, null, "Mappings changed. Review them and try again."); return; }
        var changed = track.KeyFiles.Count > 0;
        track.KeyFiles.Clear();
        InstrumentResult(request.Context, request.RequestId, changed, null);
    }
    private void MapInstrumentKey(SetInstrumentalKeyMappingRequestedMessage m)
    {
        if (ActiveInstrument(m.Context) is not { } track) return;
        var file = string.IsNullOrWhiteSpace(m.FileName) ? null : m.FileName.Replace('\\', '/');
        var error = !InstrumentalKeys.TryGetIndex(m.Key, out _) ? "Choose C1–B9." : file is not null ? SamplePaths.GetRelativePathError(file) : null;
        if (error is null && m.FolderName is { } folder && (SamplePaths.GetFolderNameError(folder) is not null ||
            tracks.OfType<InstrumentalTrack>().Any(t => t != track && string.Equals(t.FolderName, folder, StringComparison.OrdinalIgnoreCase)))) error = "Choose an unreserved sample folder.";
        if (error is not null) { InstrumentResult(m.Context, m.RequestId, false, null, error); return; }
        if (file is not null) file = availableSamples.FirstOrDefault(n => string.Equals(n, file, StringComparison.OrdinalIgnoreCase)) ?? file;
        track.KeyFiles.TryGetValue(m.Key, out var old);
        var changed = old != file || m.FolderName is not null && track.FolderName != m.FolderName;
        if (m.FolderName is not null) track.FolderName = m.FolderName;
        if (changed) { if (file is null) track.KeyFiles.Remove(m.Key); else track.KeyFiles[m.Key] = file; }
        InstrumentResult(m.Context, m.RequestId, changed, null);
    }

    private void MapInstrumentKeys(SetInstrumentalKeyMappingsRequestedMessage m)
    {
        if (ActiveInstrument(m.Context) is not { } track) return;
        var error = SamplePaths.GetFolderNameError(m.FolderName);
        if (error is null && tracks.OfType<InstrumentalTrack>().Any(t => t != track && string.Equals(t.FolderName, m.FolderName, StringComparison.OrdinalIgnoreCase))) error = "This sample folder belongs to another track.";
        if (error is null && m.Mappings.Any(pair => !InstrumentalKeys.TryGetIndex(pair.Key, out _) || SamplePaths.GetRelativePathError(pair.Value) is not null)) error = "Choose valid keys and sample paths.";
        if (error is not null) { InstrumentResult(m.Context, m.RequestId, false, null, error); return; }
        var mappings = m.Mappings.ToDictionary(pair => pair.Key, pair => pair.Value.Replace('\\', '/'));
        var changed = track.FolderName != m.FolderName || mappings.Any(pair => !track.KeyFiles.TryGetValue(pair.Key, out var old) || old != pair.Value);
        track.FolderName = m.FolderName;
        foreach (var pair in mappings) track.KeyFiles[pair.Key] = pair.Value;
        InstrumentResult(m.Context, m.RequestId, changed, null);
    }
}
