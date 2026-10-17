using CommunityToolkit.Mvvm.ComponentModel;
namespace osuHitsounder.Test.Presentation;
public sealed partial class StubNumericValue : ObservableObject
{
    [ObservableProperty] private decimal volume = 50;
}
