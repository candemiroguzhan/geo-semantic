using System.Text;
using System.Text.Json;
using GeoSemantic.Application.Abstractions;
using GeoSemantic.Infrastructure.Configuration;
using GeoSemantic.Infrastructure.Geospatial;
using GeoSemantic.Infrastructure.Persistence;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoSemantic.Infrastructure.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GeoSemantic.Tests", Guid.NewGuid().ToString("N"));
    private FileSystemDatasetRepository Repository(int megabytes = 1) {
        var options = Options.Create(new StorageOptions { DataDirectory = "data", MaxUploadSizeMb = megabytes });
        return new(options, new TestEnvironment(root), new GeoJsonService(options), NullLogger<FileSystemDatasetRepository>.Instance);
    }
    [Fact] public async Task ImportThousandFeaturesPersistsAndSurvivesRepositoryRestart() {
        var repository = Repository();
        var features = Enumerable.Range(0, 1000).Select(i => new {
            type = "Feature", id = "duplicate", geometry = new { type = "Point", coordinates = new[] { 32.8 + i * .00001, 39.9 } },
            properties = new { name = $"Kurgusal yer {i}" }
        });
        using var input = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new { type = "FeatureCollection", features }));
        var dataset = await repository.ImportAsync(input, "../../outside.geojson", "Thousand features");
        Assert.Equal("outside.geojson", dataset.OriginalFileName);
        Assert.Equal(1000, dataset.FeatureCount);
        var original = await repository.ReadFeaturesAsync(dataset.Id);
        Assert.Equal(1000, original.Select(f => f.Id).Distinct().Count());
        var restarted = Repository();
        Assert.Equal(dataset.Id, (await restarted.GetDatasetsAsync()).Single().Id);
        Assert.Equal(original.Select(f => f.Id), (await restarted.ReadFeaturesAsync(dataset.Id)).Select(f => f.Id));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "data"), "original.geojson", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
    }
    [Fact] public async Task UploadSizeLimitIsEnforcedBeforeParsingOrPersistence() {
        var repository = Repository();
        using var input = new MemoryStream(new byte[1024 * 1024 + 1]);
        await Assert.ThrowsAsync<UserOperationException>(() => repository.ImportAsync(input, "large.geojson", null));
        Assert.Empty(await repository.GetDatasetsAsync());
    }
    [Fact] public async Task InvalidImportDoesNotCreateDatasetDirectory() {
        var repository = Repository();
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        await Assert.ThrowsAsync<UserOperationException>(() => repository.ImportAsync(input, "bad.geojson", null));
        Assert.Empty(await repository.GetDatasetsAsync());
    }
    [Fact] public void StorageUnderPublicWebrootIsRejected() {
        var options = Options.Create(new StorageOptions { DataDirectory = "wwwroot/uploads" });
        Assert.Throws<InvalidOperationException>(() => new FileSystemDatasetRepository(options, new TestEnvironment(root),
            new GeoJsonService(options), NullLogger<FileSystemDatasetRepository>.Instance));
    }
    public void Dispose() {
        var ownedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "GeoSemantic.Tests")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(root).StartsWith(ownedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test path.");
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
    private sealed class TestEnvironment(string root) : IHostEnvironment {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "GeoSemantic.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

