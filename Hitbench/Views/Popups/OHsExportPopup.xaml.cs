using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.Helpers;
using Hitbench.ViewModels.Popups;
namespace Hitbench.Views.Popups;

public partial class OHsExportPopup : BasePopup
{
    private readonly OHsExportPopupVM viewModel;
    private readonly Action<bool> completeDialog;
    private readonly DependencyPropertyDescriptor inputError;
    private bool completing;
    public OHsExportPopup(OHsExportPopupVM viewModel) : this(viewModel, null) { }
    internal OHsExportPopup(OHsExportPopupVM viewModel, Action<bool>? completeDialog)
    {
        InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel;
        this.completeDialog = completeDialog ?? (accepted => DialogResult = accepted);
        var work = SystemParameters.WorkArea;
        MaxWidth = Math.Max(1, work.Width - 48); MaxHeight = Math.Max(1, work.Height - 48); MinWidth = Math.Min(540, MaxWidth);
        inputError = DependencyPropertyDescriptor.FromProperty(NumericUpDown.HasInputErrorProperty, typeof(NumericUpDown));
        inputError.AddValueChanged(StartingIndexInput, OnIndexErrorChanged);
        viewModel.Completed += OnCompleted;
        StartingIndexInput.Loaded += OnIndexLoaded;
        DiffNameInput.Loaded += OnNameLoaded;
        Closed += (_, _) => Detach();
    }
    protected override bool ShouldBubbleApplicationClose => false;
    private void OnIndexLoaded(object sender, RoutedEventArgs e)
    {
        StartingIndexInput.ApplyTemplate();
        if (StartingIndexInput.Template.FindName("PART_TextBox", StartingIndexInput) is TextBox editor)
        { KeyboardNavigationPolicy.SetAllowTabNavigation(editor, true); KeyboardNavigation.SetIsTabStop(editor, true); }
    }
    private void OnNameLoaded(object sender, RoutedEventArgs e) { DiffNameInput.Focus(); DiffNameInput.SelectAll(); }
    private void OnIndexErrorChanged(object? sender, EventArgs e) => viewModel.SetIndexInputError(StartingIndexInput.HasInputError);
    private void OnCompleted(bool accepted) { completing = true; completeDialog(accepted); }
    protected override void OnCloseButtonClick() => viewModel.RequestClose();
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!completing) { e.Cancel = true; Dispatcher.BeginInvoke(new Action(viewModel.RequestClose)); }
        base.OnClosing(e);
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && (SingleFormatInput.IsDropDownOpen || MixedFormatInput.IsDropDownOpen)) return;
        base.OnPreviewKeyDown(e);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && viewModel.ExportCommand.CanExecute(null))
        { e.Handled = true; viewModel.ExportCommand.Execute(null); }
    }
    internal void Detach()
    {
        inputError.RemoveValueChanged(StartingIndexInput, OnIndexErrorChanged);
        viewModel.Completed -= OnCompleted; StartingIndexInput.Loaded -= OnIndexLoaded; DiffNameInput.Loaded -= OnNameLoaded;
    }
}
