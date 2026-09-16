using System.Windows;
using System.Windows.Input;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Views.Popups;

public partial class ProjectNamePopup : BasePopup
{
    private readonly ProjectNamePopupVM viewModel;

    public ProjectNamePopup(ProjectNamePopupVM viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
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
        DialogResult = false;
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
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        OnCloseButtonClick();
    }
}
