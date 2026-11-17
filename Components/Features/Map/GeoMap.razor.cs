using GeoSemantic.Services;
using GeoSemantic.Infrastructure.Configuration;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace GeoSemantic.Components.Features.Map;

public partial class GeoMap : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private WorkspaceState State { get; set; } = default!;
    [Inject] private IOptions<MapOptions> Options { get; set; } = default!;
    [Inject] private ILogger<GeoMap> Logger { get; set; } = default!;
    private ElementReference container;
    private IJSObjectReference? module;
    private IJSObjectReference? map;
    private DotNetObjectReference<GeoMap>? callback;
    private string? warning;
    private long version = -1;
    private string? results;
    private Guid? selected;
    private Guid? active;
    private bool disposed;
    private bool failed;
    protected override void OnInitialized() => State.Changed += OnChanged;
    private void OnChanged() { if (!disposed) _ = InvokeAsync(StateHasChanged); }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (disposed || failed) return;
        try {
            if (firstRender) {
                module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/maplibre/mapManager.js");
                callback = DotNetObjectReference.Create(this);
                map = await module.InvokeAsync<IJSObjectReference>("create", container, Options.Value, callback);
            }
            if (module is null || map is null) return;
            if (version != State.LayerVersion) {
                await module.InvokeVoidAsync("syncDatasets", map, State.MapLayers.Select(p => new { id = p.Key.ToString(), json = p.Value }).ToArray());
                version = State.LayerVersion;
            }
            if (active != State.ActiveDatasetId && State.ActiveDataset is { } dataset) {
                active = dataset.Id;
                await module.InvokeVoidAsync("fitBounds", map, dataset.Bounds);
            }
            if (results != State.Search?.GeoJson) {
                results = State.Search?.GeoJson;
                await module.InvokeVoidAsync("setResults", map, results);
            }
            if (selected != State.SelectedFeature?.Id) {
                selected = State.SelectedFeature?.Id;
                await module.InvokeVoidAsync("setSelection", map, State.SelectedGeoJson);
            }
        }
        catch (JSDisconnectedException) { }
        catch (JSException ex) { failed = true; Logger.LogWarning(ex, "Map operation failed"); warning = "A map operation failed. Reload the workspace; see application logs if it persists."; await InvokeAsync(StateHasChanged); }
    }
    [JSInvokable] public async Task FeatureSelected(string id) {
        if (Guid.TryParse(id, out var featureId)) await InvokeAsync(() => State.ExecuteAsync(() => State.SelectByIdAsync(featureId)));
    }
    [JSInvokable] public Task MapWarning(string message) => InvokeAsync(() => { warning = message; StateHasChanged(); });
    public async ValueTask DisposeAsync() {
        disposed = true; State.Changed -= OnChanged;
        try {
            if (module is not null && map is not null) await module.InvokeVoidAsync("dispose", map);
            if (map is not null) await map.DisposeAsync();
            if (module is not null) await module.DisposeAsync();
        } catch (JSDisconnectedException) { }
        finally { callback?.Dispose(); }
        GC.SuppressFinalize(this);
    }
}

