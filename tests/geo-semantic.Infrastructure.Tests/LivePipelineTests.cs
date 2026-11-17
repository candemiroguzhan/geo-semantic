using GeoSemantic.Application;
using GeoSemantic.Application.Abstractions;
using GeoSemantic.Application.Features.Datasets;
using GeoSemantic.Application.Features.Indexing;
using GeoSemantic.Application.Features.Search;
using GeoSemantic.Domain.Entities;
using GeoSemantic.Infrastructure.VectorStorage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Qdrant.Client;

namespace GeoSemantic.Infrastructure.Tests;

public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute() {
        if (Environment.GetEnvironmentVariable("GEOSEMANTIC_LIVE_TESTS") != "1")
            Skip = "Set GEOSEMANTIC_LIVE_TESTS=1 to use existing Ollama and Qdrant services.";
    }
}
public sealed class LivePipelineTests
{
    [LiveFact, Trait("Category", "Integration")]
    public async Task ImportIndexReindexSearchExportRestartAndDelete()
    {
        var repoRoot = FindRoot();
        var dataRoot = Path.Combine(repoRoot, "artifacts", "live-" + Guid.NewGuid().ToString("N"));
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = repoRoot });
        builder.Configuration["Storage:DataDirectory"] = dataRoot;
        builder.Configuration["Qdrant:CollectionPrefix"] = "geosemantic_verify_" + Guid.NewGuid().ToString("N")[..8];
        builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
        using var host = builder.Build();
        await host.StartAsync();
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var embeddings = services.GetRequiredService<IEmbeddingService>();
        var vectors = services.GetRequiredService<IVectorStore>();
        var repository = services.GetRequiredService<IDatasetRepository>();
        var datasets = services.GetRequiredService<DatasetService>();
        var indexing = services.GetRequiredService<IndexingCoordinator>();
        Guid? datasetId = null;
        try {
            Assert.True((await embeddings.CheckHealthAsync()).Online);
            Assert.True((await vectors.CheckHealthAsync()).Online);
            var vector = await embeddings.GenerateEmbeddingAsync("İnsanların yürüyüş yapabileceği yeşil alanlar");
            Assert.Equal(1024, vector.Length);
            await using var input = File.OpenRead(Path.Combine(repoRoot, "samples", "sample-pois.geojson"));
            var dataset = await datasets.ImportDatasetAsync(input, "sample.geojson", "Live verification");
            datasetId = dataset.Id;
            Assert.Equal(30, dataset.FeatureCount);
            var originalIds = (await repository.ReadFeaturesAsync(dataset.Id)).Select(f => f.Id).ToArray();
            var fields = new[] { "name", "category", "description_en", "description_tr" };
            indexing.Start(dataset.Id, fields);
            await Wait(indexing, dataset.Id);
            Assert.Equal(DatasetStatus.Indexed, (await repository.GetDatasetAsync(dataset.Id))!.Status);
            Assert.Equal(30UL, (await vectors.GetCollectionInfoAsync())!.Count);
            indexing.Start(dataset.Id, fields);
            await Wait(indexing, dataset.Id);
            Assert.Equal(30UL, (await vectors.GetCollectionInfoAsync())!.Count);
            Assert.Equal(originalIds, (await repository.ReadFeaturesAsync(dataset.Id)).Select(f => f.Id).ToArray());
            var search = services.GetRequiredService<SemanticSearchService>();
            foreach (var query in new[] { "Find green recreational spaces for walking.", "Sağlık ve acil tedavi hizmeti sunan hastaneleri bul." }) {
                var response = await search.SearchAsync(new(query, dataset.Id, 5));
                Assert.Equal(5, response.RetrievedCount);
                Assert.True(response.Results[0].Score >= response.Results[^1].Score);
                Assert.Contains(query.StartsWith("Find") ? "Park" : "Hospital", response.Results[0].Feature.Category!);
                var exported = services.GetRequiredService<IGeoJsonService>().Parse(System.Text.Encoding.UTF8.GetBytes(response.GeoJson), Guid.NewGuid());
                Assert.Equal(5, exported.Count);
            }
            // Fresh repository instance must resolve the same immutable original and metadata.
            var fresh = ActivatorUtilities.CreateInstance<GeoSemantic.Infrastructure.Persistence.FileSystemDatasetRepository>(services);
            Assert.Equal(dataset.Id, (await fresh.GetDatasetAsync(dataset.Id))!.Id);
            Assert.Equal(originalIds, (await fresh.ReadFeaturesAsync(dataset.Id)).Select(f => f.Id).ToArray());
            await datasets.DeleteDatasetAsync(dataset.Id);
            datasetId = null;
            Assert.Null(await repository.GetDatasetAsync(dataset.Id));
            Assert.Equal(0UL, (await vectors.GetCollectionInfoAsync())!.Count);
        }
        finally {
            if (datasetId.HasValue) await datasets.DeleteDatasetAsync(datasetId.Value);
            var client = services.GetRequiredService<QdrantClient>();
            if (await client.CollectionExistsAsync(vectors.CollectionName)) await client.DeleteCollectionAsync(vectors.CollectionName);
            await host.StopAsync();
            // Only this test's generated workspace-owned directory is removed.
            if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, true);
        }
    }
    private static async Task Wait(IndexingCoordinator indexing, Guid id) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        while (indexing.GetProgress(id)?.Finished != true) await Task.Delay(200, timeout.Token);
        Assert.Null(indexing.GetProgress(id)!.Error);
    }
    internal static string FindRoot() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "geo-semantic.csproj"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

