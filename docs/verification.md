# Verification — 30 October 2026

Results below were observed in this Windows workspace, not inferred from compilation.

| Check | Result |
|---|---|
| Debug solution build | Passed, zero warnings/errors |
| Release solution build | Passed, zero warnings/errors |
| Release unit tests | 50 passed; one opt-in integration test skipped as intended |
| Explicit live integration test | Passed against existing Ollama and Qdrant |
| Explicit Edge browser tests | 2 passed |
| HTTP application startup | /explorer returned 200 |
| Working-tree whitespace check | Passed |

## Unit verification

11 Domain tests cover transitions, deterministic IDs and cosine score handling. 12 Application tests cover text ordering/Unicode/scalars, empty input, query validation, ordering/Top-K, conflicting dataset operations, failed deletion and real indexing orchestration with fakes. 27 Infrastructure tests cover six geometry types, invalid geometry/content, serialization, point payloads, collection identity, embeddings, configuration, filesystem persistence and simulated Ollama failures.

The filesystem test imports 1,000 features, checks distinct stable IDs despite duplicate source IDs, and resolves the same metadata and IDs through a fresh repository. This is a bounded import/persistence check, not a claim of a 1,000-feature performance benchmark or live indexing load test.

## Live verification

Ollama's existing qwen3-embedding:0.6b model returned a validated 1024-dimensional vector. Qdrant 1.20.0 accepted official-client gRPC operations.

The isolated live test imported 30 fictional POIs, indexed them, indexed them again, and confirmed the collection still contained exactly 30 points. English walking/green-space retrieval ranked a park first; Turkish healthcare retrieval ranked a hospital first. Results were sorted and exported to parseable GeoJSON. A fresh repository preserved IDs. Deletion removed local metadata and all dataset vectors. The test's own collection and temporary data directory were cleaned.

This does not claim service-process outage testing: Ollama failure/model absence were simulated in unit tests. The user's Ollama process and Qdrant container were not stopped or reconfigured.

## Browser verification

Playwright .NET used the installed Microsoft Edge in headless mode.

1. Uploaded the sample through InputFile, selected the default descriptive fields, built a real index, searched with the real model/Qdrant, inspected ranked results, selected a result, clicked its mapped geometry, opened its popup, downloaded GeoJSON, and deleted the dataset through the UI.
2. Blocked OpenFreeMap requests, verified the blank-map warning/fallback, uploaded Point/MultiPoint/LineString/MultiLineString/Polygon/MultiPolygon geometries, toggled visibility off/on, collapsed the inspector, and deleted the dataset.

Both tests asserted no unhandled browser JavaScript errors. Screenshots were visually inspected, including detailed Ankara streets with visible basemap attribution and the mixed geometries on the blank fallback.

Generated, git-ignored artifacts:

- artifacts/explorer.png
- artifacts/mixed-geometries.png
- artifacts/browser-results.geojson

The separate Browser.Tests project is intentionally outside geo-semantic.sln so ordinary builds/restores do not require the large browser automation package. README documents its explicit command.

## Remaining limits

- Single process against a storage directory; no authentication or public-deployment hardening.
- Full server parsing capped at 50 MB / 10,000 features by default; at most three visible dataset layers.
- In-process indexing is retryable but not resumable after process restart. Previous vectors are replaced during rebuild.
- External basemap resources require internet; there is no offline tile package.
- A model tag is treated as immutable. Changing weights behind the same tag requires explicit re-indexing.
- No agents, generative LLM, spatial retrieval, raster models, or future roadmap capabilities are implemented.

## Changed files

Created three core layer projects, three solution test projects, a separate optional browser test project, feature-oriented Razor components, WorkspaceLayout/ExplorerPage, WorkspaceState, an isolated MapLibre module, design-token CSS, bundled MapLibre assets/license, a 30-feature sample, centralized build/package settings, and documentation.

Modified existing files: geo-semantic.csproj, geo-semantic.sln, Program.cs, appsettings.json, Components/App.razor, Components/_Imports.razor, Components/Pages/Home.razor, and .gitignore. Home redirects to /explorer. The original MainLayout and template pages were retained; their pre-existing staged/unstaged changes were not authored by this implementation.
