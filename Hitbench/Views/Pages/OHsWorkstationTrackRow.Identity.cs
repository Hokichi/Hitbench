using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hitbench.Core.Entities;

namespace Hitbench.Views.Pages;

public partial class OHsWorkstationTrackRow
{
    internal bool BeginNameEdit()
    {
        if (Track is not { } track || ViewModel?.BeginTrackRenameCommand.CanExecute(track) != true) return false;
        ViewModel.BeginTrackRenameCommand.Execute(track);
        return IsRenaming;
    }

    private void FocusTrackNameEditor()
    {
        if (FindVisualChild<TextBox>(this, "TrackNameInput") is not { } input) return;
        input.Focus(); input.SelectAll();
    }

    private void OnTrackNameMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && BeginNameEdit()) e.Handled = true;
    }

    internal async Task<bool> HandleTrackNameKeyAsync(Key key)
    {
        if (key is not (Key.Enter or Key.Escape) || Track is not { } track || ViewModel is not { } owner ||
            !ReferenceEquals(owner.RenamingTrack, track)) return false;
        if (key == Key.Escape) owner.CancelTrackRename(track);
        else await owner.CommitTrackRenameAsync(track);
        if (ReferenceEquals(Track, track) && ReferenceEquals(ViewModel, owner)) GetRemovalFocusTarget()?.Focus();
        return true;
    }

    private async void OnTrackNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        e.Handled = true;
        try { await HandleTrackNameKeyAsync(e.Key); }
        catch (Exception error) { if (ViewModel is { } owner) owner.ErrorMessage = error.Message; }
    }

    private async void OnTrackNameLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox { DataContext: Track track } || !ReferenceEquals(Track, track) || ViewModel is not { } owner) return;
        try { await owner.CommitTrackRenameAsync(track); }
        catch (Exception error) { owner.ErrorMessage = error.Message; }
    }
}
