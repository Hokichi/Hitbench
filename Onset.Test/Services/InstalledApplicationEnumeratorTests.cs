using Microsoft.Win32;
using Onset.Services.FileSystem;

namespace Onset.Test.Services;

public class InstalledApplicationEnumeratorTests
{
    [Test]
    public void ReadsRegisteredLocationsIconsAndAppPathsWithoutRunningCommands()
    {
        var path = @"Software\Onset.Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            using var root = Registry.CurrentUser.CreateSubKey(path);
            using (var app = root.CreateSubKey(@"Uninstall\location"))
                app.SetValue("InstallLocation", @"C:\Games\stable");
            using (var app = root.CreateSubKey(@"Uninstall\icon"))
                app.SetValue("DisplayIcon", "\"C:\\Games\\stable, current\\OSU!.EXE\",0");
            using (var app = root.CreateSubKey(@"Uninstall\lazer"))
                app.SetValue("DisplayIcon", "\"C:\\Games\\lazer\\osu!lazer.exe\",0");
            using (var app = root.CreateSubKey(@"App Paths\osu!lazer.exe"))
                app.SetValue("", @"C:\Games\lazer\osu!lazer.exe");
            using (var app = root.CreateSubKey(@"Uninstall\irrelevant"))
            {
                app.SetValue("DisplayIcon", @"C:\Other\uninstall.exe,0");
                app.SetValue("UninstallString", @"C:\DoNotExecute.exe");
            }
            using (var app = root.CreateSubKey(@"App Paths\osu!.exe"))
                app.SetValue("", "\"%LOCALAPPDATA%\\osu!\\osu!.exe\"");
            var directories = InstalledApplicationEnumerator.Enumerate(root, CancellationToken.None,
                () => Assert.Fail("All test registry keys are readable")).ToArray();
            Assert.That(directories, Is.EquivalentTo(new[]
            {
                @"C:\Games\stable", @"C:\Games\stable, current",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu!")
            }));
            Assert.Throws<OperationCanceledException>(() =>
                InstalledApplicationEnumerator.Enumerate(root, new CancellationToken(true), () => { }).ToArray());
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }

    [Test]
    public async Task SkippedRegistryLocationDoesNotHideLaterInstallation()
    {
        var root = Directory.CreateTempSubdirectory("osu-registered-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "osu!.exe"), "");
            var service = new HomeFolderService(root.FullName, (_, skipped) =>
            {
                skipped();
                return [root.FullName];
            });
            var result = await service.FindInstallationAsync(CancellationToken.None);
            Assert.That(result.Directory, Is.EqualTo(root.FullName));
            Assert.That(result.SkippedLocations, Is.True);
        }
        finally { root.Delete(true); }
    }
}
