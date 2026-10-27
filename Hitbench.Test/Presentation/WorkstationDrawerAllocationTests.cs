using Hitbench.Views.Pages;
using Hitbench.Resources.DataModels;
using System.Windows;

namespace Hitbench.Test.Presentation;

public sealed class WorkstationDrawerAllocationTests
{
    [TestCase(OHsWorkstationDrawerKind.Samples, 1200, 700, 1200, 260, 200, 0, 480, 700)]
    [TestCase(OHsWorkstationDrawerKind.Samples, 1200, 700, 600, 80, 200, 0, 480, 700)]
    [TestCase(OHsWorkstationDrawerKind.Samples, 600, 360, 400, 20, 200, 0, 200, 360)]
    [TestCase(OHsWorkstationDrawerKind.Arrangement, 1200, 700, 800, 240, 200, 900, 800, 120)]
    [TestCase(OHsWorkstationDrawerKind.KeysMapping, 1200, 700, 800, 240, 200, 300, 800, 240)]
    public void HostSizeUsesStableAllocationForItsAxis(OHsWorkstationDrawerKind kind,
        double dockWidth, double dockHeight, double tracksWidth, double tracksHeight,
        double side, double desired, double width, double height) =>
        Assert.That(WorkstationPage.CalculateDrawerHostSize(kind, new Size(dockWidth, dockHeight),
            new Size(tracksWidth, tracksHeight), side, desired), Is.EqualTo(new Size(width, height)));

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
