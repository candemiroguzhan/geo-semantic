# Architecture

The existing root Blazor project remains the sole presentation application. Moving it under src would add churn to the developer's pending files and launch configuration without changing its responsibilities. The existing .sln is retained; .slnx conversion is unnecessary.

## Dependency direction

~~~text
Domain ← Application ← Infrastructure
            ↑                ↑
            └──── Web ────────┘
~~~

Domain depends on NetTopologySuite for geometry but has no geo-semantic project references. Application depends on Domain and hosting/logging/DI abstractions for a simple in-process coordinator; it does not depend on ASP.NET, Qdrant or Ollama implementations. Infrastructure implements the contracts explicitly. Web composes these layers.

The application uses explicit services, without MediatR, a generic repository framework, CQRS, a message broker or reflection-based dependency discovery.

## Persistence and identity

A server-generated dataset GUID is the only filesystem path component accepted from application operations. Uploaded filenames are display metadata only. Upload length is checked while copying; parsing and feature count have explicit limits. Original files are immutable. Metadata is written to a unique sibling temporary file and atomically replaced.

Feature GUIDs use the first 128 bits of SHA-256(dataset GUID + immutable feature ordinal). This survives restarts and re-indexing, and handles duplicate or missing source feature IDs. Original source IDs are retained separately. Export uses internal IDs and a foreign geosemantic member recording dataset/source identity; export re-import intentionally creates a new dataset identity.

Storage paths cannot be under wwwroot. Configuration is trusted developer input; no arbitrary user filesystem paths are accepted. Backups need both filesystem and Qdrant data.

## Indexing lifecycle

Dataset states: Imported → Indexing → Indexed, or Failed. Failed/Indexed can transition to Indexing. A singleton per-dataset lease excludes indexing and deletion simultaneously. A bounded Channel permits eight queued jobs, processed by one hosted worker; jobs outlive their initiating Razor component. Cancellation is server-observable, not merely a UI flag.

Each batch updates immutable progress snapshots. Selected text is validated before any vector deletion. The target collection is persisted before writing vectors, so failed jobs can be cleaned up. Successful metadata is committed only after all batches complete. Partial/failed datasets are excluded from search. Startup marks interrupted jobs Failed. Retry rebuilds only that dataset's vectors. v0.1 does not retain an older searchable index while rebuilding.

Deletion removes vectors before local files. If Qdrant fails, local metadata remains for retry. A local deletion error after vector deletion leaves local data that can be re-indexed or deleted again. There is no distributed transaction.

## Collections

Collection name: {prefix}_{first16hex(SHA256(exactModelName))}_{dimensions}_cosine.

Collection creation checks existing dimensionality and distance. Model identity and collection are persisted with dataset metadata. The DatasetId keyword payload index supports filtered deletion/retrieval. Stable feature UUID point IDs make upserts idempotent. Rebuild clears the dataset filter before writing, removing obsolete vectors without touching others.

A mutable Ollama model tag may point to changed weights. Treat model tags as immutable for an installation; if changing weights behind the same tag, re-index every dataset or change the configured model/prefix. Automatic digest-based model migration is outside v0.1.

## Workspace and mapping

One scoped state container per Blazor circuit owns current selection, visible layer JSON, query, results and dataset metadata. Three visible layers maximum; original feature objects are only retained for the active dataset and current search results. Persistent originals remain in the repository.

MapLibre is initialized once after render via an isolated JS module. Dataset versions avoid resending unchanged layers. Result and selected layers are separate, rendered above originals. Geometry-type expressions handle multipart points, lines and polygons. A single map click handler resolves stable IDs and builds DOM popups with textContent. ResizeObserver handles inspector/result panel changes. Disposal removes the map, observer, popup and .NET callback.

The workbench has a dedicated layout so existing template routes remain intact. General controls, theme and notifications use MudBlazor. CSS design tokens centralize the dark palette. No user property strings enter raw HTML.

## Constraints

Single application process, trusted local user, bounded in-memory parsing, no persistent job queue, and no offline basemap tile storage. Larger data and multiuser deployment require a different storage/streaming strategy. See roadmap.

