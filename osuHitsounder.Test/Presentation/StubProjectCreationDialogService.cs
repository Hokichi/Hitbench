using osuHitsounder.Services.Dialogs;

namespace osuHitsounder.Test.Presentation;

public sealed class StubProjectCreationDialogService : IProjectCreationDialogService
{
    public string? Result { get; set; }
    public Func<string, IReadOnlySet<string>, CancellationToken, Task<string?>>? Request { get; set; }
    public int CallCount { get; private set; }
    public string? SuggestedName { get; private set; }
    public IReadOnlySet<string>? ExistingNames { get; private set; }

    public Task<string?> RequestNameAsync(
        string suggestedName,
        IReadOnlySet<string> existingNames,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        SuggestedName = suggestedName;
        ExistingNames = existingNames;
        return Request?.Invoke(suggestedName, existingNames, cancellationToken) ?? Task.FromResult(Result);
    }
}
