using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetTopologySuite.Geometries;

namespace GeoSemantic.Domain.Entities;

public sealed record GeoFeature(Guid Id, Guid DatasetId, string? SourceFeatureId, Geometry Geometry,
    Dictionary<string, JsonElement> Properties, string SearchableText = "")
{
    public string GeometryType => Geometry.GeometryType;
    public string Name => Property("name") ?? Property("Name") ?? SourceFeatureId ?? Id.ToString("N")[..8];
    public string? Category => Property("category") ?? Property("Category");
    private string? Property(string key) => Properties.TryGetValue(key, out var value) ? value.ToString() : null;

    // The immutable original file's ordinal disambiguates absent or duplicate source identifiers.
    public static Guid StableId(Guid datasetId, int ordinal) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"{datasetId:N}:{ordinal}")).AsSpan(0, 16));
}

public sealed record SemanticSearchResult(Guid FeatureId, Guid DatasetId, double Score, int Rank, GeoFeature Feature)
{
    public static double ValidateScore(double score) =>
        double.IsFinite(score) && score >= -1.00001 && score <= 1.00001
            ? Math.Clamp(score, -1, 1) : throw new ArgumentException("Invalid cosine similarity score.");
}

