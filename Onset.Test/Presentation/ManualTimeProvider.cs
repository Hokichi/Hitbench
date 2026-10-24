namespace Onset.Test.Presentation;

public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset now;
    public void SetUtcNow(DateTimeOffset value) => now = value;
    public override DateTimeOffset GetUtcNow() => now;
}
