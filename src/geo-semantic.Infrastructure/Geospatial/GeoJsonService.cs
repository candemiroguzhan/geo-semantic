using System.Text.Json;
using GeoSemantic.Application.Abstractions;
using GeoSemantic.Domain.Entities;
using GeoSemantic.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;
using NetTopologySuite.Operation.Valid;

namespace GeoSemantic.Infrastructure.Geospatial;

public sealed class GeoJsonService(IOptions<StorageOptions> options) : IGeoJsonService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new GeoJsonConverterFactory());
        return options;
    }
    public IReadOnlyList<GeoFeature> Parse(ReadOnlyMemory<byte> json, Guid datasetId)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.GetString() != "FeatureCollection" ||
                !root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
                throw new UserOperationException("Upload a GeoJSON FeatureCollection with a features array.");
            if (root.TryGetProperty("crs", out _)) throw new UserOperationException("Remove the legacy CRS declaration and export WGS84 longitude/latitude GeoJSON.");
            if (features.GetArrayLength() == 0) throw new UserOperationException("The dataset contains no features.");
            if (features.GetArrayLength() > options.Value.MaxFeatures)
                throw new UserOperationException($"The dataset exceeds the {options.Value.MaxFeatures:N0} feature limit.");
            var result = new List<GeoFeature>();
            foreach (var feature in features.EnumerateArray())
            {
                var ordinal = result.Count;
                try
                {
                    if (feature.GetProperty("type").GetString() != "Feature") throw new UserOperationException("Expected a GeoJSON Feature.");
                    var geometryJson = feature.GetProperty("geometry");
                    var geometryType = geometryJson.GetProperty("type").GetString();
                    if (geometryType is not ("Point" or "MultiPoint" or "LineString" or "MultiLineString" or "Polygon" or "MultiPolygon"))
                        throw new UserOperationException("Supported geometries: Point, MultiPoint, LineString, MultiLineString, Polygon, MultiPolygon.");
                    var geometry = JsonSerializer.Deserialize<Geometry>(geometryJson, JsonOptions) ?? throw new UserOperationException("Geometry is missing.");
                    geometry.SRID = 4326;
                    if (geometry.IsEmpty) throw new UserOperationException("Geometry must not be empty.");
                    foreach (var c in geometry.Coordinates)
                        if (!double.IsFinite(c.X) || !double.IsFinite(c.Y) || c.X < -180 || c.X > 180 || c.Y < -90 || c.Y > 90)
                            throw new UserOperationException("Coordinates must be WGS84 longitude [-180,180] and latitude [-90,90]. Reproject the source data before importing.");
                    var validation = new IsValidOp(geometry);
                    if (!validation.IsValid) throw new UserOperationException($"Invalid geometry: {validation.ValidationError.Message}. Repair the source geometry before importing.");
                    var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    if (feature.TryGetProperty("properties", out var props) && props.ValueKind != JsonValueKind.Null)
                    {
                        if (props.ValueKind != JsonValueKind.Object) throw new UserOperationException("Properties must be an object or null.");
                        foreach (var prop in props.EnumerateObject())
                        {
                            if (string.IsNullOrWhiteSpace(prop.Name) || prop.Name.Length > 256) throw new UserOperationException("Property names must be 1–256 characters.");
                            if (!properties.TryAdd(prop.Name, prop.Value.Clone())) throw new UserOperationException("Duplicate property names are not supported.");
                        }
                    }
                    var sourceId = feature.TryGetProperty("id", out var id) ? id.ToString() : null;
                    result.Add(new(GeoFeature.StableId(datasetId, ordinal), datasetId, sourceId, geometry, properties));
                }
                catch (Exception ex) when (ex is UserOperationException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
                { throw new UserOperationException($"Feature {ordinal + 1}: {(ex is UserOperationException ? ex.Message : "Malformed feature or geometry. Check type, geometry and coordinates.")}", ex); }
            }
            return result;
        }
        catch (JsonException ex) { throw new UserOperationException($"Invalid GeoJSON syntax near line {ex.LineNumber + 1}.", ex); }
    }

    public string Serialize(IEnumerable<GeoFeature> features) => JsonSerializer.Serialize(new {
        type = "FeatureCollection",
        features = features.Select(f => new {
            type = "Feature", id = f.Id.ToString(), geometry = JsonSerializer.SerializeToElement(f.Geometry, JsonOptions),
            properties = f.Properties, geosemantic = new { featureId = f.Id, datasetId = f.DatasetId, sourceFeatureId = f.SourceFeatureId }
        })
    });
}

