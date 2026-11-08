using CommunityToolkit.Mvvm.ComponentModel;
using Hitbench.Resources.DataModels;
using System.ComponentModel;
using System.Globalization;
namespace Hitbench.ViewModels.Popups;
public sealed class OHsMapsetSampleImportPopupVM : ObservableObject
{
    private bool closed;
    public IReadOnlyList<OHsMapsetSampleEntry> Entries { get; }
    public string PromptText => string.Format(CultureInfo.CurrentCulture, "{0} samples found. Would you like to import", Entries.Count);
    public bool CanConfirm => !closed && Entries.Any(e => e.IsChecked);
    public bool? SelectionAccepted { get; private set; }
    public IReadOnlyList<string> SelectedPaths { get; private set; } = [];
    public OHsMapsetSampleImportPopupVM(IReadOnlyList<string> paths)
    {
        Entries = Array.AsReadOnly(paths.Select(p => new OHsMapsetSampleEntry(p)).ToArray());
        foreach (var entry in Entries) entry.PropertyChanged += OnChanged;
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(OHsMapsetSampleEntry.IsChecked)) OnPropertyChanged(nameof(CanConfirm)); }
    public bool TryCompleteSelection(bool accepted)
    {
        if (closed || accepted && !CanConfirm) return false;
        SelectionAccepted = accepted;
        SelectedPaths = accepted ? Array.AsReadOnly(Entries.Where(e => e.IsChecked).Select(e => e.SourcePath).ToArray()) : [];
        Deactivate(); return true;
    }
    public void Deactivate()
    {
        closed = true;
        foreach (var entry in Entries) entry.PropertyChanged -= OnChanged;
        OnPropertyChanged(nameof(CanConfirm));
    }
}
