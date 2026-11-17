using GeoSemantic.Application.Abstractions;
using GeoSemantic.Application.Features.Indexing;
using GeoSemantic.Domain.Entities;
namespace GeoSemantic.Application.Features.Datasets;

public sealed class DatasetService(IDatasetRepository repository, IVectorStore vectors, DatasetOperations operations)
{
    public Task<GeoDataset> ImportDatasetAsync(Stream input, string filename, string? name, CancellationToken ct = default) =>
        repository.ImportAsync(input, filename, name, ct);
    public Task<IReadOnlyList<GeoDataset>> GetDatasetsAsync(CancellationToken ct = default) => repository.GetDatasetsAsync(ct);
    public Task<GeoDataset?> GetDatasetAsync(Guid id, CancellationToken ct = default) => repository.GetDatasetAsync(id, ct);
    public async Task DeleteDatasetAsync(Guid id, CancellationToken ct = default)
    {
        using var lease = operations.Acquire(id);
        var dataset = await repository.GetDatasetAsync(id, ct) ?? throw new UserOperationException("Dataset was not found.");
        // Vector deletion first: retain local data and metadata if Qdrant fails, allowing a safe retry.
        if (dataset.CollectionName is not null) await vectors.DeleteDatasetVectorsAsync(id, dataset.CollectionName, ct);
        await repository.DeleteAsync(id, ct);
    }
}

