using System.Collections;
using Hitbench.Core.Entities;

namespace Hitbench.Test.Presentation;

internal sealed class CountingSampleSelection(IReadOnlyList<Sample> samples) : IReadOnlyCollection<Sample>
{
    public int Count => samples.Count;
    public int Enumerations { get; private set; }
    public IEnumerator<Sample> GetEnumerator()
    {
        Enumerations++;
        return samples.GetEnumerator();
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
