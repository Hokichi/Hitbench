using osuHitsounder.Services.Dialogs;

namespace osuHitsounder.Test.Presentation;

public sealed class StubProjectCreationDialogService : IProjectCreationDialogService
{
    public string? Result { get; set; }
    public string? SuggestedName { get; private set; }
    public IReadOnlySet<string>? ExistingNames { get; private set; }

    public Task<string?> RequestNameAsync(
        string suggestedName,
        IReadOnlySet<string> existingNames,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SuggestedName = suggestedName;
        ExistingNames = existingNames;
        return Task.FromResult(Result);
    }
}
