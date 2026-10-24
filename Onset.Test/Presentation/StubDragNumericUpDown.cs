using Onset.Resources.CustomControls;

namespace Onset.Test.Presentation;

public sealed class StubDragNumericUpDown : NumericUpDown
{
    public bool CanCapture { get; set; } = true;
    public int Captures { get; private set; }
    public int Releases { get; private set; }
    protected override bool TryCaptureVerticalDragPointer() { Captures++; return CanCapture; }
    protected override void ReleaseVerticalDragPointer() => Releases++;
}
