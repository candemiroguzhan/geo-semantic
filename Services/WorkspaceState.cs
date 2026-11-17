using GeoSemantic.Application.Abstractions;
using GeoSemantic.Application.Features.Datasets;
using GeoSemantic.Application.Features.Indexing;
using GeoSemantic.Application.Features.Search;
using GeoSemantic.Domain.Entities;
using GeoSemantic.Infrastructure.Health;
using Microsoft.JSInterop;

namespace GeoSemantic.Services;

public sealed class WorkspaceState(DatasetService datasets, IDatasetRepository repository, IGeoJsonService geoJson,
    SemanticSearchService search, IndexingCoordinator indexing, ServiceHealthMonitor health, ILogger<WorkspaceState> logger)
{
    public event Action? Changed;
    public IReadOnlyList<GeoDataset> Datasets { get; private set; } = [];
    public Guid? ActiveDatasetId { get; private set; }
    public GeoDataset? ActiveDataset => Datasets.FirstOrDefault(d => d.Id == ActiveDatasetId);
    public HashSet<Guid> VisibleDatasets { get; } = [];
    public IReadOnlyList<GeoFeature> ActiveFeatures { get; private set; } = [];
    public Dictionary<Guid, string> MapLayers { get; } = [];
    public SearchResponse? Search { get; private set; }
    public GeoFeature? SelectedFeature { get; private set; }
    public string Query { get; set; } = "";
    public int TopK { get; set; } = 10;
    public Guid? SearchDatasetId { get; set; }
    public bool Busy { get; private set; }
    public string? Notice { get; private set; }
    public bool IsError { get; private set; }
    public PlatformHealth? Health { get; private set; }
    public IndexingProgress? Progress => ActiveDatasetId.HasValue ? indexing.GetProgress(ActiveDatasetId.Value) : null;
    public long LayerVersion { get; private set; }
    public void Notify() => Changed?.Invoke();

    public async Task ExecuteAsync(Func<Task> action)
    {
        if (Busy) return;
        Busy = true; Notice = null; Notify();
        try { await action(); }
        catch (Exception ex) {
            logger.LogWarning(ex, "Workspace operation failed");
            IsError = true;
            Notice = ex is UserOperationException or ArgumentException ? ex.Message :
                ex is OperationCanceledException ? "Operation cancelled or timed out. Check service availability and retry." :
                "The operation failed. Check service availability and the application logs.";
        }
        finally { Busy = false; Notify(); }
    }
    public void ClearNotice() { Notice = null; Notify(); }
    public async Task InitializeAsync()
    {
        await RefreshAsync();
        await CheckHealthAsync();
    }
    public async Task RefreshAsync()
    {
        Datasets = await datasets.GetDatasetsAsync();
        if (ActiveDatasetId is null && Datasets.Count > 0) await ActivateAsync(Datasets[0].Id);
        Notify();
    }
    public async Task CheckHealthAsync(bool force = false)
    {
        Health = await health.CheckAsync(force);
        Notify();
    }
    public async Task ActivateAsync(Guid id)
    {
        var features = await repository.ReadFeaturesAsync(id);
        ActiveDatasetId = id; ActiveFeatures = features;
        await SetVisibilityAsync(id, true, features);
        Notify();
    }
    public async Task SetVisibilityAsync(Guid id, bool visible, IReadOnlyList<GeoFeature>? features = null)
    {
        // Limit browser layers to three bounded datasets to avoid accumulating unbounded client data.
        if (visible && !MapLayers.ContainsKey(id))
        {
            if (MapLayers.Count >= 3) {
                var oldest = MapLayers.Keys.First();
                MapLayers.Remove(oldest); VisibleDatasets.Remove(oldest);
            }
            MapLayers[id] = geoJson.Serialize(features ?? await repository.ReadFeaturesAsync(id));
        }
        if (visible) VisibleDatasets.Add(id);
        else { VisibleDatasets.Remove(id); MapLayers.Remove(id); }
        LayerVersion++; Notify();
    }
    public async Task ImportAsync(Stream input, string fileName)
    {
        var dataset = await datasets.ImportDatasetAsync(input, fileName, null);
        await RefreshAsync(); await ActivateAsync(dataset.Id);
        IsError = false; Notice = $"Imported {dataset.FeatureCount:N0} validated features."; Notify();
    }
    public void StartIndexing(IEnumerable<string> fields)
    {
        if (ActiveDatasetId is null) return;
        indexing.Start(ActiveDatasetId.Value, fields);
        Datasets = Datasets.Select(d => d.Id == ActiveDatasetId ? d with { Status = DatasetStatus.Indexing } : d).ToArray();
        Search = null; SelectedFeature = null; Notify();
    }
    public void CancelIndexing() { if (ActiveDatasetId.HasValue) indexing.Cancel(ActiveDatasetId.Value); }
    public async Task SearchAsync(CancellationToken ct)
    {
        logger.LogInformation("Semantic search requested for dataset {DatasetId}, TopK {TopK}, query length {QueryLength}", SearchDatasetId, TopK, Query.Length);
        Search = await search.SearchAsync(new(Query, SearchDatasetId, TopK), ct);
        SelectedFeature = null; Notify();
    }
    public void Select(GeoFeature feature) { SelectedFeature = feature; Notify(); }
    public async Task SelectByIdAsync(Guid id)
    {
        var feature = ActiveFeatures.FirstOrDefault(f => f.Id == id) ?? Search?.Results.FirstOrDefault(r => r.FeatureId == id)?.Feature;
        if (feature is null)
            foreach (var datasetId in VisibleDatasets.Where(d => d != ActiveDatasetId))
            {
                feature = (await repository.ReadFeaturesAsync(datasetId)).FirstOrDefault(f => f.Id == id);
                if (feature is not null) break;
            }
        if (feature is not null) Select(feature);
    }
    public string SelectedGeoJson => geoJson.Serialize(SelectedFeature is null ? [] : [SelectedFeature]);
    public async Task DeleteAsync(Guid id)
    {
        await datasets.DeleteDatasetAsync(id);
        VisibleDatasets.Remove(id); MapLayers.Remove(id); LayerVersion++;
        Search = null; SelectedFeature = null;
        if (ActiveDatasetId == id) { ActiveDatasetId = null; ActiveFeatures = []; }
        await RefreshAsync();
    }
}

