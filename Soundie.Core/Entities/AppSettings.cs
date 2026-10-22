namespace Soundie.Core.Entities;

public class AppSettings
{
    public int Theme { get; set; } = 0;
    public int Volume { get; set; } = 100;
    public string? AsioDriverName { get; set; }
    public string OsuPath { get; set; } = string.Empty;
    public bool IsMaximized { get; set; }
    public double RestoredWidth { get; set; } = 1280;
    public double RestoredHeight { get; set; } = 720;
}
