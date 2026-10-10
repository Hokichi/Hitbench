using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Services.Dialogs;

public interface ISampleManagementDialogService
{
    Task ShowAsync(SampleManagementContext context, CancellationToken token);
}
