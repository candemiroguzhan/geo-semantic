using GeoSemantic.Application.Abstractions;
namespace GeoSemantic.Infrastructure.Health;

public sealed record PlatformHealth(ServiceHealth Ollama, ServiceHealth Qdrant, DateTimeOffset CheckedAt)
{
    public bool Ready => Ollama.Online && Qdrant.Online;
}
public sealed class ServiceHealthMonitor(IEmbeddingService embeddings, IVectorStore vectors)
{
    private readonly SemaphoreSlim gate = new(1);
    private PlatformHealth? cached;
    public async Task<PlatformHealth> CheckAsync(bool force = false, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!force && cached is not null && DateTimeOffset.UtcNow - cached.CheckedAt < TimeSpan.FromSeconds(30)) return cached;
            var ollama = embeddings.CheckHealthAsync(ct);
            var qdrant = vectors.CheckHealthAsync(ct);
            await Task.WhenAll(ollama, qdrant);
            return cached = new(await ollama, await qdrant, DateTimeOffset.UtcNow);
        }
        finally { gate.Release(); }
    }
}

