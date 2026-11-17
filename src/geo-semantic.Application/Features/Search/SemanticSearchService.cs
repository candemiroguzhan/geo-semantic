using System.Diagnostics;
using GeoSemantic.Application.Abstractions;
using GeoSemantic.Domain.Entities;
namespace GeoSemantic.Application.Features.Search;

public sealed class SearchOptions
{
    public int DefaultTopK { get; set; } = 10;
    public int MaxTopK { get; set; } = 50;
}
public sealed record SearchRequest(string QueryText, Guid? DatasetId = null, int TopK = 10)
{
    public string Validate()
    {
        var query = QueryText?.Trim();
        if (string.IsNullOrWhiteSpace(query) || query.Length > 2000)
            throw new UserOperationException("Enter a query between 1 and 2,000 characters.");
        if (DatasetId == Guid.Empty) throw new UserOperationException("Invalid dataset identifier.");
        return query;
    }
}
public sealed record SearchResponse(IReadOnlyList<SemanticSearchResult> Results, string GeoJson,
    TimeSpan SearchDuration, TimeSpan EmbeddingDuration, TimeSpan VectorSearchDuration, string Query, string Model)
{
    public int RetrievedCount => Results.Count;
}

public sealed class SemanticSearchService(IEmbeddingService embeddings, IVectorStore vectors,
    IDatasetRepository repository, IGeoJsonService geoJson, SearchOptions options)
{
    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken ct = default)
    {
        var query = request.Validate();
        var total = Stopwatch.StartNew();
        var model = await embeddings.GetModelInfoAsync(ct);
        var eligible = (await repository.GetDatasetsAsync(ct)).Where(d => d.Status == DatasetStatus.Indexed &&
            d.CollectionName == vectors.CollectionName && d.EmbeddingModel == model.Model && d.EmbeddingDimensions == model.Dimensions &&
            (!request.DatasetId.HasValue || d.Id == request.DatasetId)).ToDictionary(d => d.Id);
        if (eligible.Count == 0) throw new UserOperationException("Index a dataset with the configured model before searching.");
        var watch = Stopwatch.StartNew();
        var vector = await embeddings.GenerateEmbeddingAsync(query, ct);
        var embeddingTime = watch.Elapsed;
        watch.Restart();
        var topK = Math.Clamp(request.TopK, 1, options.MaxTopK);
        var hits = new List<VectorHit>();
        // Scope each retrieval to an indexed dataset. Failed or partial jobs must not crowd out valid results.
        foreach (var id in eligible.Keys)
            hits.AddRange(await vectors.SearchAsync(vector, topK, id, ct));
        var vectorTime = watch.Elapsed;
        var results = new List<SemanticSearchResult>();
        foreach (var group in hits.Where(h => eligible.ContainsKey(h.DatasetId)).GroupBy(h => h.DatasetId))
        {
            var features = (await repository.ReadFeaturesAsync(group.Key, ct)).ToDictionary(f => f.Id);
            foreach (var hit in group)
                if (features.TryGetValue(hit.FeatureId, out var feature))
                    results.Add(new(hit.FeatureId, hit.DatasetId, SemanticSearchResult.ValidateScore(hit.Score), 0, feature));
        }
        var ordered = results.OrderByDescending(r => r.Score).ThenBy(r => r.FeatureId).Take(topK)
            .Select((r, i) => r with { Rank = i + 1 }).ToArray();
        return new(ordered, geoJson.Serialize(ordered.Select(r => r.Feature)), total.Elapsed, embeddingTime, vectorTime, query, model.Model);
    }
}

