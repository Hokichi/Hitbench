using System.Windows;

namespace osuHitsounder.Views.Pages;

public partial class WorkstationPage
{
    private static readonly DependencyPropertyKey IsHeaderDimmedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsHeaderDimmed), typeof(bool), typeof(WorkstationPage), new PropertyMetadata(false));

    public static readonly DependencyProperty IsHeaderDimmedProperty = IsHeaderDimmedPropertyKey.DependencyProperty;

    public bool IsHeaderDimmed => (bool)GetValue(IsHeaderDimmedProperty);

    private void RefreshHeaderDimming() =>
        SetValue(IsHeaderDimmedPropertyKey, !deactivationStarted && viewModel.ShowStatusOverlay);
}
