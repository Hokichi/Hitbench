using osuHitsounder.Core.Entities;
using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Presentation;

public sealed class StubAppSettingsService : IAppSettingsService
{
    public Func<CancellationToken, Task<AppSettings>> Load { get; set; } =
        _ => Task.FromResult(new AppSettings { OsuPath = @"C:\osu" });
    public Func<string, CancellationToken, Task> Save { get; set; } = (_, _) => Task.CompletedTask;
    public Task<AppSettings> LoadAsync(CancellationToken token) => Load(token);
    public Task SaveOsuPathAsync(string path, CancellationToken token) => Save(path, token);
}

