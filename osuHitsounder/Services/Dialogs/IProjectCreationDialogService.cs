namespace osuHitsounder.Services.Dialogs;

public interface IProjectCreationDialogService
{
    Task<string?> RequestNameAsync(
        string suggestedName,
        IReadOnlySet<string> existingNames,
        CancellationToken cancellationToken);
}
