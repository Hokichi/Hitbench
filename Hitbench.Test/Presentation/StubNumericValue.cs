using CommunityToolkit.Mvvm.ComponentModel;
namespace Hitbench.Test.Presentation;
public sealed partial class StubNumericValue : ObservableObject
{
    [ObservableProperty] private decimal volume = 50;
}
