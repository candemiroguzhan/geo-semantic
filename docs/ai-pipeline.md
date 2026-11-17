# AI pipeline

## Indexing

1. Load and validate immutable GeoJSON features.
2. Sort selected property names ordinally and remove duplicates.
3. Ignore null, empty, array and object values; preserve scalar Unicode text.
4. Produce labeled sentences, with no geometry coordinates.
5. Reject empty feature text and oversized selected text before mutating vectors.
6. Validate collection dimensions and cosine distance.
7. Persist selected properties, model, dimensions, collection and Indexing status.
8. Remove the dataset's previous vectors from its recorded collection as necessary.
9. POST batches of 16 to Ollama /api/embed with the exact configured model and truncate=false.
10. Validate returned model, vector count, dimensionality, finite values and nonzero magnitude.
11. Upsert stable UUID points with FeatureId, DatasetId, GeometryType, Name, Category and SearchableText.
12. Commit Indexed status and timestamp after every batch succeeds.

HTTP clients use IHttpClientFactory with configurable timeouts. gRPC uses the official Qdrant .NET client with a 30-second request deadline. Cancellation is propagated to I/O.

## Retrieval

Trim/validate a 1–2,000-character query, clamp Top-K, and identify Indexed datasets matching configured model, dimensions and collection. Generate one real query embedding. Retrieve up to Top-K candidates **per eligible dataset with a DatasetId filter**, merge by descending cosine similarity, and take global Top-K. This avoids partial vectors from failed jobs crowding out valid results.

Resolve each hit's stable ID against its immutable local original. Export the resolved features as a GeoJSON FeatureCollection, and display them in ranked order. Turkish and English use the same embedding model. There is no keyword fallback, generative answer, prompt-based planner, or fabricated result.

Cosine scores may be negative. They are similarity values, not confidence probabilities or percentages.

## Observability

Index snapshots record actual completed batches/features, model/dimensions, accumulated embedding duration, accumulated Qdrant upsert duration, total elapsed time, current stage, and safe error state.

Search responses measure query embedding time, aggregate vector retrieval time, total duration and returned result count. Total also includes metadata loading, feature resolution and serialization overhead. The UI displays these real measurements without invented sample metrics.

Structured logs record operation IDs, batch sizes, durations, collection names and failures. Uploaded file contents and vector arrays are never logged. The UI shows user-actionable messages; detailed exceptions stay server-side.

## Health

Read-only Ollama model-list and Qdrant gRPC checks run independently of startup, with five-second health timeouts. Results are cached for 30 seconds across circuits; manual Refresh is available. No background aggressive polling or model/container administration is implemented.

The model list establishes required model availability. Every actual embedding validates runtime dimensional compatibility. Service failures disable actions when reflected in health, and in-flight failures return a notification while preserving dataset/map browsing.

