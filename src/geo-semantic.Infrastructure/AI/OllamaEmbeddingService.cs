using System.Net.Http.Json;
using System.Text.Json;
using GeoSemantic.Application.Abstractions;
using GeoSemantic.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoSemantic.Infrastructure.AI;

public sealed class OllamaEmbeddingService(IHttpClientFactory factory, IOptions<OllamaOptions> options,
    ILogger<OllamaEmbeddingService> logger) : IEmbeddingService
{
    private OllamaOptions Config => options.Value;
    public Task<EmbeddingModelInfo> GetModelInfoAsync(CancellationToken ct = default) =>
        Task.FromResult(new EmbeddingModelInfo(Config.EmbeddingModel, Config.EmbeddingDimensions, Config.BatchSize));
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default) =>
        (await GenerateEmbeddingsAsync([text], ct))[0];

    public static void ValidateResponse(IReadOnlyList<float[]>? vectors, int count, int dimensions)
    {
        if (vectors is null || vectors.Count != count || vectors.Any(v => v is null || v.Length != dimensions ||
            v.Any(x => !float.IsFinite(x)) || v.All(x => x == 0)))
            throw new UserOperationException($"Ollama returned invalid embeddings. Expected {count} nonzero vectors of {dimensions} finite values.");
    }
    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];
        if (texts.Any(string.IsNullOrWhiteSpace)) throw new UserOperationException("Cannot embed empty text.");
        var result = new List<float[]>();
        var client = factory.CreateClient("Ollama");
        foreach (var batch in texts.Chunk(Config.BatchSize))
        {
            logger.LogInformation("Embedding batch {Count} with {Model}", batch.Length, Config.EmbeddingModel);
            try
            {
                using var response = await client.PostAsJsonAsync("api/embed", new { model = Config.EmbeddingModel, input = batch, truncate = false }, ct);
                if (!response.IsSuccessStatusCode)
                    throw new UserOperationException($"Ollama embedding request failed (HTTP {(int)response.StatusCode}). Check the installed model and input length.");
                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var responseModel = doc.RootElement.GetProperty("model").GetString();
                if (responseModel != Config.EmbeddingModel) throw new UserOperationException("Ollama returned a different embedding model.");
                var vectors = doc.RootElement.GetProperty("embeddings").Deserialize<float[][]>();
                ValidateResponse(vectors, batch.Length, Config.EmbeddingDimensions);
                result.AddRange(vectors!);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException)
            { throw new UserOperationException("Cannot obtain embeddings from Ollama. Check its endpoint and model.", ex); }
        }
        return result;
    }
    public async Task<ServiceHealth> CheckHealthAsync(CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var client = factory.CreateClient("Ollama");
            using var response = await client.GetAsync("api/tags", timeout.Token);
            response.EnsureSuccessStatusCode();
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
            var exists = doc.RootElement.GetProperty("models").EnumerateArray().Any(m => m.GetProperty("name").GetString() == Config.EmbeddingModel);
            return new(exists, exists ? $"Ready · {Config.EmbeddingModel}" : $"Required model {Config.EmbeddingModel} is not installed. No download was attempted.");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            logger.LogWarning("Ollama health check failed: {ErrorType}", ex.GetType().Name);
            return new(false, "Ollama is unavailable. Start Ollama and verify the configured endpoint.");
        }
    }
}

