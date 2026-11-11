namespace Hitbench.Core.Entities;

public sealed class InstrumentalNote
{
    public string Key { get; set; } = "C4";
    public int Timestamp { get; set; }
    public bool IsReadOnly { get; set; }
}
