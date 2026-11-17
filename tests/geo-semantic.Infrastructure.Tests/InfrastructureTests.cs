using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using GeoSemantic.Application.Abstractions;
using GeoSemantic.Infrastructure.AI;
using GeoSemantic.Infrastructure.Configuration;
using GeoSemantic.Infrastructure.Geospatial;
using GeoSemantic.Infrastructure.VectorStorage;
using Microsoft.Extensions.Options;

namespace GeoSemantic.Infrastructure.Tests;
public sealed class InfrastructureTests
{
    private static readonly GeoJsonService GeoJson = new(Options.Create(new StorageOptions()));
    private static byte[] Collection(string geometry, string properties = "{}") =>
        Encoding.UTF8.GetBytes($$"""{"type":"FeatureCollection","features":[{"type":"Feature","geometry":{{geometry}},"properties":{{properties}}}]}""");
    [Theory]
    [InlineData("""{"type":"Point","coordinates":[32.85,39.93]}""")]
    [InlineData("""{"type":"MultiPoint","coordinates":[[32,39],[33,40]]}""")]
    [InlineData("""{"type":"LineString","coordinates":[[32,39],[33,40]]}""")]
    [InlineData("""{"type":"MultiLineString","coordinates":[[[32,39],[33,40]]]}""")]
    [InlineData("""{"type":"Polygon","coordinates":[[[32,39],[33,39],[33,40],[32,39]]]}""")]
    [InlineData("""{"type":"MultiPolygon","coordinates":[[[[32,39],[33,39],[33,40],[32,39]]]]}""")]
    public void AllSupportedGeometriesRoundTrip(string geometry) {
        var id = Guid.NewGuid();
        var original = GeoJson.Parse(Collection(geometry), id).Single();
        var exported = GeoJson.Serialize([original]);
        var restored = GeoJson.Parse(Encoding.UTF8.GetBytes(exported), id).Single();
        Assert.True(original.Geometry.EqualsExact(restored.Geometry));
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(4326, restored.Geometry.SRID);
    }
    [Theory]
    [InlineData("""{"type":"Point","coordinates":[300,40]}""")]
    [InlineData("""{"type":"Point","coordinates":[32,100]}""")]
    [InlineData("""{"type":"Polygon","coordinates":[[[0,0],[2,2],[0,2],[2,0],[0,0]]]}""")]
    [InlineData("""{"type":"GeometryCollection","geometries":[]}""")]
    [InlineData("null")]
    public void InvalidGeometryIsRejected(string geometry) => Assert.Throws<UserOperationException>(() => GeoJson.Parse(Collection(geometry), Guid.NewGuid()));
    [Theory][InlineData("{}")][InlineData("{bad")][InlineData("""{"type":"FeatureCollection","features":[]}""")]
    public void InvalidCollectionsAreRejected(string json) => Assert.Throws<UserOperationException>(() => GeoJson.Parse(Encoding.UTF8.GetBytes(json), Guid.NewGuid()));
    [Fact] public void UnicodeAndNestedPropertiesRoundTrip() {
        var id = Guid.NewGuid();
        var feature = GeoJson.Parse(Collection("""{"type":"Point","coordinates":[32,39]}""", """{"name":"Yeşil çınar","nested":{"x":true}}"""), id).Single();
        Assert.Equal("Yeşil çınar", feature.Name);
        var output = GeoJson.Parse(Encoding.UTF8.GetBytes(GeoJson.Serialize([feature])), id).Single();
        Assert.Equal(feature.Properties["nested"].GetRawText(), output.Properties["nested"].GetRawText());
    }
    [Fact] public void QdrantMappingHasStableIdAndRequiredPayload() {
        var f = GeoJson.Parse(Collection("""{"type":"Point","coordinates":[32,39]}""", """{"name":"Park","category":"Green"}"""), Guid.NewGuid()).Single();
        var point = QdrantVectorStore.MapPoint(new(f with { SearchableText = "Name: Park." }, [1, 0]));
        Assert.Equal(f.Id.ToString(), point.Id.Uuid);
        Assert.Equal(f.DatasetId.ToString(), point.Payload["DatasetId"].StringValue);
        Assert.Equal("Green", point.Payload["Category"].StringValue);
        Assert.Equal("Name: Park.", point.Payload["SearchableText"].StringValue);
    }
    [Fact] public void CollectionsSeparateModelsAndDimensions() {
        var name = QdrantVectorStore.BuildCollectionName("geo", "qwen3-embedding:0.6b", 1024);
        Assert.Equal(name, QdrantVectorStore.BuildCollectionName("geo", "qwen3-embedding:0.6b", 1024));
        Assert.NotEqual(name, QdrantVectorStore.BuildCollectionName("geo", "different", 1024));
        Assert.NotEqual(name, QdrantVectorStore.BuildCollectionName("geo", "qwen3-embedding:0.6b", 512));
    }
    [Fact] public void EmbeddingsRejectWrongShapeNonfiniteAndZero() {
        Assert.Throws<UserOperationException>(() => OllamaEmbeddingService.ValidateResponse([[1]], 1, 1024));
        Assert.Throws<UserOperationException>(() => OllamaEmbeddingService.ValidateResponse([[float.NaN, 0]], 1, 2));
        Assert.Throws<UserOperationException>(() => OllamaEmbeddingService.ValidateResponse([[0, 0]], 1, 2));
        Assert.Throws<UserOperationException>(() => OllamaEmbeddingService.ValidateResponse([], 1, 2));
        OllamaEmbeddingService.ValidateResponse([[1, 0]], 1, 2);
    }
    [Fact] public void ConfigurationValidationRejectsUnsafeValues() {
        var invalid = new OllamaOptions { EmbeddingDimensions = 0, BaseUrl = "bad" };
        Assert.False(Validator.TryValidateObject(invalid, new ValidationContext(invalid), [], true));
        var qdrant = new QdrantOptions { CollectionPrefix = "../bad", GrpcPort = 0 };
        Assert.False(Validator.TryValidateObject(qdrant, new ValidationContext(qdrant), [], true));
        var valid = new OllamaOptions();
        Assert.True(Validator.TryValidateObject(valid, new ValidationContext(valid), [], true));
    }
}

