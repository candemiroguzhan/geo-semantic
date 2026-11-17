using GeoSemantic.Domain.Entities;
using NetTopologySuite.Geometries;
namespace GeoSemantic.Domain.Tests;

public sealed class DomainTests
{
    private static GeoDataset Dataset() => new() { Id = Guid.NewGuid(), AvailableProperties = ["name", "category"] };
    [Fact] public void IndexingRequiresAvailableFields() {
        Assert.Throws<ArgumentException>(() => Dataset().BeginIndexing([]));
        Assert.Throws<ArgumentException>(() => Dataset().BeginIndexing(["missing"]));
    }
    [Fact] public void StateTransitionsRetainIndexMetadata() {
        var dataset = Dataset().BeginIndexing(["name"]);
        Assert.Equal(DatasetStatus.Indexing, dataset.Status);
        Assert.Throws<InvalidOperationException>(() => dataset.BeginIndexing(["name"]));
        var indexed = dataset.CompleteIndexing("model", 1024, "collection");
        Assert.Equal(DatasetStatus.Indexed, indexed.Status);
        Assert.NotNull(indexed.IndexedAt);
        Assert.Equal(1024, indexed.EmbeddingDimensions);
        Assert.Equal("collection", indexed.CollectionName);
        var failed = indexed.FailIndexing("retry");
        Assert.Equal(DatasetStatus.Failed, failed.Status);
        Assert.Equal("collection", failed.CollectionName);
    }
    [Fact] public void CannotCompleteUnstartedIndexing() =>
        Assert.Throws<InvalidOperationException>(() => Dataset().CompleteIndexing("model", 1024, "collection"));
    [Fact] public void IdentityIsStableAndDatasetScoped() {
        var id = Guid.NewGuid();
        Assert.Equal(GeoFeature.StableId(id, 1), GeoFeature.StableId(id, 1));
        Assert.NotEqual(GeoFeature.StableId(id, 1), GeoFeature.StableId(id, 2));
        Assert.NotEqual(GeoFeature.StableId(id, 1), GeoFeature.StableId(Guid.NewGuid(), 1));
    }
    [Theory]
    [InlineData(-0.7)][InlineData(0)][InlineData(0.8)][InlineData(1)]
    public void SimilarityScoresAreNotProbabilities(double score) => Assert.Equal(score, SemanticSearchResult.ValidateScore(score));
    [Theory]
    [InlineData(double.NaN)][InlineData(double.PositiveInfinity)][InlineData(1.1)]
    public void InvalidScoresAreRejected(double score) => Assert.Throws<ArgumentException>(() => SemanticSearchResult.ValidateScore(score));
}

