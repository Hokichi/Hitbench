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
    private ModifierKeys wheelMode;
    private int wheelRemainder;
    private double previousLaneHeight=36;
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
    private void OnStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(viewModel.IsPlaying)) UpdateRendering(); if(e.PropertyName==nameof(viewModel.LaneHeight)){ApplyLaneHeightChange(previousLaneHeight,viewModel.LaneHeight);previousLaneHeight=viewModel.LaneHeight;} if(e.PropertyName==nameof(viewModel.Position) && !viewModel.IsPlaying || e.PropertyName==nameof(viewModel.ScrollWithMarker) && viewModel.ScrollWithMarker)RevealPosition(); }
    private void UpdateRendering()
    { var active = !closed && IsLoaded && viewModel.IsPlaying; if (active == rendering) return; rendering = active; if (active) CompositionTarget.Rendering += OnRendering; else CompositionTarget.Rendering -= OnRendering; }
    private void OnRendering(object? sender, EventArgs e) { viewModel.RefreshPlayback(); if(viewModel.ScrollWithMarker)RevealPosition(); UpdateRendering(); }
    private void RevealPosition()
    {
        if (SongWaveform.ActualWidth <= 0 || viewModel.Waveform.Duration <= TimeSpan.Zero) return;
        var x = viewModel.Position.TotalMilliseconds / viewModel.Waveform.Duration.TotalMilliseconds * SongWaveform.ActualWidth;
        if (x < WaveformScroll.HorizontalOffset || x > WaveformScroll.HorizontalOffset + WaveformScroll.ViewportWidth)WaveformScroll.ScrollToHorizontalOffset(Math.Max(0, x - WaveformScroll.ViewportWidth / 2));
    }
    private void OnWaveformSizeChanged(object sender, SizeChangedEventArgs e) => SongWaveform.SetCurrentValue(WaveformView.ViewportWidthProperty, Math.Max(0, e.NewSize.Width));
    private void OnWaveformScrolled(object sender, ScrollChangedEventArgs e)
    { if (!ReferenceEquals(e.OriginalSource, WaveformScroll)) return; TimelineScroll.SetCurrentValue(ScrollBar.ValueProperty, WaveformScroll.HorizontalOffset); }
    private void OnHorizontalChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (WaveformScroll is not null) WaveformScroll.ScrollToHorizontalOffset(e.NewValue); }
    private void OnWaveformClick(object sender, MouseButtonEventArgs e) { SongWaveform.Focus(); if (SongWaveform.SeekAt(e.GetPosition(SongWaveform).X)) e.Handled = true; }
    private void OnTimelineManualInput(object sender,RoutedEventArgs e)=>viewModel.ScrollWithMarker=false;
    private void OnNoteEdit(object sender, InstrumentalNoteEditRequestedEventArgs e)
    { switch (e.Kind) { case InstrumentalNoteEditKind.Place: viewModel.PlaceNote(e.Key, e.RequestedMilliseconds); break; case InstrumentalNoteEditKind.Move: viewModel.MoveNotes(e.NoteIds,e.NoteId, e.Key, e.RequestedMilliseconds); break; case InstrumentalNoteEditKind.Remove: viewModel.RemoveNotes(e.NoteIds); break; } e.Handled = true; }
    private void OnNoteSelection(object sender,InstrumentalNoteSelectionRequestedEventArgs e){viewModel.SetNoteSelection(e.NoteIds,e.AnchorId);e.Handled=true;}
    private void OnEditingModeChecked(object sender,RoutedEventArgs e){if(sender is RadioButton radio)viewModel.IsAdditionMode=radio.Name=="AdditionModeButton";}
    private static bool IsTextInput(DependencyObject? origin)
    {for(var node=origin;node is not null;node=node is Visual?VisualTreeHelper.GetParent(node):null)if(node is TextBoxBase or PasswordBox || node is ComboBox{IsEditable:true})return true;return false;}
    internal bool HandleEditorShortcut(Key key,ModifierKeys modifiers,bool repeat,DependencyObject? origin)
    {
        if(IsTextInput(origin))return false;
        if(modifiers==ModifierKeys.None && key is Key.Left or Key.Right && (ReferenceEquals(origin,SongWaveform)||ReferenceEquals(origin,WaveformScroll))){var seek=key==Key.Right?viewModel.NextGridCommand:viewModel.PreviousGridCommand;if(!repeat && seek.CanExecute(null))seek.Execute(null);return true;}
        ICommand? command=modifiers==ModifierKeys.Control?key switch{Key.Z=>viewModel.UndoCommand,Key.Y=>viewModel.RedoCommand,Key.C=>viewModel.CopyNotesCommand,Key.V=>viewModel.PasteNotesCommand,Key.A=>viewModel.SelectAllNotesCommand,_=>null}:modifiers==ModifierKeys.None && key==Key.Delete?viewModel.DeleteSelectedNotesCommand:null;
        if(command is not null){if(!repeat && command.CanExecute(null)){PianoRoll.CancelInteraction();command.Execute(null);}return true;}
        if(modifiers==ModifierKeys.Control){var index=key switch{Key.D1 or Key.NumPad1=>0,Key.D2 or Key.NumPad2=>1,Key.D3 or Key.NumPad3=>2,Key.D4 or Key.NumPad4=>3,Key.D5 or Key.NumPad5=>4,Key.D6 or Key.NumPad6=>5,Key.D8 or Key.NumPad8=>6,_=>-1};if(index>=0){if(!repeat)viewModel.SelectedSubdivisionIndex=index;return true;}}
        if(modifiers==ModifierKeys.None && key is Key.Home or Key.End){viewModel.ScrollWithMarker=false;if(key==Key.Home)WaveformScroll.ScrollToLeftEnd();else WaveformScroll.ScrollToRightEnd();return true;}
        return false;
    }
    internal void ApplyLaneHeightChange(double previousHeight,double nextHeight){var offset=PianoRoll.VerticalOffset/previousHeight*nextHeight;PianoRoll.SetCurrentValue(PianoRollView.LaneHeightProperty,nextHeight);PianoRoll.SetCurrentValue(PianoRollView.VerticalOffsetProperty,Math.Clamp(offset,0,PianoRoll.MaximumVerticalOffset));}
    internal bool HandleArrangementWheel(ModifierKeys modifiers,int delta,DependencyObject? origin)
    {
        var within=false;for(var node=origin;node is not null;node=node is Visual?VisualTreeHelper.GetParent(node):null)if(ReferenceEquals(node,PianoRoll)||ReferenceEquals(node,SongWaveform)||ReferenceEquals(node,WaveformScroll)){within=true;break;}
        if(!within || !viewModel.CanEdit || modifiers is not (ModifierKeys.Control or ModifierKeys.Shift or ModifierKeys.Alt or (ModifierKeys.Control|ModifierKeys.Shift))){wheelMode=ModifierKeys.None;wheelRemainder=0;return false;}
        if(wheelMode!=modifiers){wheelMode=modifiers;wheelRemainder=0;}wheelRemainder+=delta;var steps=wheelRemainder/120;wheelRemainder%=120;
        if(modifiers==ModifierKeys.Shift){if(delta!=0)viewModel.ScrollWithMarker=false;for(var i=0;i<Math.Abs(steps);i++)if(steps>0)WaveformScroll.LineLeft();else WaveformScroll.LineRight();}
        else if(modifiers==ModifierKeys.Control)viewModel.ZoomFactor+=steps;
        else if(modifiers==ModifierKeys.Alt)viewModel.SelectedSubdivisionIndex+=steps;
        else viewModel.LaneHeight+=steps*6;
        return true;
    }
    internal bool HandlePlaybackKey(Key key, ModifierKeys modifiers, bool repeat, DependencyObject? origin)
    {
        for (var node = origin; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : null)
            if (node is TextBoxBase or PasswordBox || node is ComboBox { IsEditable: true }) return false;
        if (modifiers == ModifierKeys.Control && key is Key.Left or Key.Right)
        { var navigation = key == Key.Right ? viewModel.NextWholeBeatCommand : viewModel.PreviousWholeBeatCommand; if (!repeat && navigation.CanExecute(null)) navigation.Execute(null); return true; }
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
        if(HandleEditorShortcut(e.Key,Keyboard.Modifiers,e.IsRepeat,e.OriginalSource as DependencyObject)){e.Handled=true;return;}
        if (HandlePlaybackKey(e.Key, Keyboard.Modifiers, e.IsRepeat, e.OriginalSource as DependencyObject)) { e.Handled = true; return; }
        if (e.Key == Key.Escape && HandleEscape()) { e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    { if (HandleArrangementWheel(Keyboard.Modifiers,e.Delta,e.OriginalSource as DependencyObject)) { e.Handled = true; return; } base.OnPreviewMouseWheel(e); }
}
