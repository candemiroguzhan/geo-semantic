namespace GeoSemantic.Domain.Entities;

public enum DatasetStatus { Imported, Indexing, Indexed, Failed }

public sealed record GeoDataset
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string OriginalFileName { get; init; } = "";
    public int FeatureCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? IndexedAt { get; init; }
    public DatasetStatus Status { get; init; }
    public string? EmbeddingModel { get; init; }
    public int? EmbeddingDimensions { get; init; }
    public string? CollectionName { get; init; }
    public IReadOnlyList<string> SelectedProperties { get; init; } = [];
    public IReadOnlyList<string> AvailableProperties { get; init; } = [];
    public Dictionary<string, int> GeometryTypes { get; init; } = [];
    public double[] Bounds { get; init; } = [];
    public string? Error { get; init; }

    public GeoDataset BeginIndexing(IReadOnlyList<string> fields)
    {
        if (Status == DatasetStatus.Indexing) throw new InvalidOperationException("Dataset is already indexing.");
        if (fields.Count == 0 || fields.Any(f => !AvailableProperties.Contains(f)))
            throw new ArgumentException("Select one or more available properties.");
        return this with { Status = DatasetStatus.Indexing, SelectedProperties = fields.Distinct().Order(StringComparer.Ordinal).ToArray(), Error = null, IndexedAt = null };
    }

    public GeoDataset CompleteIndexing(string model, int dimensions, string collection) =>
        Status != DatasetStatus.Indexing ? throw new InvalidOperationException("Dataset is not indexing.") :
        this with { Status = DatasetStatus.Indexed, EmbeddingModel = model, EmbeddingDimensions = dimensions, CollectionName = collection, IndexedAt = DateTimeOffset.UtcNow, Error = null };

    public GeoDataset FailIndexing(string error) => this with { Status = DatasetStatus.Failed, Error = error, IndexedAt = null };
}

