using CommunityToolkit.Mvvm.ComponentModel;
using Soundie.Services.ProjectFiles;

namespace Soundie.ViewModels.Popups;

public partial class ProjectNamePopupVM : ObservableObject
{
    private readonly HashSet<string> existingNames;

    public ProjectNamePopupVM(string suggestedName, IReadOnlySet<string> existingNames)
    {
        SuggestedName = suggestedName;
        this.existingNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveName))]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private string projectName = "";

    public string SuggestedName { get; }

    public string EffectiveName => string.IsNullOrWhiteSpace(ProjectName)
        ? SuggestedName.Trim()
        : ProjectName.Trim();

    public string? ValidationError => ProjectNameRules.GetSyntaxError(
            string.IsNullOrWhiteSpace(ProjectName) ? EffectiveName : ProjectName)
        ?? (existingNames.Contains(EffectiveName) ? "Project already exists." : null);

    public bool CanCreate => ValidationError is null;
}
