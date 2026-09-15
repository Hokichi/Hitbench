using osuHitsounder.Core.Entities;
using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Presentation;

public sealed class StubAppSettingsService : IAppSettingsService
{
    public Func<CancellationToken, Task<AppSettings>> Load { get; set; } =
        _ => Task.FromResult(new AppSettings { OsuPath = @"C:\osu" });
    public Func<string, CancellationToken, Task> Save { get; set; } = (_, _) => Task.CompletedTask;
    public Func<(bool IsMaximized, double Width, double Height), CancellationToken, Task> SaveWindowPlacement { get; set; } =
        (_, _) => Task.CompletedTask;
    public Task<AppSettings> LoadAsync(CancellationToken token) => Load(token);
    public Task SaveOsuPathAsync(string path, CancellationToken token) => Save(path, token);
    public Task SaveWindowPlacementAsync(bool isMaximized, double restoredWidth, double restoredHeight,
        CancellationToken token) => SaveWindowPlacement((isMaximized, restoredWidth, restoredHeight), token);
}
