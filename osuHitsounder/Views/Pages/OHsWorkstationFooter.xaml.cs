using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Views.Pages;

public partial class OHsWorkstationFooter : UserControl
{
    public OHsWorkstationFooter() { InitializeComponent(); }
    public static readonly DependencyProperty AudioCardBorderThicknessProperty = DependencyProperty.Register(nameof(AudioCardBorderThickness), typeof(Thickness), typeof(OHsWorkstationFooter));
    public Thickness AudioCardBorderThickness { get => (Thickness)GetValue(AudioCardBorderThicknessProperty); set => SetValue(AudioCardBorderThicknessProperty, value); }
}
