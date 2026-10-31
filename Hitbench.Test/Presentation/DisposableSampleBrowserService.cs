namespace Hitbench.Test.Presentation;

public sealed class DisposableSampleBrowserService : StubSampleBrowserService, IDisposable
{
    public int DisposeCalls { get; private set; }
    public Action? DisposeAction { get; set; }
    public void Dispose() { DisposeCalls++; DisposeAction?.Invoke(); }
}
