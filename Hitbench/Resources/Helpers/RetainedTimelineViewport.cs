namespace Hitbench.Resources.Helpers;

/// <summary>A bounded overscan interval whose drawings move without rebuilding inside its bounds.</summary>
public sealed class RetainedTimelineViewport
{
    private bool valid, wasRetained;
    private double viewport;
    public double Origin { get; private set; }
    public double Start { get; private set; }
    public double Width { get; private set; }
    public double Translation { get; private set; }
    public void Invalidate() => valid = false;

    public bool Update(double offset, double viewportWidth, bool retained)
    {
        if (!double.IsFinite(offset) || !double.IsFinite(viewportWidth) || viewportWidth <= 0)
        { Invalidate(); Origin = Start = Width = Translation = 0; return true; }
        var rebuild = !valid || viewport != viewportWidth || wasRetained != retained ||
            (retained ? offset < Origin + Start || offset + viewportWidth > Origin + Start + Width : offset != Origin);
        if (rebuild)
        {
            Origin = offset; viewport = viewportWidth; wasRetained = retained;
            Start = retained ? -viewportWidth / 2 : 0;
            Width = retained ? viewportWidth * 2 : viewportWidth;
            valid = true;
        }
        Translation = Origin - offset;
        return rebuild;
    }
}
