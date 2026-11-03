using System.Diagnostics;
using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;
using Hitbench.Services.Ui;

namespace Hitbench.Test.Services;

public sealed class OHsUpdateRestartServiceTests
{
    [TestCase(false, false)] [TestCase(true, false)] [TestCase(true, true)]
    public async Task OnlySuccessfulSaveAndAcceptedCloseAuthorizeInstall(bool saveSucceeds, bool acceptClose)
    {
        var root = Directory.CreateTempSubdirectory("hitbench-restart-test-").FullName;
        try
        {
            var install = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
            var workspace = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
            var handoff = new OHsUpdateHandoff(install, _ => { File.WriteAllText(Path.Combine(workspace, "ready"), ""); return Process.GetCurrentProcess(); });
            var resumed = false;
            var service = new OHsUpdateRestartService(handoff, _ => Task.FromResult(saveSucceeds),
                (commit, _) => { if (acceptClose) commit(); return Task.FromResult(acceptClose); }, () => resumed = true);
            var success = await service.TryRestartAsync(new("1.2.4", workspace, Path.Combine(workspace, "payload"), ["Hitbench.exe"]), default);
            Assert.That(success, Is.EqualTo(saveSucceeds && acceptClose));
            Assert.That(File.Exists(Path.Combine(workspace, "commit")), Is.EqualTo(success));
            Assert.That(resumed, Is.EqualTo(!success));
        }
        finally { Directory.Delete(root, true); }
    }
}
