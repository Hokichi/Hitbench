using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Onset.Resources.CustomControls;
using Onset.Resources.DataModels;
using Onset.Resources.Messages;

namespace Onset.Views.Popups;

public partial class OHsControlPointEditor : UserControl
{
    public static readonly DependencyProperty PointProperty = DependencyProperty.Register(nameof(Point), typeof(ControlPointSnapshot), typeof(OHsControlPointEditor), new PropertyMetadata(null, (d, e) => ((OHsControlPointEditor)d).ApplyPoint(e.OldValue as ControlPointSnapshot)));
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(nameof(Context), typeof(ControlPointsContext), typeof(OHsControlPointEditor), new PropertyMetadata(null, (d, _) => ((OHsControlPointEditor)d).ApplyPoint(null)));
    public static readonly DependencyProperty SetPointCommandProperty = DependencyProperty.Register(nameof(SetPointCommand), typeof(ICommand), typeof(OHsControlPointEditor));
    private static readonly DependencyPropertyKey AcceptedVolumeTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(AcceptedVolumeText), typeof(string), typeof(OHsControlPointEditor), new PropertyMetadata(""));
    public static readonly DependencyProperty AcceptedVolumeTextProperty = AcceptedVolumeTextPropertyKey.DependencyProperty;
    public ControlPointSnapshot? Point { get => (ControlPointSnapshot?)GetValue(PointProperty); set => SetValue(PointProperty, value); }
    public ControlPointsContext? Context { get => (ControlPointsContext?)GetValue(ContextProperty); set => SetValue(ContextProperty, value); }
    public ICommand? SetPointCommand { get => (ICommand?)GetValue(SetPointCommandProperty); set => SetValue(SetPointCommandProperty, value); }
    public string AcceptedVolumeText => (string)GetValue(AcceptedVolumeTextProperty);
    internal NumericUpDown VolumeControl => VolumeInput;
    internal ComboBox SamplesetControl => SamplesetInput;
    private bool applying;
    private bool volumeEdited;
    private TextBox? textEditor;

    public OHsControlPointEditor()
    {
        InitializeComponent();
        VolumeInput.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnVolumeStep), true);
        VolumeInput.AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(OnVolumeKey), true);
        VolumeInput.AddHandler(Mouse.MouseWheelEvent, new MouseWheelEventHandler(OnVolumeWheel), true);
        Loaded += (_, _) => AttachTextEditor();
        Unloaded += (_, _) => DetachTextEditor();
    }

    private void ApplyPoint(ControlPointSnapshot? previous)
    {
        if (VolumeInput is null) return;
        applying = true;
        try
        {
            if (Point is not { } point) { volumeEdited = false; return; }
            SetValue(AcceptedVolumeTextPropertyKey, string.Format(CultureInfo.CurrentCulture,
                "Accepted volume: {0}%. Edit volume to change it (5–100%).", point.Volume));
            if (previous?.Id != point.Id || !volumeEdited && !VolumeInput.HasInputError)
            {
                volumeEdited = false;
                VolumeInput.DataContext = point;
                VolumeInput.SetCurrentValue(NumericUpDown.ValueProperty, (decimal)point.Volume);
                VolumeInput.ApplyTemplate();
                AttachTextEditor();
            }
            SamplesetInput.SelectedItem = point.Sampleset;
        }
        finally { applying = false; }
    }

    private void AttachTextEditor()
    {
        VolumeInput.ApplyTemplate();
        var next = VolumeInput.Template?.FindName("PART_TextBox", VolumeInput) as TextBox;
        if (next == textEditor) return;
        DetachTextEditor(); textEditor = next;
        if (textEditor is null) return;
        textEditor.TextChanged += OnVolumeTextChanged;
        textEditor.LostKeyboardFocus += OnVolumeLostFocus;
        textEditor.PreviewTextInput += OnExplicitTextInput;
        DataObject.AddPastingHandler(textEditor, OnExplicitPaste);
    }
    private void DetachTextEditor()
    {
        if (textEditor is null) return;
        textEditor.TextChanged -= OnVolumeTextChanged;
        textEditor.LostKeyboardFocus -= OnVolumeLostFocus;
        textEditor.PreviewTextInput -= OnExplicitTextInput;
        DataObject.RemovePastingHandler(textEditor, OnExplicitPaste);
        textEditor = null;
    }
    private void OnVolumeTextChanged(object sender, TextChangedEventArgs e) { if (!applying) volumeEdited = true; }
    private void OnExplicitTextInput(object sender, TextCompositionEventArgs e) { if (!applying && !e.Handled) volumeEdited = true; }
    private void OnExplicitPaste(object sender, DataObjectPastingEventArgs e) { if (!applying && !e.CommandCancelled) volumeEdited = true; }
    private void OnVolumeLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitVolumeInput(volumeEdited);
    private void OnVolumeStep(object sender, RoutedEventArgs e) => CommitVolumeInput(true);
    private void OnVolumeKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down) CommitVolumeInput(true);
        else if (e.Key == Key.Enter) CommitVolumeInput(volumeEdited);
    }
    private void OnVolumeWheel(object sender, MouseWheelEventArgs e)
    {
        if (VolumeInput.IsKeyboardFocusWithin) CommitVolumeInput(true);
    }
    internal bool CommitVolumeInput(bool explicitlyEdited)
    {
        if (applying || !explicitlyEdited || Point is not { } point || Context is not { } context) return true;
        applying = true;
        bool valid;
        try { valid = VolumeInput.CommitInput(); }
        finally { applying = false; }
        if (!valid) { volumeEdited = true; return false; }
        volumeEdited = false;
        var request = new SetControlPointRequestedMessage(context, point.Id, VolumeInput.Value, null);
        if (SetPointCommand?.CanExecute(request) == true) SetPointCommand.Execute(request);
        return true;
    }
    internal void CommitPendingInput() => CommitVolumeInput(volumeEdited);

    private void OnSamplesetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (applying || Point is not { } point || Context is not { } context || SamplesetInput.SelectedItem is not string sampleset) return;
        var request = new SetControlPointRequestedMessage(context, point.Id, null, sampleset);
        if (SetPointCommand?.CanExecute(request) == true) SetPointCommand.Execute(request);
    }
    internal bool HandleEscape()
    {
        if (SamplesetInput.IsDropDownOpen) return false;
        return true;
    }
    internal void FocusInitialInput() => textEditor?.Focus();
    internal void Clear()
    {
        applying = true;
        try { Point = null; Context = null; SamplesetInput.IsDropDownOpen = false; VolumeInput.DataContext = null; volumeEdited = false; DetachTextEditor(); }
        finally { applying = false; }
    }
}
