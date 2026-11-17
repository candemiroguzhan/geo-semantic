using GeoSemantic.Domain.Entities;

namespace GeoSemantic.Application.Abstractions;

public sealed record EmbeddingModelInfo(string Model, int Dimensions, int BatchSize);
public sealed record ServiceHealth(bool Online, string Message);
public sealed record VectorEntry(GeoFeature Feature, float[] Vector);
public sealed record VectorHit(Guid FeatureId, Guid DatasetId, double Score);
public sealed record VectorCollectionInfo(string Name, ulong Count, int Dimensions);

public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
    Task<EmbeddingModelInfo> GetModelInfoAsync(CancellationToken ct = default);
    Task<ServiceHealth> CheckHealthAsync(CancellationToken ct = default);
}
public interface IVectorStore
{
    string CollectionName { get; }
    Task EnsureCollectionAsync(CancellationToken ct = default);
    Task UpsertVectorsAsync(IReadOnlyList<VectorEntry> entries, CancellationToken ct = default);
    Task<IReadOnlyList<VectorHit>> SearchAsync(float[] vector, int topK, Guid? datasetId, CancellationToken ct = default);
    Task DeleteDatasetVectorsAsync(Guid id, string collection, CancellationToken ct = default);
    Task<VectorCollectionInfo?> GetCollectionInfoAsync(CancellationToken ct = default);
    Task<ServiceHealth> CheckHealthAsync(CancellationToken ct = default);
}
public interface IGeoJsonService
{
    IReadOnlyList<GeoFeature> Parse(ReadOnlyMemory<byte> json, Guid datasetId);
    string Serialize(IEnumerable<GeoFeature> features);
}
public interface IDatasetRepository
{
    Task<GeoDataset> ImportAsync(Stream input, string originalFileName, string? name, CancellationToken ct = default);
    Task<IReadOnlyList<GeoDataset>> GetDatasetsAsync(CancellationToken ct = default);
    Task<GeoDataset?> GetDatasetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<GeoFeature>> ReadFeaturesAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(GeoDataset dataset, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
public sealed class UserOperationException(string message, Exception? inner = null) : Exception(message, inner);

