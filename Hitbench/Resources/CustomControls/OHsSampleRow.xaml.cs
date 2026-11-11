using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace Hitbench.Resources.CustomControls;

public partial class OHsSampleRow : UserControl
{
    public static readonly DependencyProperty WaveformMarginProperty = DependencyProperty.Register(nameof(WaveformMargin), typeof(Thickness), typeof(OHsSampleRow), new PropertyMetadata(new Thickness(8, 4, 8, 4)));
    public Thickness WaveformMargin { get => (Thickness)GetValue(WaveformMarginProperty); set => SetValue(WaveformMarginProperty, value); }
    public static readonly DependencyProperty ShowPlayButtonProperty = DependencyProperty.Register(nameof(ShowPlayButton), typeof(bool), typeof(OHsSampleRow), new PropertyMetadata(true));
    public bool ShowPlayButton { get => (bool)GetValue(ShowPlayButtonProperty); set => SetValue(ShowPlayButtonProperty, value); }
    public static readonly DependencyProperty NameTrailingTemplateProperty = DependencyProperty.Register(nameof(NameTrailingTemplate), typeof(DataTemplate), typeof(OHsSampleRow), new PropertyMetadata(null));
    public DataTemplate? NameTrailingTemplate { get => (DataTemplate?)GetValue(NameTrailingTemplateProperty); set => SetValue(NameTrailingTemplateProperty, value); }
    public static readonly DependencyProperty IsDragSourceProperty = DependencyProperty.Register(nameof(IsDragSource), typeof(bool), typeof(OHsSampleRow), new PropertyMetadata(false));
    public bool IsDragSource { get => (bool)GetValue(IsDragSourceProperty); set => SetValue(IsDragSourceProperty, value); }
    public static readonly DependencyProperty FileNameProperty = DependencyProperty.Register(nameof(FileName), typeof(string), typeof(OHsSampleRow), new PropertyMetadata(string.Empty));
    public string FileName { get => (string)GetValue(FileNameProperty); set => SetValue(FileNameProperty, value); }
    public static readonly DependencyProperty FileToolTipProperty = DependencyProperty.Register(nameof(FileToolTip), typeof(object), typeof(OHsSampleRow), new PropertyMetadata(null));
    public object? FileToolTip { get => (object?)GetValue(FileToolTipProperty); set => SetValue(FileToolTipProperty, value); }
    public static readonly DependencyProperty WaveformProperty = DependencyProperty.Register(nameof(Waveform), typeof(Hitbench.Core.Entities.WaveformData), typeof(OHsSampleRow), new PropertyMetadata(null));
    public Hitbench.Core.Entities.WaveformData? Waveform { get => (Hitbench.Core.Entities.WaveformData?)GetValue(WaveformProperty); set => SetValue(WaveformProperty, value); }
    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(nameof(IsPlaying), typeof(bool), typeof(OHsSampleRow), new PropertyMetadata(false));
    public bool IsPlaying { get => (bool)GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }
    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(nameof(IsEditing), typeof(bool), typeof(OHsSampleRow), new PropertyMetadata(false));
    public bool IsEditing { get => (bool)GetValue(IsEditingProperty); set => SetValue(IsEditingProperty, value); }
    public static readonly DependencyProperty PlayCommandProperty = DependencyProperty.Register(nameof(PlayCommand), typeof(ICommand), typeof(OHsSampleRow), new PropertyMetadata(null));
    public ICommand? PlayCommand { get => (ICommand?)GetValue(PlayCommandProperty); set => SetValue(PlayCommandProperty, value); }
    public static readonly DependencyProperty PlayCommandParameterProperty = DependencyProperty.Register(nameof(PlayCommandParameter), typeof(object), typeof(OHsSampleRow), new PropertyMetadata(null));
    public object? PlayCommandParameter { get => (object?)GetValue(PlayCommandParameterProperty); set => SetValue(PlayCommandParameterProperty, value); }
    public static readonly DependencyProperty NameEditorTemplateProperty = DependencyProperty.Register(nameof(NameEditorTemplate), typeof(DataTemplate), typeof(OHsSampleRow), new PropertyMetadata(null));
    public DataTemplate? NameEditorTemplate { get => (DataTemplate?)GetValue(NameEditorTemplateProperty); set => SetValue(NameEditorTemplateProperty, value); }
    public static readonly DependencyProperty ActionsTemplateProperty = DependencyProperty.Register(nameof(ActionsTemplate), typeof(DataTemplate), typeof(OHsSampleRow), new PropertyMetadata(null));
    public DataTemplate? ActionsTemplate { get => (DataTemplate?)GetValue(ActionsTemplateProperty); set => SetValue(ActionsTemplateProperty, value); }
    public event EventHandler? WaveformRequested;
    public OHsSampleRow() { InitializeComponent(); Loaded += (_, _) => WaveformRequested?.Invoke(this, EventArgs.Empty); DataContextChanged += (_, _) => { if (IsLoaded) WaveformRequested?.Invoke(this, EventArgs.Empty); }; }
    public bool FocusPreview() => ShowPlayButton && PlayAction.Focus();
}
