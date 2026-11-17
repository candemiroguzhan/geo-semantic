# geo-semantic v0.1

A local-first geospatial semantic search workbench built on the repository's existing .NET 10 Interactive Server Blazor application. Import WGS84 GeoJSON, select descriptive attributes, generate real Ollama embeddings, index them in Qdrant, and explore ranked geographic results in MapLibre.

The existing frontend remains **geo-semantic.csproj at the repository root**. There is no second frontend. The workspace is at **/explorer**; / redirects there. Original Counter and Weather routes and their layout remain available.

## Features

- Validated FeatureCollection import, atomic filesystem metadata, restart-safe feature identities.
- Points, lines, polygons and multipart geometries; bounds, attribute and geometry statistics.
- Property selection, deterministic Unicode text, batching, cancellation and observable indexing.
- Real bilingual semantic retrieval with model-compatible collections and dataset filtering.
- Map visibility, selection, highlighting, popups, focus and ranked result inspection.
- GeoJSON export, measured AI pipeline metrics and cached service health checks.
- Dark MudBlazor GIS workbench; collapsible inspector and resizable/collapsible result panel.
- Explicit feature, upload and visible-layer limits for predictable MVP memory use.

## Stack and architecture

.NET SDK 10.0.401 / net10.0, Blazor Interactive Server, MudBlazor, NetTopologySuite, System.Text.Json, Ollama HTTP, official Qdrant.Client over gRPC, MapLibre GL JS 5.24.0 and Serilog.

- src/geo-semantic.Domain: geometry-bearing entities, state transitions, stable IDs.
- src/geo-semantic.Application: contracts, dataset operations, indexing orchestration, text generation, search.
- src/geo-semantic.Infrastructure: filesystem, GeoJSON validation, Ollama, Qdrant, options, health.
- geo-semantic.csproj: existing web presentation and composition root.
- tests/: three xUnit projects. External-service and browser tests are opt-in.

See [architecture](docs/architecture.md), [AI pipeline](docs/ai-pipeline.md), [development](docs/development.md) and [roadmap](docs/roadmap.md).

## Local prerequisites

Windows 11 and .NET SDK 10.0.401. The expected services are already installed:

- Ollama: http://localhost:11434; installed model **qwen3-embedding:0.6b**, **1024 dimensions**.
- Existing Docker container **geosemantic-qdrant**: HTTP localhost:6333, gRPC localhost:6334.

Do not create another container or download another model. No generative model is used. Health checks never manage containers or models.

PowerShell connectivity checks:

~~~powershell
Invoke-RestMethod http://localhost:11434/api/tags
Invoke-RestMethod http://localhost:6333/
~~~

## Build and run

From this repository directory:

~~~powershell
dotnet restore geo-semantic.sln
dotnet build geo-semantic.sln --no-restore
dotnet test geo-semantic.sln --no-restore
dotnet run --project .\geo-semantic.csproj --launch-profile http
~~~

Open http://localhost:5287/explorer. Import your GeoJSON or click **Import fictional Ankara sample**. Choose fields, build the semantic index, then search. The 30 sample places are entirely fictional and are labeled as such.

Example queries:

- Find green recreational spaces for walking.
- Show healthcare facilities.
- Find educational and research institutions.
- İnsanların yürüyüş yapabileceği yeşil alanları göster.
- Sanayi ve üretim tesislerini bul.

Select a ranked result to focus its geometry. Export GeoJSON from the result panel.

## Configuration

All settings are in appsettings.json, with validated options and standard ASP.NET environment overrides. For example:

~~~powershell
$env:Ollama__BaseUrl = 'http://localhost:11434'
$env:Storage__DataDirectory = 'AppData'
dotnet run --project .\geo-semantic.csproj --launch-profile http
~~~

Defaults: batches of 16, 120-second embedding timeout, 50 MB upload limit, 10,000 features per dataset, Top-K 10 (maximum 50), Ankara initial extent. At most three datasets are sent to the map at once. Activating a fourth replaces the oldest visible layer, without deleting its dataset.

Datasets live in AppData/datasets/{serverGeneratedId}/original.geojson and metadata.json, outside wwwroot. Back up the full directory and Qdrant storage together. Never edit original files in place: internal IDs depend on immutable feature order. Use a single application process against a data directory.

MapLibre's runtime and license are bundled locally. The configured development basemap is https://tiles.openfreemap.org/styles/dark, with its style-provided attribution. It requires internet access and is a development service, not an offline tile package or a production SLA. On failure, a blank geographic canvas remains available.

The style endpoint is documented by [OpenFreeMap](https://github.com/hyperknot/openfreemap-styles). MapLibre's attribution control retains the OpenFreeMap, OpenMapTiles and OpenStreetMap credits.

## Verification

~~~powershell
# Unit tests: no Ollama, Qdrant or browser needed
dotnet test geo-semantic.sln

# Explicit live integration test, using isolated temporary datasets and a test collection
$env:GEOSEMANTIC_LIVE_TESTS = '1'
dotnet test .\tests\geo-semantic.Infrastructure.Tests --filter 'Category=Integration'
Remove-Item Env:GEOSEMANTIC_LIVE_TESTS

# Browser verification requires Microsoft Edge, running app, and live services.
# Use a fresh test storage directory when running the app.
$env:Storage__DataDirectory = 'artifacts/browser-data'
dotnet run --project .\geo-semantic.csproj --no-launch-profile --urls http://localhost:5198
# In a second PowerShell:
$env:GEOSEMANTIC_BROWSER_TESTS = '1'
dotnet test .\tests\geo-semantic.Browser.Tests --filter 'Category=Browser'
Remove-Item Env:GEOSEMANTIC_BROWSER_TESTS
~~~

Browser verification writes artifacts/explorer.png and artifacts/browser-results.geojson. See [verification report](docs/verification.md) for results actually observed during implementation.

## Troubleshooting

- **Ollama offline:** start the existing Ollama installation, verify the endpoint, then click Refresh under Local services. Dataset browsing still works.
- **Required model unavailable:** verify api/tags contains the configured model exactly. The app never downloads or substitutes a model.
- **Wrong vector dimensions:** verify model configuration. Embedding responses are validated at runtime; incompatible vectors are rejected.
- **Qdrant offline:** start the existing geosemantic-qdrant container and check localhost:6334. HTTP reachability alone does not prove gRPC works.
- **Invalid geometry:** the import error identifies a feature number. Repair the source geometry or export WGS84 GeoJSON; the app never silently repairs or reprojects.
- **Failed indexing:** select the dataset and retry. Partial vectors are excluded from retrieval. Re-indexing replaces only that dataset's vectors.
- **Delete failure:** retain the dataset, restore Qdrant connectivity and retry. Local files are deliberately retained if vector deletion fails.
- **Interrupted job:** startup marks persisted Indexing datasets Failed. Restart indexing explicitly.
- **Blank map:** check the basemap warning, internet access and WebGL availability. Local feature layers work without a basemap.
- **HTTPS certificate warning:** use the http launch profile for local development, or configure a trusted development certificate.
- **Restore problems:** check access to api.nuget.org and the installed .NET SDK version.

## Deployment scope

v0.1 is intended for a trusted local developer, bound to loopback. It has no authentication. Do not publish it to a LAN or the internet without authentication/authorization, TLS, request limits, tenant/data isolation, protected storage, deployment-specific basemap terms, and a separate security review. No arbitrary filesystem, Docker administration, or model management operations are exposed.

Future semantic-spatial search, agents, raster models and EarthLens integration are documented, not implemented.

