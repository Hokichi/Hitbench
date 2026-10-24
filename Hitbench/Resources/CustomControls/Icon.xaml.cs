using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Hitbench.Resources.CustomControls;

public partial class Icon : UserControl
{
    public static readonly DependencyProperty PathProperty = DependencyProperty.Register(
        nameof(Path),
        typeof(Geometry),
        typeof(Icon),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color),
        typeof(Brush),
        typeof(Icon),
        new PropertyMetadata(Brushes.Black));

    public Icon()
    {
        InitializeComponent();
    }

    public Geometry? Path
    {
        get => (Geometry?)GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    public Brush? Color
    {
        get => (Brush?)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }
}


