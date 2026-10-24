using System.Windows;
using System.Windows.Data;
using Hitbench.Views.Pages;

namespace Hitbench;

public partial class MainWindow
{
    private WorkstationPage? volumePresentationPage;

    private void SetVolumePresentationSource(WorkstationPage? page)
    {
        if (ReferenceEquals(volumePresentationPage, page)) return;
        volumePresentationPage?.DetachVolumeOverlayHost();
        BindingOperations.ClearBinding(VolumeBackdropHost, UIElement.EffectProperty);
        BindingOperations.ClearBinding(VolumeBackdropHost, FrameworkElement.TagProperty);
        volumePresentationPage = page;
        if (page is null) return;
        VolumeBackdropHost.SetBinding(FrameworkElement.TagProperty,
            new Binding(nameof(WorkstationPage.IsVolumeOverlayOpen)) { Source = page, Mode = BindingMode.OneWay });
        VolumeBackdropHost.SetBinding(UIElement.EffectProperty,
            new Binding(nameof(WorkstationPage.VolumeBlurEffect)) { Source = page, Mode = BindingMode.OneWay });
        page.AttachVolumeOverlayHost(ShellVolumeOverlayHost);
    }
}
