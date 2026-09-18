namespace osuHitsounder.Services.Ui;

/// <summary>Song viewport geometry in device-independent units.</summary>
public sealed class SongTileLayout
{
    private readonly double horizontalGap;

    public SongTileLayout(double maximizedWidth, double maximizedHeight, double horizontalGap, double verticalGap)
    {
        RequirePositive(maximizedWidth, nameof(maximizedWidth));
        RequirePositive(maximizedHeight, nameof(maximizedHeight));
        if (!double.IsFinite(horizontalGap) || horizontalGap < 0)
            throw new ArgumentOutOfRangeException(nameof(horizontalGap));
        if (!double.IsFinite(verticalGap) || verticalGap < 0)
            throw new ArgumentOutOfRangeException(nameof(verticalGap));
        this.horizontalGap = horizontalGap;
        BaselineWidth = (maximizedWidth - 4 * horizontalGap) / 5;
        TileHeight = (maximizedHeight - 7 * verticalGap) / 8;
        RequirePositive(BaselineWidth, nameof(maximizedWidth));
        RequirePositive(TileHeight, nameof(maximizedHeight));
        MinimumViewportWidth = .75 * BaselineWidth;
        MinimumViewportHeight = 3 * TileHeight + 2 * verticalGap;
    }

    public double BaselineWidth { get; }
    public double TileHeight { get; }
    public double MinimumViewportWidth { get; }
    public double MinimumViewportHeight { get; }

    public int GetColumns(double viewportWidth)
    {
        RequirePositive(viewportWidth, nameof(viewportWidth));
        var columns = 5;
        while (columns > 1 &&
               (viewportWidth - (columns - 1) * horizontalGap) / columns <= MinimumViewportWidth)
            columns--;
        return columns;
    }

    public double GetTileWidth(double viewportWidth)
    {
        var columns = GetColumns(viewportWidth);
        return Math.Max(MinimumViewportWidth, (viewportWidth - (columns - 1) * horizontalGap) / columns);
    }

    private static void RequirePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
    }
}
