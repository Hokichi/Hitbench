using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace osuHitsounder.Views.Popups;

public partial class SampleLoaderPopup
{
    private readonly Dictionary<object, Brush> designBrushes = new();

    private void InitializePalette()
    {
        foreach (var key in Resources.Keys.Cast<object>().ToArray())
            if (Resources[key] is Brush brush) designBrushes[key] = brush;
        ApplyPalette();
        SystemParameters.StaticPropertyChanged += OnSystemPaletteChanged;
    }

    private void OnSystemPaletteChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            Dispatcher.InvokeAsync(() => { if (!deactivated) ApplyPalette(); });
    }

    private void ApplyPalette()
    {
        foreach (var (key, original) in designBrushes)
        {
            if (!SystemParameters.HighContrast) { Resources[key] = original; continue; }
            var name = key.ToString()!;
            var surface = name.Contains("Background") || name.EndsWith("Popup") || name.EndsWith("SelectedColumn") ||
                name.EndsWith("List") || name.EndsWith("Inset") || name.EndsWith("Input") || name.EndsWith("Fill") ||
                name.EndsWith("Brush.Navy.Tint");
            Resources[key] = surface ? SystemColors.WindowBrush : SystemColors.WindowTextBrush;
        }
    }
}
