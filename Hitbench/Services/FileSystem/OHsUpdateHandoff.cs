using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Hitbench.Resources.DataModels;

namespace Hitbench.Services.FileSystem;

public sealed class OHsUpdateHandoff(string applicationDirectory, Func<ProcessStartInfo, Process?>? startProcess = null,
    TimeSpan? readyTimeout = null)
{
    public async Task<string> PrepareAsync(OHsStagedUpdate update, CancellationToken token)
    {
        var install = Path.GetFullPath(applicationDirectory);
        OHsUpdateService.RejectLinkedAncestors(install);
        OHsUpdateService.RejectLinkedAncestors(update.Workspace);
        if (Path.GetFullPath(update.Workspace).StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update workspace must be outside the installation.");
        foreach (var file in update.Files) OHsUpdateService.ValidateRelativePath(file);
        var jobPath = Path.Combine(update.Workspace, "job.json");
        using var original = Process.GetCurrentProcess();
        var job = new { InstallDirectory = install, update.Workspace, update.PayloadDirectory, update.Files,
            OriginalProcessId = original.Id, OriginalProcessStartTime = original.StartTime.ToUniversalTime().ToString("O"), Executable = "Hitbench.exe" };
        await File.WriteAllTextAsync(jobPath, JsonSerializer.Serialize(job), token).ConfigureAwait(false);
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Hitbench.UpdateHelper") ?? throw new InvalidOperationException("Missing update helper.");
        using var reader = new StreamReader(resource);
        var script = await reader.ReadToEndAsync(token).ConfigureAwait(false);
        script += "\r\nInvoke-OHsUpdate -JobPath $env:HITBENCH_UPDATE_JOB | Out-Null\r\n";
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = install };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) info.ArgumentList.Add(arg);
        info.Environment["HITBENCH_UPDATE_JOB"] = jobPath;
        try
        {
            using var process = (startProcess ?? Process.Start)(info) ?? throw new IOException("Unable to start update helper.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(readyTimeout ?? TimeSpan.FromSeconds(10));
            while (!File.Exists(Path.Combine(update.Workspace, "ready")))
            {
                if (process.HasExited) throw new IOException("Update helper exited before becoming ready.");
                await Task.Delay(50, timeout.Token).ConfigureAwait(false);
            }
            return jobPath;
        }
        catch { Abort(jobPath); throw; }
    }

    public void Commit(string jobPath)
    {
        var workspace = Path.GetDirectoryName(Path.GetFullPath(jobPath))!;
        OHsUpdateService.RejectLinkedAncestors(workspace);
        var temporary = Path.Combine(workspace, "commit.tmp");
        File.WriteAllText(temporary, "");
        File.Move(temporary, Path.Combine(workspace, "commit"));
    }
    public void Abort(string jobPath)
    {
        var workspace = Path.GetDirectoryName(Path.GetFullPath(jobPath))!;
        if (!Directory.Exists(workspace) || File.Exists(Path.Combine(workspace, "commit"))) return;
        OHsUpdateService.RejectLinkedAncestors(workspace);
        File.WriteAllText(Path.Combine(workspace, "abort"), "");
    }
}
