using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.Messages;
using System.IO;
namespace Hitbench.ViewModels.Popups;

public sealed partial class ManageSamplesPopupVM
{
    private bool reservedForDropImport;
    private bool externalImportRegistered;
    private bool SameContext(Hitbench.Resources.DataModels.SampleManagementContext other) => Context.ProjectPath == other.ProjectPath && Context.SessionId == other.SessionId;
    private void RegisterExternalImport()
    {
        if (externalImportRegistered) return;
        externalImportRegistered = true;
        messenger.Register<OHsPrepareSampleDropImportMessage>(this, (_, m) => { if (!SameContext(m.Context)) return; m.Reply(ReserveForImportAsync()); });
        messenger.Register<OHsSampleDropImportFinishedMessage>(this, (_, m) => { if (!SameContext(m.Context) || !reservedForDropImport) return; reservedForDropImport = false; IsBusy = false; });
        messenger.Register<ProjectSamplesChangedMessage>(this, (_, m) =>
        {
            if (deactivated || !SameContext(m.Context)) return;
            var names = m.IncludesSubfolders ? m.Files.ToArray() :
                m.Files.Where(n => !n.Contains('/') && !n.Contains('\\'))
                    .Concat(allSampleFiles.Where(n => n.Contains('/') || n.Contains('\\'))).ToArray();
            if (allSampleFiles.SequenceEqual(names)) return;
            InvalidateWaveforms(); Replace(names);
        });
    }
    private Task<bool> ReserveForImportAsync()
    {
        if (!CanAct()) return Task.FromResult(false);
        reservedForDropImport = true; IsBusy = true;
        var preparation = PrepareAsync(); operation = preparation; return preparation;
        async Task<bool> PrepareAsync() { try { await StopPreviewAsync(); return !deactivated; } catch (Exception e) { ErrorMessage = e.Message; reservedForDropImport = false; IsBusy = false; return false; } }
    }
}
