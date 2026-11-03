using System.Diagnostics;
using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Services;

public sealed class OHsUpdateHandoffTests
{
    [Test]
    public void HelperReadinessTimeoutAbortsWithoutAuthorizingInstall()
    {
        var root = Directory.CreateTempSubdirectory("hitbench-ready-timeout-").FullName;
        try
        {
            var install = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
            var workspace = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
            var handoff = new OHsUpdateHandoff(install, _ => Process.GetCurrentProcess(), TimeSpan.FromMilliseconds(20));
            Assert.CatchAsync<OperationCanceledException>(() => handoff.PrepareAsync(new("1.2.4", workspace, Path.Combine(workspace, "payload"), ["Hitbench.exe"]), default));
            Assert.That(File.Exists(Path.Combine(workspace, "abort")), Is.True);
            Assert.That(File.Exists(Path.Combine(workspace, "commit")), Is.False);
        }
        finally { Directory.Delete(root, true); }
    }
    [Test]
    public async Task ReadyHelperIsHiddenAndCommitRequiresExplicitAuthorization()
    {
        var root = Directory.CreateTempSubdirectory("hitbench-handoff-").FullName;
        try
        {
            var install = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
            var workspace = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
            var staged = new OHsStagedUpdate("1.2.4", workspace, Path.Combine(workspace, "payload"), ["Hitbench.exe"]);
            ProcessStartInfo? started = null;
            var handoff = new OHsUpdateHandoff(install, info =>
            {
                started = info;
                File.WriteAllText(Path.Combine(workspace, "ready"), "");
                return Process.GetCurrentProcess();
            });
            var job = await handoff.PrepareAsync(staged, default);
            Assert.That(started!.UseShellExecute, Is.False);
            Assert.That(started.CreateNoWindow, Is.True);
            Assert.That(started.WorkingDirectory, Is.Not.EqualTo(workspace), "A helper cannot remove its native working directory during cleanup.");
            Assert.That(started.ArgumentList.Sum(argument => argument.Length + 3), Is.LessThan(32000));
            Assert.That(started.WindowStyle, Is.EqualTo(ProcessWindowStyle.Hidden));
            Assert.That(started.Environment["HITBENCH_UPDATE_JOB"], Is.EqualTo(job));
            Assert.That(File.Exists(Path.Combine(workspace, "commit")), Is.False);
            handoff.Commit(job);
            handoff.Abort(job);
            Assert.That(File.Exists(Path.Combine(workspace, "commit")), Is.True);
            Assert.That(File.Exists(Path.Combine(workspace, "abort")), Is.False);
        }
        finally { Directory.Delete(root, true); }
    }
}
