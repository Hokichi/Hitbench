using System.Collections;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.Messages;
using Hitbench.Services.ProjectFiles;

namespace Hitbench.ViewModels.Shell.HomePage;

public partial class HomePageVM
{
    [ObservableProperty] private string draftProjectName = "";
    [ObservableProperty] private bool isProjectNameEditorVisible;
    [ObservableProperty] private bool isLoadingProjectNames;
    private HashSet<string>? existingProjectNames;
    private int projectDraftGeneration;
    public string? ProjectNameError => ProjectNameRules.GetSyntaxError(DraftProjectName)
        ?? (existingProjectNames?.Contains(DraftProjectName.Trim()) == true ? "Project already exists." : null);
    public bool HasErrors => ProjectNameError is not null;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    public IEnumerable GetErrors(string? propertyName) => propertyName is null or "" or nameof(DraftProjectName)
        && ProjectNameError is { } error ? new[] { error } : Array.Empty<string>();

    partial void OnDraftProjectNameChanged(string value) => RefreshProjectNameValidation();
    partial void OnIsLoadingProjectNamesChanged(bool value) => RefreshCommands();
    partial void OnIsProjectNameEditorVisibleChanged(bool value) => RefreshCommands();
    private void RefreshProjectNameValidation()
    {
        OnPropertyChanged(nameof(ProjectNameError));
        OnPropertyChanged(nameof(HasErrors));
        ErrorsChanged?.Invoke(this, new(nameof(DraftProjectName)));
        CreateProjectCommand.NotifyCanExecuteChanged();
    }
    private void ResetProjectDraft()
    {
        projectDraftGeneration++;
        IsProjectNameEditorVisible = false;
        IsLoadingProjectNames = false;
        existingProjectNames = null;
        DraftProjectName = "";
        RefreshProjectNameValidation();
    }
    private bool CanShowProjectCreation() => active && CanInteract && HasSelectedMapset && !IsCreatingProject;
    private bool CanCancelProjectCreation() => IsProjectNameEditorVisible && !IsCreatingProject;
    private bool CanCreateProject() => CanShowProjectCreation() && IsProjectNameEditorVisible && !IsLoadingProjectNames
        && existingProjectNames is not null && !HasErrors;

    [RelayCommand(CanExecute = nameof(CanShowProjectCreation), AllowConcurrentExecutions = true)]
    private async Task ShowProjectCreationAsync(CancellationToken cancellationToken)
    {
        var operation = ShowProjectCreationCoreAsync(cancellationToken);
        projectOperations.Add(operation);
        try { await operation; }
        finally { projectOperations.Remove(operation); }
    }

    private async Task ShowProjectCreationCoreAsync(CancellationToken cancellationToken)
    {
        if (!CanShowProjectCreation() || IsProjectNameEditorVisible) return;
        IsProjectNameEditorVisible = true;
        var generation = ++projectDraftGeneration;
        IsLoadingProjectNames = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        try
        {
            var names = await projectFiles.GetExistingNamesAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (active && generation == projectDraftGeneration)
            {
                existingProjectNames = new(names, StringComparer.OrdinalIgnoreCase);
                RefreshProjectNameValidation();
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (active && generation == projectDraftGeneration) messenger.Send(new ProjectCreationFailedMessage($"Unable to create project: {error.Message}"));
        }
        finally { if (generation == projectDraftGeneration) IsLoadingProjectNames = false; }
    }

    [RelayCommand(CanExecute = nameof(CanCancelProjectCreation))]
    private void CancelProjectCreation() => ResetProjectDraft();

    [RelayCommand(CanExecute = nameof(CanCreateProject))]
    private async Task CreateProjectAsync(CancellationToken cancellationToken)
    {
        if (!CanCreateProject()) return;
        var selected = SelectedMapset!;
        var name = DraftProjectName.Trim();
        var generation = projectDraftGeneration;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        IsCreatingProject = true;
        try
        {
            var names = await projectFiles.GetExistingNamesAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (!active || generation != projectDraftGeneration) return;
            existingProjectNames = new(names, StringComparer.OrdinalIgnoreCase);
            RefreshProjectNameValidation();
            if (HasErrors) return;
            var path = await projectFiles.CreateAsync(selected.Directory, name, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (active && generation == projectDraftGeneration) messenger.Send(new ProjectCreatedMessage(path));
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (active && generation == projectDraftGeneration) messenger.Send(new ProjectCreationFailedMessage($"Unable to create project: {error.Message}"));
        }
        finally { IsCreatingProject = false; }
    }
}
