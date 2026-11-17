using System.Text.Json;
using GeoSemantic.Application.Abstractions;
using GeoSemantic.Domain.Entities;
using GeoSemantic.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;

namespace GeoSemantic.Infrastructure.Persistence;

public sealed class FileSystemDatasetRepository : IDatasetRepository
{
    private readonly string root;
    private readonly long maxBytes;
    private readonly IGeoJsonService geoJson;
    private readonly ILogger<FileSystemDatasetRepository> logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public FileSystemDatasetRepository(IOptions<StorageOptions> options, IHostEnvironment environment, IGeoJsonService geoJson,
        ILogger<FileSystemDatasetRepository> logger)
    {
        root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.DataDirectory, "datasets"));
        var webRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "wwwroot"));
        if (root.Equals(webRoot, StringComparison.OrdinalIgnoreCase) || root.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Dataset storage must be outside wwwroot.");
        this.geoJson = geoJson; this.logger = logger;
        maxBytes = options.Value.MaxUploadSizeMb * 1024L * 1024;
        Directory.CreateDirectory(root);
    }
    private string Folder(Guid id) => id == Guid.Empty ? throw new UserOperationException("Invalid dataset identifier.") : Path.Combine(root, id.ToString("N"));
    private string Metadata(Guid id) => Path.Combine(Folder(id), "metadata.json");

    public async Task<GeoDataset> ImportAsync(Stream input, string originalFileName, string? name, CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + count > maxBytes) throw new UserOperationException($"Upload exceeds the {maxBytes / 1024 / 1024} MB limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, count), ct);
        }
        IReadOnlyList<GeoFeature> features;
        try { features = geoJson.Parse(buffer.ToArray(), id); }
        catch (UserOperationException ex) { logger.LogWarning("Dataset validation rejected: {Reason}", ex.Message); throw; }
        var filename = Path.GetFileName(originalFileName.Replace('\\', '/'));
        var displayName = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(filename) : name.Trim();
        if (displayName.Length is 0 or > 120) throw new UserOperationException("Dataset name must be 1–120 characters.");
        var envelope = new Envelope();
        foreach (var feature in features) envelope.ExpandToInclude(feature.Geometry.EnvelopeInternal);
        var dataset = new GeoDataset {
            Id = id, Name = displayName, OriginalFileName = filename[..Math.Min(filename.Length, 255)], FeatureCount = features.Count,
            AvailableProperties = features.SelectMany(f => f.Properties.Keys).Distinct().Order(StringComparer.Ordinal).ToArray(),
            GeometryTypes = features.GroupBy(f => f.GeometryType).ToDictionary(g => g.Key, g => g.Count()),
            Bounds = [envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY]
        };
        Directory.CreateDirectory(Folder(id));
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(Folder(id), "original.geojson"), buffer.ToArray(), ct);
            await SaveAsync(dataset, ct);
        }
        catch { Directory.Delete(Folder(id), true); throw; }
        logger.LogInformation("Imported dataset {DatasetId} with {FeatureCount} features", id, features.Count);
        return dataset;
    }
    public async Task<IReadOnlyList<GeoDataset>> GetDatasetsAsync(CancellationToken ct = default)
    {
        var result = new List<GeoDataset>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id)) continue;
            try { if (await GetDatasetAsync(id, ct) is { } dataset) result.Add(dataset); }
            catch (Exception ex) when (ex is JsonException or IOException) { logger.LogError(ex, "Cannot read dataset metadata {DatasetId}", id); }
        }
        return result.OrderByDescending(d => d.CreatedAt).ToArray();
    }
    public async Task<GeoDataset?> GetDatasetAsync(Guid id, CancellationToken ct = default)
    {
        var path = Metadata(id);
        if (!File.Exists(path)) return null;
        var dataset = JsonSerializer.Deserialize<GeoDataset>(await File.ReadAllTextAsync(path, ct), JsonOptions);
        if (dataset?.Id != id) throw new UserOperationException("Dataset metadata is invalid.");
        return dataset;
    }
    public async Task<IReadOnlyList<GeoFeature>> ReadFeaturesAsync(Guid id, CancellationToken ct = default)
    {
        if (!File.Exists(Metadata(id))) throw new UserOperationException("Dataset was not found.");
        return geoJson.Parse(await File.ReadAllBytesAsync(Path.Combine(Folder(id), "original.geojson"), ct), id);
    }
    public async Task SaveAsync(GeoDataset dataset, CancellationToken ct = default)
    {
        var path = Metadata(dataset.Id);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(dataset, JsonOptions), ct);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Directory.Delete(Folder(id), true);
        logger.LogInformation("Deleted local dataset {DatasetId}", id);
        return Task.CompletedTask;
    }
}

