using System.Text.Json;
using GeoSemantic.Application.Abstractions;
using GeoSemantic.Application.Features.Indexing;
using GeoSemantic.Application.Features.Search;
using GeoSemantic.Application.Features.Datasets;
using GeoSemantic.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;

namespace GeoSemantic.Application.Tests;

public sealed class ApplicationTests
{
    [Fact] public void TextIsDeterministicUnicodeAndScalarOnly() {
        var props = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"name":"Çınar Parkı","category":"Park","number":42,"open":true,"null":null,"empty":" ","nested":{"x":1},"list":[1,2]}""")!;
        var generator = new SearchableTextGenerator();
        var text = generator.Generate(props, props.Keys.Reverse());
        Assert.Equal("Category: Park. Name: Çınar Parkı. Number: 42. Open: true.", text);
        Assert.Equal(text, generator.Generate(props, props.Keys));
    }
    [Fact] public void EmptyPropertiesProduceNoText() => Assert.Equal("", new SearchableTextGenerator().Generate(new Dictionary<string, JsonElement>(), ["missing"]));
    [Theory][InlineData("")][InlineData("   ")][InlineData(null)]
    public void EmptyQueriesRejected(string? query) => Assert.Throws<UserOperationException>(() => new SearchRequest(query!).Validate());
    [Fact] public void ExcessiveQueriesRejected() => Assert.Throws<UserOperationException>(() => new SearchRequest(new string('a', 2001)).Validate());
    [Fact] public void TurkishQueriesPreserved() => Assert.Equal("Yeşil alanları göster", new SearchRequest(" Yeşil alanları göster ").Validate());
    [Fact] public void DatasetOperationsExcludeConflictingJobs() {
        var operations = new DatasetOperations(); var id = Guid.NewGuid();
        using (operations.Acquire(id)) Assert.Throws<UserOperationException>(() => operations.Acquire(id));
        using var next = operations.Acquire(id);
    }
    [Fact] public async Task SearchOrdersResultsAndClampsTopK() {
        var fakes = new Fakes();
        var service = new SemanticSearchService(fakes, fakes, fakes, fakes, new SearchOptions());
        var response = await service.SearchAsync(new("parks", fakes.Dataset.Id, 1000));
        Assert.Equal(50, fakes.LastTopK);
        Assert.Equal([.9, .3], response.Results.Select(r => r.Score).ToArray());
        Assert.Equal([1, 2], response.Results.Select(r => r.Rank).ToArray());
        Assert.Equal(fakes.Dataset.Id, fakes.Filter);
    }
    [Fact] public async Task DeleteFailurePreservesLocalDataset() {
        var fakes = new Fakes { FailDelete = true };
        var service = new DatasetService(fakes, fakes, new DatasetOperations());
        await Assert.ThrowsAsync<IOException>(() => service.DeleteDatasetAsync(fakes.Dataset.Id));
        Assert.False(fakes.LocalDeleted);
    }
    [Fact] public async Task IndexingBatchesAndCompletesUsingRealOrchestrator() {
        var fakes = new Fakes();
        var coordinator = new IndexingCoordinator(fakes, fakes, fakes, new SearchableTextGenerator(), new DatasetOperations(), NullLogger<IndexingCoordinator>.Instance);
        await coordinator.StartAsync(default);
        try {
            coordinator.Start(fakes.Dataset.Id, ["name"]);
            await WaitFinished(coordinator, fakes.Dataset.Id);
            Assert.Equal(DatasetStatus.Indexed, fakes.Dataset.Status);
            Assert.Equal(2, coordinator.GetProgress(fakes.Dataset.Id)!.Processed);
            Assert.Equal(2, coordinator.GetProgress(fakes.Dataset.Id)!.Batches);
            Assert.Equal(2, fakes.Upserts);
        } finally { await coordinator.StopAsync(default); coordinator.Dispose(); }
    }
    [Fact] public async Task FailedEmbeddingLeavesRetryableMetadata() {
        var fakes = new Fakes { FailEmbedding = true };
        var coordinator = new IndexingCoordinator(fakes, fakes, fakes, new SearchableTextGenerator(), new DatasetOperations(), NullLogger<IndexingCoordinator>.Instance);
        await coordinator.StartAsync(default);
        try {
            coordinator.Start(fakes.Dataset.Id, ["name"]);
            await WaitFinished(coordinator, fakes.Dataset.Id);
            Assert.Equal(DatasetStatus.Failed, fakes.Dataset.Status);
            Assert.NotNull(fakes.Dataset.CollectionName);
            Assert.Equal(0, fakes.Upserts);
        } finally { await coordinator.StopAsync(default); coordinator.Dispose(); }
    }
    private static async Task WaitFinished(IndexingCoordinator coordinator, Guid id) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (coordinator.GetProgress(id)?.Finished != true) await Task.Delay(10, timeout.Token);
    }
}

// Small explicit fakes preserve the service boundaries without requiring external services.
internal sealed class Fakes : IEmbeddingService, IVectorStore, IDatasetRepository, IGeoJsonService
{
    public GeoDataset Dataset { get; set; } = new() { Id = Guid.NewGuid(), Name = "Test", FeatureCount = 2, AvailableProperties = ["name"], Status = DatasetStatus.Indexed, CollectionName = "test", EmbeddingModel = "test", EmbeddingDimensions = 2 };
    public string CollectionName => "test";
    public int LastTopK; public Guid? Filter; public int Upserts; public bool LocalDeleted; public bool FailDelete; public bool FailEmbedding;
    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default) => Task.FromResult(new float[] { 1, 0 });
    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        FailEmbedding ? throw new UserOperationException("Embedding failed") : Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[] { 1, 0 }).ToArray());
    public Task<EmbeddingModelInfo> GetModelInfoAsync(CancellationToken ct = default) => Task.FromResult(new EmbeddingModelInfo("test", 2, 1));
    public Task<ServiceHealth> CheckHealthAsync(CancellationToken ct = default) => Task.FromResult(new ServiceHealth(true, "ready"));
    public Task EnsureCollectionAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task UpsertVectorsAsync(IReadOnlyList<VectorEntry> entries, CancellationToken ct = default) { Upserts += entries.Count; return Task.CompletedTask; }
    public async Task<IReadOnlyList<VectorHit>> SearchAsync(float[] vector, int topK, Guid? datasetId, CancellationToken ct = default) {
        LastTopK = topK; Filter = datasetId; var features = await ReadFeaturesAsync(Dataset.Id, ct);
        return [new(features[0].Id, Dataset.Id, .3), new(features[1].Id, Dataset.Id, .9)];
    }
    public Task DeleteDatasetVectorsAsync(Guid id, string collection, CancellationToken ct = default) => FailDelete ? throw new IOException("Unavailable") : Task.CompletedTask;
    public Task<VectorCollectionInfo?> GetCollectionInfoAsync(CancellationToken ct = default) => Task.FromResult<VectorCollectionInfo?>(null);
    public Task<GeoDataset> ImportAsync(Stream input, string originalFileName, string? name, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<GeoDataset>> GetDatasetsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GeoDataset>>([Dataset]);
    public Task<GeoDataset?> GetDatasetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<GeoDataset?>(Dataset);
    public Task<IReadOnlyList<GeoFeature>> ReadFeaturesAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GeoFeature>>(
        Enumerable.Range(0, 2).Select(i => new GeoFeature(GeoFeature.StableId(id, i), id, null, new Point(32, 39), JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"name":"Park"}""")!)).ToArray());
    public Task SaveAsync(GeoDataset dataset, CancellationToken ct = default) { Dataset = dataset; return Task.CompletedTask; }
    public Task DeleteAsync(Guid id, CancellationToken ct = default) { LocalDeleted = true; return Task.CompletedTask; }
    public IReadOnlyList<GeoFeature> Parse(ReadOnlyMemory<byte> json, Guid datasetId) => throw new NotSupportedException();
    public string Serialize(IEnumerable<GeoFeature> features) => "{}";
}

