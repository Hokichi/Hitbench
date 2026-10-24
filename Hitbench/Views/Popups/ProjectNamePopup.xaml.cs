using System.Windows;
using System.Windows.Input;
using Hitbench.Resources.CustomControls;
using Hitbench.Services.ProjectFiles;
using Hitbench.ViewModels.Popups;

namespace Hitbench.Views.Popups;

public partial class ProjectNamePopup : BasePopup
{
    private readonly ProjectNamePopupVM viewModel;
    private readonly Action<bool> completeDialog;

    public ProjectNamePopup(ProjectNamePopupVM viewModel) : this(viewModel, null) { }

    internal ProjectNamePopup(ProjectNamePopupVM viewModel, Action<bool>? completeDialog)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.completeDialog = completeDialog ?? (accepted => DialogResult = accepted);
        DataContext = viewModel;
        DataObject.AddPastingHandler(ProjectNameTextBox, OnPasting);
    }

    public string? Result { get; private set; }

    protected override bool ShouldBubbleApplicationClose => false;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && CreateButton.IsEnabled)
        {
            e.Handled = true;
            OnCreateClick(CreateButton, e);
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnCloseButtonClick()
    {
        Result = null;
        CompleteDialog(false);
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = ProjectNameRules.RemoveInvalidCharacters(e.Text) != e.Text;

    private void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        var pasted = e.DataObject.GetData(DataFormats.UnicodeText) as string
            ?? e.DataObject.GetData(DataFormats.Text) as string
            ?? "";
        var filtered = ProjectNameRules.RemoveInvalidCharacters(pasted);
        var selectionStart = ProjectNameTextBox.SelectionStart;
        var selectionLength = ProjectNameTextBox.SelectionLength;
        ProjectNameTextBox.Text = ProjectNameTextBox.Text.Remove(selectionStart, selectionLength)
            .Insert(selectionStart, filtered);
        ProjectNameTextBox.CaretIndex = selectionStart + filtered.Length;
        e.CancelCommand();
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanCreate) return;
        Result = viewModel.EffectiveName;
        CompleteDialog(true);
    }

    private void CompleteDialog(bool accepted) => completeDialog(accepted);

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        OnCloseButtonClick();
    }
}
