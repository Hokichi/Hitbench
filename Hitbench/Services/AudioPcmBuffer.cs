namespace Hitbench.Services;

internal sealed class AudioPcmBuffer(float[] values, AudioMemoryReservation? reservation)
{
    private readonly object sync = new();
    private int references = 1;
    public float[] Values { get; private set; } = values;
    internal AudioMemoryBudget? Budget { get { lock (sync) return reservation?.Budget; } }
    public void Retain() { lock (sync) { ObjectDisposedException.ThrowIf(references == 0, this); references = checked(references + 1); } }
    public void AttachBudget(AudioMemoryBudget budget)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(references == 0, this);
            if (reservation is not null)
            {
                if (ReferenceEquals(reservation.Budget, budget)) return;
                if (references != 1) throw new InvalidOperationException("Shared PCM belongs to a different audio budget.");
            }
            var bytes = checked(Values.LongLength * sizeof(float));
            budget.CheckAsset(bytes);
            var next = budget.Reserve(bytes);
            reservation?.Dispose(); reservation = next;
        }
    }
    public void Release()
    {
        lock (sync)
        {
            if (--references != 0) return;
            Values = [];
            reservation?.Dispose(); reservation = null;
        }
    }
}
