using System.Windows;
using System.Windows.Input;
namespace Hitbench.Views.Pages;
public partial class OHsWorkstationTrackRow
{
    private void OnTrackIdentityPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (Track is { } track && ViewModel is { } owner) owner.SelectedTrack = track;
    }
    internal async Task<bool> EditTrackAppearanceAsync()
    {
        if (IsTrackDragPreview || Track is not { } track || ViewModel is not { } owner || !owner.ChangeTrackColorCommand.CanExecute(track)) return false;
        owner.SelectedTrack = track;
        await owner.ChangeTrackColorCommand.ExecuteAsync(track);
        if (ReferenceEquals(Track, track) && ReferenceEquals(ViewModel, owner))
            FindVisualChild<Resources.CustomControls.OHsTrackIdentityButton>(this)?.Focus();
        return true;
    }
    private async void OnTrackIdentityClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        try { await EditTrackAppearanceAsync(); }
        catch (Exception error) { if (ViewModel is { } owner) owner.ErrorMessage = error.Message; }
    }
}
