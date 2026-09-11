using System.Windows.Controls;
using osuHitsounder.ViewModels.Shell.HomePage;

namespace osuHitsounder.Views.Pages;

public partial class HomePage : UserControl
{
    private readonly HomePageVM viewModel;

    public HomePage(HomePageVM viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    public string? FatalError => viewModel.FatalError;
    public Task LoadAsync(CancellationToken cancellationToken) => viewModel.LoadAsync(cancellationToken);
}
