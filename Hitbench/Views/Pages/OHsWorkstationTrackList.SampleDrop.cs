using System.Windows;
using Hitbench.Resources.DataModels;
namespace Hitbench.Views.Pages;

public partial class OHsWorkstationTrackList
{
    internal bool CanAcceptSampleDrop(IDataObject data) => !deactivationStarted && viewModel is not null && data.GetData(typeof(OHsSampleTrackDragData)) is OHsSampleTrackDragData sample && viewModel.CanAcceptSampleTrackDrop(sample);
    internal bool HandleSampleDrop(IDataObject data) => CanAcceptSampleDrop(data) && viewModel.TryAddSampleTrack((OHsSampleTrackDragData)data.GetData(typeof(OHsSampleTrackDragData))!);
    private void OnSampleDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(OHsSampleTrackDragData))) return;
        e.Handled = true; e.Effects = CanAcceptSampleDrop(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
    }
    private void OnSampleDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(OHsSampleTrackDragData))) return; e.Handled = true; e.Effects = HandleSampleDrop(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
    }
}
