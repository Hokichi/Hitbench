using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

public sealed class WorkstationDrawerAllocationTests
{
    [TestCase(1200, 200, 480)]
    [TestCase(600, 200, 200)]
    [TestCase(100, 200, 0)]
    public void SideDrawerUsesOnlyAvailableTrackArea(double available, double fixedSide, double expected) =>
        Assert.That(WorkstationPage.CalculateSideDrawerWidth(available, fixedSide), Is.EqualTo(expected));

    [TestCase(400, 300, false, 200)]
    [TestCase(400, 120, true, 120)]
    [TestCase(100, 120, true, 100)]
    public void BottomDrawerUsesStableTrackBudget(double available, double desired, bool content, double expected) =>
        Assert.That(WorkstationPage.CalculateBottomDrawerHeight(available, desired, content), Is.EqualTo(expected));
}
