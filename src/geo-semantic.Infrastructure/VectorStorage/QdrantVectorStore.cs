using System.Security.Cryptography;
using System.Text;
using GeoSemantic.Application.Abstractions;
using GeoSemantic.Infrastructure.AI;
using GeoSemantic.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace GeoSemantic.Infrastructure.VectorStorage;

public sealed class QdrantVectorStore(QdrantClient client, IOptions<QdrantOptions> options, IOptions<OllamaOptions> embeddingOptions,
    ILogger<QdrantVectorStore> logger) : IVectorStore
{
    private readonly SemaphoreSlim collectionLock = new(1);
    public string CollectionName { get; } = BuildCollectionName(options.Value.CollectionPrefix, embeddingOptions.Value.EmbeddingModel, embeddingOptions.Value.EmbeddingDimensions);
    private int Dimensions => embeddingOptions.Value.EmbeddingDimensions;
    public static string BuildCollectionName(string prefix, string model, int dimensions) =>
        $"{prefix}_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(model))).ToLowerInvariant()[..16]}_{dimensions}_cosine";

    private static Filter DatasetFilter(Guid id) => new() {
        Must = { new Condition { Field = new FieldCondition { Key = "DatasetId", Match = new Match { Keyword = id.ToString() } } } }
    };
    public async Task EnsureCollectionAsync(CancellationToken ct = default)
    {
        await collectionLock.WaitAsync(ct);
        try
        {
            if (!await client.CollectionExistsAsync(CollectionName, ct))
            {
                await client.CreateCollectionAsync(CollectionName, new VectorParams { Size = (ulong)Dimensions, Distance = Distance.Cosine }, cancellationToken: ct);
                logger.LogInformation("Created Qdrant collection {Collection}", CollectionName);
            }
            var info = await client.GetCollectionInfoAsync(CollectionName, ct);
            var config = info.Config.Params.VectorsConfig.Params;
            if (config is null || config.Size != (ulong)Dimensions || config.Distance != Distance.Cosine)
                throw new UserOperationException("The vector collection is incompatible with the configured model. Check model and dimension configuration.");
            await client.CreatePayloadIndexAsync(CollectionName, "DatasetId", PayloadSchemaType.Keyword, cancellationToken: ct);
        }
        finally { collectionLock.Release(); }
    }
    public static PointStruct MapPoint(VectorEntry entry)
    {
        var feature = entry.Feature;
        var point = new PointStruct { Id = new PointId { Uuid = feature.Id.ToString() }, Vectors = entry.Vector };
        point.Payload.Add("FeatureId", feature.Id.ToString());
        point.Payload.Add("DatasetId", feature.DatasetId.ToString());
        point.Payload.Add("GeometryType", feature.GeometryType);
        point.Payload.Add("Name", feature.Name);
        point.Payload.Add("Category", feature.Category ?? "");
        point.Payload.Add("SearchableText", feature.SearchableText);
        return point;
    }
    public async Task UpsertVectorsAsync(IReadOnlyList<VectorEntry> entries, CancellationToken ct = default)
    {
        OllamaEmbeddingService.ValidateResponse(entries.Select(e => e.Vector).ToArray(), entries.Count, Dimensions);
        await client.UpsertAsync(CollectionName, entries.Select(MapPoint).ToArray(), wait: true, cancellationToken: ct);
        logger.LogInformation("Upserted {Count} points to {Collection}", entries.Count, CollectionName);
    }
    public async Task<IReadOnlyList<VectorHit>> SearchAsync(float[] vector, int topK, Guid? datasetId, CancellationToken ct = default)
    {
        OllamaEmbeddingService.ValidateResponse([vector], 1, Dimensions);
        var response = await client.QueryAsync(CollectionName, query: vector, filter: datasetId.HasValue ? DatasetFilter(datasetId.Value) : null,
            limit: (ulong)Math.Clamp(topK, 1, 50), payloadSelector: true, cancellationToken: ct);
        var points = response;
        logger.LogInformation("Vector search returned {Count} points from {Collection}", points.Count, CollectionName);
        return points.Select(p => new VectorHit(Guid.Parse(p.Payload["FeatureId"].StringValue),
            Guid.Parse(p.Payload["DatasetId"].StringValue), p.Score)).ToArray();
    }
    public async Task DeleteDatasetVectorsAsync(Guid id, string collection, CancellationToken ct = default)
    {
        if (!collection.StartsWith(options.Value.CollectionPrefix + "_", StringComparison.Ordinal))
            throw new UserOperationException("Dataset vector metadata refers to an unmanaged collection.");
        if (await client.CollectionExistsAsync(collection, ct))
            await client.DeleteAsync(collection, DatasetFilter(id), wait: true, cancellationToken: ct);
        logger.LogInformation("Deleted vectors for dataset {DatasetId} from {Collection}", id, collection);
    }
    public async Task<VectorCollectionInfo?> GetCollectionInfoAsync(CancellationToken ct = default)
    {
        if (!await client.CollectionExistsAsync(CollectionName, ct)) return null;
        var info = await client.GetCollectionInfoAsync(CollectionName, ct);
        return new(CollectionName, info.PointsCount, (int)info.Config.Params.VectorsConfig.Params.Size);
    }
    public async Task<ServiceHealth> CheckHealthAsync(CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try {
            await client.ListCollectionsAsync(timeout.Token);
            return new(true, "Ready · gRPC vector storage");
        }
        catch (Exception ex) when (ex is Grpc.Core.RpcException or HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning("Qdrant health check failed: {ErrorType}", ex.GetType().Name);
            return new(false, "Qdrant is unavailable. Start the existing geosemantic-qdrant container and check port 6334.");
        }
    }
}

