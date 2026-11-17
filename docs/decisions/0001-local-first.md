# ADR 0001: local-first modular monolith

Status: accepted.

Retain the existing root Blazor application and its solution file. Add Domain, Application and Infrastructure projects instead of a second frontend. Use filesystem originals plus atomic JSON metadata, Ollama HTTP embeddings and the official Qdrant gRPC client. Use a bounded in-process hosted worker for indexing and a scoped Blazor workspace state.

This meets the initial thousand-feature use case with inspectable operations and few moving parts. Costs: single-process storage ownership, no resumable distributed jobs, full bounded server parsing, and explicit re-indexing after interruption. These are documented MVP limits, not hidden production promises.

