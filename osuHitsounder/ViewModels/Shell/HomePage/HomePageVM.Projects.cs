using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;

namespace osuHitsounder.ViewModels.Shell.HomePage;

public partial class HomePageVM
{
    public ObservableCollection<OHsHomeProjectListItem> ProjectItems { get; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsProjectSearchActive))] private string projectSearchText = "";
    [ObservableProperty] private FolderEntry? selectedMapset;
    [ObservableProperty] private bool isMutatingProjects;
    private FolderSnapshot? projectsInventory;
    private readonly HashSet<Task> projectOperations = [];
    public bool IsProjectSearchActive => !string.IsNullOrWhiteSpace(ProjectSearchText);

    partial void OnIsMutatingProjectsChanged(bool value) => RefreshCommands();
    partial void OnProjectSearchTextChanged(string value)
    {
        if (active && projectsInventory is not null) SearchProjectsCommand.Execute(null);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchProjectsAsync(CancellationToken cancellationToken)
    {
        if (!active || projectsInventory is null) return;
        var operation = ApplyProjectQueryAsync(cancellationToken);
        projectOperations.Add(operation);
        try { await operation; }
        finally { projectOperations.Remove(operation); }
    }

    private async Task ApplyProjectQueryAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        var generation = ++projectsGeneration;
        var inventory = projectsInventory!;
        var query = ProjectSearchText;
        SetBusy(true, true);
        try
        {
            var snapshot = await Task.Run(() => inventory.FilterProjects(query, linked.Token), linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(true, generation)) return;
            projectsSnapshot = snapshot;
            Projects.Clear();
            await PublishPageAsync(true, snapshot, generation, 64, linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception error) { if (IsCurrent(true, generation)) FailPanel(true, error); }
        finally { if (IsCurrent(true, generation)) SetBusy(true, false); }
    }

    private void RebuildProjectItems()
    {
        ProjectItems.Clear();
        if (IsProjectSearchActive)
        {
            foreach (var project in Projects) ProjectItems.Add(OHsHomeProjectListItem.ForProject(project));
            return;
        }
        ProjectItems.Add(OHsHomeProjectListItem.ForHeading("Pinned"));
        foreach (var project in Projects.Where(entry => entry.IsPinned))
            ProjectItems.Add(OHsHomeProjectListItem.ForProject(project));
        ProjectItems.Add(OHsHomeProjectListItem.ForHeading("History"));
        foreach (var project in Projects.Where(entry => !entry.IsPinned))
            ProjectItems.Add(OHsHomeProjectListItem.ForProject(project));
    }

    private FolderEntry? FindProject(FolderEntry? entry) => entry is null ? null : projectsInventory?
        .ReadPage(0, projectsInventory.Count).FirstOrDefault(project =>
            string.Equals(project.FullName, entry.FullName, StringComparison.OrdinalIgnoreCase));
    private bool CanToggleProjectPin(FolderEntry? entry) => CanInteract && !IsCreatingProject && FindProject(entry)?.HasProjectMetadata == true;
    private bool CanDeleteProject(FolderEntry? entry) => CanInteract && !IsCreatingProject && removalDialogs is not null && FindProject(entry) is not null;

    [RelayCommand(CanExecute = nameof(CanToggleProjectPin))]
    private Task ToggleProjectPinAsync(FolderEntry? entry, CancellationToken token) =>
        CanToggleProjectPin(entry) ? OwnProjectMutationAsync(entry!, false, token) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanDeleteProject))]
    private Task DeleteProjectAsync(FolderEntry? entry, CancellationToken token) =>
        CanDeleteProject(entry) ? OwnProjectMutationAsync(entry!, true, token) : Task.CompletedTask;

    private async Task OwnProjectMutationAsync(FolderEntry entry, bool delete, CancellationToken token)
    {
        var operation = MutateProjectAsync(entry, delete, token);
        projectOperations.Add(operation);
        try { await operation; }
        finally { projectOperations.Remove(operation); }
    }

    private async Task MutateProjectAsync(FolderEntry entry, bool delete, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
        var current = FindProject(entry)!;
        IsMutatingProjects = true;
        try
        {
            if (delete && !await removalDialogs!.ConfirmAsync(current.Name, linked.Token)) return;
            linked.Token.ThrowIfCancellationRequested();
            var path = Path.Combine(current.FullName, "project.json");
            if (delete) await projectFiles.DeleteAsync(path, linked.Token);
            else await projectFiles.SetPinnedAsync(path, !current.IsPinned, linked.Token);
            if (!active) return;
            var entries = projectsInventory!.ReadPage(0, projectsInventory.Count);
            projectsInventory = FolderSnapshot.ForProjects(entries
                .Where(project => !delete || project.FullName != current.FullName)
                .Select(project => project.FullName == current.FullName ? project with { IsPinned = !current.IsPinned } : project), true);
            await SearchProjectsCommand.ExecuteAsync(null);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (active) messenger.Send(new ProjectCreationFailedMessage($"Unable to {(delete ? "delete" : "pin")} project: {error.Message}"));
        }
        finally { IsMutatingProjects = false; }
    }
}
