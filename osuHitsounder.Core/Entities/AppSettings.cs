namespace osuHitsounder.Core.Entities;

public class AppSettings
{
    public string OsuPath { get; set; } = string.Empty;
    public bool IsMaximized { get; set; }
    public double RestoredWidth { get; set; } = 1280;
    public double RestoredHeight { get; set; } = 720;
}
