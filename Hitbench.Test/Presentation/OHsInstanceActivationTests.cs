using System.Windows;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class OHsInstanceActivationTests
{
    [TestCase(false, WindowState.Normal)]
    [TestCase(true, WindowState.Maximized)]
    public void MinimizedShellRestoresPreviousStateAndTargetsDeepestModal(bool wasMaximized, WindowState expected)
    {
        var shell = new Window();
        var modal = new Window();
        var nested = new Window();
        var states = new List<WindowState>();
        Window? target = null;
        try
        {
            MainWindow.RestoreAndActivateCore(shell, WindowState.Minimized, wasMaximized, states.Add,
                owner => owner == shell ? [modal] : owner == modal ? [nested] : [], _ => true, window => target = window);
            Assert.That(states, Is.EqualTo(new[] { expected }));
            Assert.That(target, Is.SameAs(nested));
        }
        finally { nested.Close(); modal.Close(); shell.Close(); }
    }

    [Test]
    public void AlreadyMaximizedDoesNotChangePlacement()
    {
        var shell = new Window();
        try
        {
            var states = new List<WindowState>();
            MainWindow.RestoreAndActivateCore(shell, WindowState.Maximized, true, states.Add, _ => [], _ => true, _ => { });
            Assert.That(states, Is.Empty);
        }
        finally { shell.Close(); }
    }
}
