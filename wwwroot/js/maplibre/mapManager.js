const empty = () => ({ type: "FeatureCollection", features: [] });
const blank = () => ({ version: 8, sources: {}, layers: [{ id: "background", type: "background", paint: { "background-color": "#1d1e1b" } }] });

export async function create(element, options, dotnet) {
    if (!globalThis.maplibregl) throw new Error("MapLibre assets unavailable");
    const ctx = { map: null, datasets: new Map(), dotnet, popup: null, disposed: false, observer: null, warned: false };
    let style;
    try {
        const response = await fetch(options.styleUrl, { signal: AbortSignal.timeout(8000) });
        if (!response.ok) throw new Error("Basemap request failed");
        style = await response.json();
        // Retain relative URL resolution by passing the original URL when reachable.
        style = options.styleUrl;
    } catch {
        style = blank();
        await dotnet.invokeMethodAsync("MapWarning", "Basemap unavailable. Your data remains visible on a blank map.");
    }
    const map = ctx.map = new maplibregl.Map({
        container: element, style, center: [options.defaultLongitude, options.defaultLatitude],
        zoom: options.defaultZoom, attributionControl: false
    });
    map.addControl(new maplibregl.NavigationControl(), "top-right");
    map.addControl(new maplibregl.ScaleControl({ unit: "metric" }), "bottom-left");
    map.addControl(new maplibregl.AttributionControl({ compact: false }), "bottom-right");
    map.on("idle", () => { element.dataset.mapIdle = "true"; });
    map.on("movestart", () => { element.dataset.mapIdle = "false"; });
    // A failed style request must not leave initialization waiting forever.
    await new Promise(resolve => {
        let ready = false;
        const timer = setTimeout(() => {
            if (!ready) {
                map.setStyle(blank());
                dotnet.invokeMethodAsync("MapWarning", "Basemap timed out. Showing your data on a blank map.").catch(() => {});
            }
        }, 10000);
        map.once("style.load", () => { ready = true; clearTimeout(timer); resolve(); });
    });
    map.on("error", e => {
        if (!ctx.disposed && !ctx.warned) {
            ctx.warned = true;
            dotnet.invokeMethodAsync("MapWarning", "Some basemap resources could not load. Geographic layers remain available.").catch(() => {});
        }
    });
    addOverlay(ctx, "search-results", "#e9bf76", 7);
    addOverlay(ctx, "selected-feature", "#d4b9ed", 9);
    map.on("click", e => {
        const layers = [...ctx.datasets.keys(), "search-results"].flatMap(id => layerIds(id));
        const hits = map.queryRenderedFeatures(e.point, { layers: layers.filter(id => map.getLayer(id)) });
        const feature = hits[0];
        if (!feature?.id || ctx.disposed) return;
        dotnet.invokeMethodAsync("FeatureSelected", String(feature.id)).catch(() => {});
        ctx.popup?.remove();
        const content = document.createElement("div");
        const title = document.createElement("strong");
        title.textContent = String(feature.properties?.name ?? feature.properties?.Name ?? "Geographic feature");
        content.appendChild(title);
        const detail = document.createElement("p");
        detail.textContent = String(feature.properties?.category ?? feature.properties?.Category ?? feature.geometry.type);
        content.appendChild(detail);
        ctx.popup = new maplibregl.Popup({ closeButton: true }).setLngLat(e.lngLat).setDOMContent(content).addTo(map);
    });
    map.on("mousemove", e => {
        const layers = [...ctx.datasets.keys(), "search-results"].flatMap(id => layerIds(id)).filter(id => map.getLayer(id));
        map.getCanvas().style.cursor = layers.length && map.queryRenderedFeatures(e.point, { layers }).length ? "pointer" : "";
    });
    ctx.observer = new ResizeObserver(() => map.resize());
    ctx.observer.observe(element);
    element.dataset.mapReady = "true";
    // InvokeAsync<IJSObjectReference> creates the reference; return the actual context.
    return ctx;
}
const layerIds = id => [id + "-fill", id + "-line", id + "-point"];
function addOverlay(ctx, id, color, radius) {
    const map = ctx.map;
    map.addSource(id, { type: "geojson", data: empty() });
    map.addLayer({ id: id + "-fill", type: "fill", source: id, filter: ["==", ["geometry-type"], "Polygon"],
        paint: { "fill-color": color, "fill-opacity": 0.22 } });
    map.addLayer({ id: id + "-line", type: "line", source: id, filter: ["!=", ["geometry-type"], "Point"],
        paint: { "line-color": color, "line-width": id === "selected-feature" ? 4 : 2.5 } });
    map.addLayer({ id: id + "-point", type: "circle", source: id, filter: ["==", ["geometry-type"], "Point"],
        paint: { "circle-color": color, "circle-radius": ["interpolate", ["linear"], ["zoom"], 5, radius / 2, 13, radius],
            "circle-stroke-width": 1.5, "circle-stroke-color": "#191918" } });
}
export function syncDatasets(ctx, layers) {
    const wanted = new Set(layers.map(d => "dataset-" + d.id));
    for (const id of ctx.datasets.keys()) {
        if (wanted.has(id)) continue;
        for (const layer of layerIds(id)) if (ctx.map.getLayer(layer)) ctx.map.removeLayer(layer);
        if (ctx.map.getSource(id)) ctx.map.removeSource(id);
        ctx.datasets.delete(id);
    }
    for (const layer of layers) {
        const id = "dataset-" + layer.id;
        if (!ctx.datasets.has(id)) {
            addOverlay(ctx, id, "#b2c79b", 5);
            ctx.datasets.set(id, layer.json);
            ctx.map.getSource(id).setData(JSON.parse(layer.json));
        } else if (ctx.datasets.get(id) !== layer.json) {
            ctx.map.getSource(id).setData(JSON.parse(layer.json)); ctx.datasets.set(id, layer.json);
        }
    }
    // Result and selection layers always sit above original data.
    for (const id of ["search-results", "selected-feature"])
        for (const layer of layerIds(id)) ctx.map.moveLayer(layer);
}
export function setResults(ctx, json) {
    ctx.map.getSource("search-results").setData(json ? JSON.parse(json) : empty());
}
export function fitBounds(ctx, bounds) {
    if (bounds?.length === 4) ctx.map.fitBounds([[bounds[0], bounds[1]], [bounds[2], bounds[3]]],
        { padding: 65, maxZoom: 15, duration: 650 });
}
export function setSelection(ctx, json) {
    const data = JSON.parse(json);
    ctx.map.getSource("selected-feature").setData(data);
    const bounds = [Infinity, Infinity, -Infinity, -Infinity];
    function visit(coords) {
        if (typeof coords[0] === "number") {
            bounds[0] = Math.min(bounds[0], coords[0]); bounds[1] = Math.min(bounds[1], coords[1]);
            bounds[2] = Math.max(bounds[2], coords[0]); bounds[3] = Math.max(bounds[3], coords[1]);
        } else for (const item of coords) visit(item);
    }
    for (const feature of data.features) visit(feature.geometry.coordinates);
    if (Number.isFinite(bounds[0])) fitBounds(ctx, bounds);
}
export function download(filename, json) {
    const url = URL.createObjectURL(new Blob([json], { type: "application/geo+json;charset=utf-8" }));
    const anchor = document.createElement("a");
    anchor.href = url; anchor.download = filename; anchor.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
export function dispose(ctx) {
    ctx.disposed = true; ctx.observer?.disconnect(); ctx.popup?.remove(); ctx.map.remove();
}

