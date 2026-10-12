using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services.Ui;
using osuHitsounder.ViewModels.Popups;
namespace osuHitsounder.Views.Popups;

public partial class OHsInstrumentalArrangementPopup : BasePopup
{
    private readonly InstrumentalArrangementPopupVM viewModel;
    private bool closed, rendering;
    protected override bool ShouldBubbleApplicationClose => false;
    public OHsInstrumentalArrangementPopup(InstrumentalArrangementPopupVM vm)
    {
        viewModel = vm; InitializeComponent(); DataContext = vm; ResizeMode = ResizeMode.CanResize;
        Loaded += OnLoaded; Closed += OnClosed; viewModel.PropertyChanged += OnStateChanged;
    }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var available = Owner is { } owner ? WindowWorkArea.GetClientSize(owner) : Size.Empty;
        if (available.Width > 0 && available.Height > 0) { MaxWidth = available.Width; MaxHeight = available.Height; Width = Math.Min(Width, available.Width); Height = Math.Min(Height, available.Height); MinWidth = Math.Min(600, available.Width); MinHeight = Math.Min(460, available.Height); }
        PianoRoll.VerticalOffset = 66 * PianoRoll.LaneHeight; PianoRoll.Focus(); RevealPosition(); UpdateRendering();
    }
    private void OnClosed(object? sender, EventArgs e)
    { closed = true; PianoRoll.CancelInteraction(); viewModel.PropertyChanged -= OnStateChanged; UpdateRendering(); }
    private void OnStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(viewModel.IsPlaying)) UpdateRendering(); }
    private void UpdateRendering()
    { var active = !closed && IsLoaded && viewModel.IsPlaying; if (active == rendering) return; rendering = active; if (active) CompositionTarget.Rendering += OnRendering; else CompositionTarget.Rendering -= OnRendering; }
    private void OnRendering(object? sender, EventArgs e) { viewModel.RefreshPlayback(); RevealPosition(); UpdateRendering(); }
    private void RevealPosition()
    {
        if (SongWaveform.ActualWidth <= 0 || viewModel.Waveform.Duration <= TimeSpan.Zero) return;
        var x = viewModel.Position.TotalMilliseconds / viewModel.Waveform.Duration.TotalMilliseconds * SongWaveform.ActualWidth;
        if (x < WaveformScroll.HorizontalOffset || x > WaveformScroll.HorizontalOffset + WaveformScroll.ViewportWidth) WaveformScroll.ScrollToHorizontalOffset(Math.Max(0, x - WaveformScroll.ViewportWidth / 2));
    }
    private void OnWaveformSizeChanged(object sender, SizeChangedEventArgs e) => SongWaveform.SetCurrentValue(WaveformView.ViewportWidthProperty, Math.Max(0, e.NewSize.Width));
    private void OnWaveformScrolled(object sender, ScrollChangedEventArgs e)
    { if (!ReferenceEquals(e.OriginalSource, WaveformScroll)) return; TimelineScroll.SetCurrentValue(ScrollBar.ValueProperty, WaveformScroll.HorizontalOffset); }
    private void OnHorizontalChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (WaveformScroll is not null) WaveformScroll.ScrollToHorizontalOffset(e.NewValue); }
    private void OnWaveformClick(object sender, MouseButtonEventArgs e) { if (SongWaveform.SeekAt(e.GetPosition(SongWaveform).X)) e.Handled = true; }
    private void OnNoteEdit(object sender, InstrumentalNoteEditRequestedEventArgs e)
    { switch (e.Kind) { case InstrumentalNoteEditKind.Place: viewModel.PlaceNote(e.Key, e.RequestedMilliseconds); break; case InstrumentalNoteEditKind.Move: viewModel.MoveNote(e.NoteId, e.Key, e.RequestedMilliseconds); break; case InstrumentalNoteEditKind.Remove: viewModel.RemoveNote(e.NoteId); break; } e.Handled = true; }
    internal bool HandlePlaybackKey(Key key, ModifierKeys modifiers, bool repeat, DependencyObject? origin)
    {
        for (var node = origin; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : null)
            if (node is TextBoxBase or PasswordBox || node is ComboBox { IsEditable: true }) return false;
        if (modifiers == ModifierKeys.Control && key is Key.Left or Key.Right)
        { var navigation = key == Key.Right ? viewModel.NextGridCommand : viewModel.PreviousGridCommand; if (!repeat && navigation.CanExecute(null)) navigation.Execute(null); return true; }
        if (modifiers == ModifierKeys.Control && key is Key.Home or Key.End)
        { if (!repeat && viewModel.SeekCommand.CanExecute(null)) viewModel.SeekCommand.Execute(key == Key.Home ? TimeSpan.Zero : viewModel.Waveform.Duration); return true; }
        if (key != Key.Space || modifiers is not (ModifierKeys.None or ModifierKeys.Shift)) return false;
        if (repeat) return true;
        var command = modifiers == ModifierKeys.Shift ? viewModel.StopCommand : viewModel.PlayPauseCommand;
        if (!command.CanExecute(null)) return false; command.Execute(null); return true;
    }
    internal bool HandleEscape() { if (PianoRoll.CancelInteraction()) return true; if (viewModel.SelectedNoteId is not null) { viewModel.SelectNote(null); return true; } return false; }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (HandlePlaybackKey(e.Key, Keyboard.Modifiers, e.IsRepeat, e.OriginalSource as DependencyObject)) { e.Handled = true; return; }
        if (e.Key == Key.Escape && HandleEscape()) { e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    { if (Keyboard.Modifiers == ModifierKeys.Control) { viewModel.ZoomFactor *= Math.Pow(1.25, e.Delta / 120d); e.Handled = true; return; } base.OnPreviewMouseWheel(e); }
}
