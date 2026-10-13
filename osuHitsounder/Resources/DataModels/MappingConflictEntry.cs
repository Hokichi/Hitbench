using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace osuHitsounder.Resources.DataModels;

public sealed class MappingConflictEntry
{
    private bool changing;
    public string Key { get; }
    public ReadOnlyObservableCollection<MappingConflictChoice> Choices { get; }
    public IReadOnlyList<MappingConflictChoice> ExistingChoices { get; }
    public IReadOnlyList<MappingConflictChoice> IncomingChoices { get; }
    public string ExistingStatus => ExistingChoices.Count == 0 ? "Unmapped" : "";
    public MappingConflictEntry(MappingConflict conflict)
    {
        Key = conflict.Key;
        var choices = new ObservableCollection<MappingConflictChoice>();
        if (conflict.ExistingPath is { } existing) choices.Add(new(Key, existing, true, conflict.ExistingIsMissing));
        foreach (var incoming in conflict.IncomingPaths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ThenBy(Path.GetFileName, StringComparer.Ordinal))
            choices.Add(new(Key, incoming, false, false));
        Choices = new(choices);
        ExistingChoices = Array.AsReadOnly(choices.Where(c => c.IsExisting).ToArray());
        IncomingChoices = Array.AsReadOnly(choices.Where(c => !c.IsExisting).ToArray());
        var first = IncomingChoices.FirstOrDefault() ?? ExistingChoices.FirstOrDefault();
        if (first is not null) first.IsChecked = true;
        foreach (var choice in choices) choice.PropertyChanged += ChoiceChanged;
    }
    private void ChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (changing || e.PropertyName != nameof(MappingConflictChoice.IsChecked) || sender is not MappingConflictChoice selected) return;
        changing = true;
        try
        {
            if (selected.IsChecked) foreach (var choice in Choices.Where(c => c != selected)) choice.IsChecked = false;
            else if (!Choices.Any(c => c.IsChecked)) selected.IsChecked = true;
        }
        finally { changing = false; }
    }
}
