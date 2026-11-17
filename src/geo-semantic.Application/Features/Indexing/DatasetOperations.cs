using System.Collections.Concurrent;
using GeoSemantic.Application.Abstractions;
namespace GeoSemantic.Application.Features.Indexing;

// Serializes destructive work per dataset across all browser circuits.
public sealed class DatasetOperations
{
    private readonly ConcurrentDictionary<Guid, byte> active = new();
    public IDisposable Acquire(Guid id)
    {
        if (!active.TryAdd(id, 0)) throw new UserOperationException("This dataset has an active operation. Wait or cancel indexing first.");
        return new Lease(() => active.TryRemove(id, out _));
    }
    private sealed class Lease(Action release) : IDisposable { public void Dispose() => release(); }
}

