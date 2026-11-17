using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using GeoSemantic.Application.Abstractions;
using GeoSemantic.Domain.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GeoSemantic.Application.Features.Indexing;

public sealed record IndexingProgress(Guid DatasetId, string DatasetName, string Stage, int Processed, int Total,
    int Batches, string Model, int Dimensions, TimeSpan EmbeddingDuration, TimeSpan UpsertDuration, TimeSpan Duration,
    bool Finished = false, string? Error = null);

public sealed class IndexingCoordinator(IDatasetRepository repository, IEmbeddingService embeddings, IVectorStore vectors,
    SearchableTextGenerator textGenerator, DatasetOperations operations, ILogger<IndexingCoordinator> logger) : BackgroundService
{
    private sealed record Job(Guid Id, string[] Fields, IDisposable Lease, CancellationTokenSource Cancellation);
    private readonly Channel<Job> queue = Channel.CreateBounded<Job>(new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.Wait });
    private readonly ConcurrentDictionary<Guid, IndexingProgress> progress = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> cancellations = new();
    public IndexingProgress? GetProgress(Guid id) => progress.GetValueOrDefault(id);
    public void Cancel(Guid id) { if (cancellations.TryGetValue(id, out var source)) source.Cancel(); }

    public void Start(Guid id, IEnumerable<string> properties)
    {
        var lease = operations.Acquire(id);
        var source = new CancellationTokenSource();
        var fields = properties.Distinct().Order(StringComparer.Ordinal).ToArray();
        cancellations[id] = source;
        progress[id] = new(id, "", "Queued", 0, 0, 0, "", 0, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
        if (!queue.Writer.TryWrite(new(id, fields, lease, source)))
        {
            cancellations.TryRemove(id, out _); progress.TryRemove(id, out _);
            source.Dispose(); lease.Dispose();
            throw new UserOperationException("The indexing queue is full. Try again after a job completes.");
        }
        foreach (var old in progress.Where(p => p.Value.Finished).Take(Math.Max(0, progress.Count - 100))) progress.TryRemove(old.Key, out _);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Interrupted work must be explicitly re-indexed; partially stored vectors are excluded from search.
        foreach (var dataset in await repository.GetDatasetsAsync(stoppingToken))
            if (dataset.Status == DatasetStatus.Indexing)
                await repository.SaveAsync(dataset.FailIndexing("Indexing was interrupted. Re-index this dataset."), stoppingToken);
        try
        {
            await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
            {
                using (job.Lease)
                using (job.Cancellation)
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, job.Cancellation.Token))
                {
                    try { await RunAsync(job, linked.Token); }
                    finally { cancellations.TryRemove(job.Id, out _); }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task RunAsync(Job job, CancellationToken ct)
    {
        var total = Stopwatch.StartNew();
        GeoDataset? dataset = null;
        var embeddingTime = TimeSpan.Zero;
        var upsertTime = TimeSpan.Zero;
        var processed = 0;
        var batches = 0;
        EmbeddingModelInfo? model = null;
        void Publish(string stage, bool finished = false, string? error = null) =>
            progress[job.Id] = new(job.Id, dataset?.Name ?? "", stage, processed, dataset?.FeatureCount ?? 0, batches,
                model?.Model ?? "", model?.Dimensions ?? 0, embeddingTime, upsertTime, total.Elapsed, finished, error);
        try
        {
            dataset = await repository.GetDatasetAsync(job.Id, ct) ?? throw new UserOperationException("Dataset was not found.");
            dataset = dataset.BeginIndexing(job.Fields);
            model = await embeddings.GetModelInfoAsync(ct);
            var features = await repository.ReadFeaturesAsync(job.Id, ct);
            // Validate all selected text before removing any vectors.
            var prepared = features.Select(f => f with { SearchableText = textGenerator.Generate(f.Properties, job.Fields) }).ToArray();
            if (prepared.Any(f => string.IsNullOrWhiteSpace(f.SearchableText)))
                throw new UserOperationException("Some features have no text in the selected fields. Select additional properties.");
            await repository.SaveAsync(dataset, ct);
            Publish("Preparing collection");
            await vectors.EnsureCollectionAsync(ct);
            if (dataset.CollectionName is not null && dataset.CollectionName != vectors.CollectionName)
                await vectors.DeleteDatasetVectorsAsync(job.Id, dataset.CollectionName, ct);
            // Persist the target before the first write so failed imports/deletions know which collection to clean.
            dataset = dataset with { CollectionName = vectors.CollectionName, EmbeddingModel = model.Model, EmbeddingDimensions = model.Dimensions };
            await repository.SaveAsync(dataset, ct);
            await vectors.DeleteDatasetVectorsAsync(job.Id, vectors.CollectionName, ct);
            foreach (var batch in prepared.Chunk(model.BatchSize))
            {
                ct.ThrowIfCancellationRequested();
                Publish("Generating embeddings");
                var watch = Stopwatch.StartNew();
                var generated = await embeddings.GenerateEmbeddingsAsync(batch.Select(f => f.SearchableText).ToArray(), ct);
                embeddingTime += watch.Elapsed;
                if (generated.Count != batch.Length || generated.Any(v => v.Length != model.Dimensions || v.Any(x => !float.IsFinite(x))))
                    throw new UserOperationException("Embedding response has incompatible vectors.");
                Publish("Writing vectors");
                watch.Restart();
                await vectors.UpsertVectorsAsync(batch.Select((f, i) => new VectorEntry(f, generated[i])).ToArray(), ct);
                upsertTime += watch.Elapsed;
                processed += batch.Length; batches++;
                Publish("Batch completed");
            }
            dataset = dataset.CompleteIndexing(model.Model, model.Dimensions, vectors.CollectionName);
            await repository.SaveAsync(dataset, ct);
            Publish("Indexed", true);
            logger.LogInformation("Indexed dataset {DatasetId}: {Features} features in {Duration}", job.Id, processed, total.Elapsed);
        }
        catch (Exception ex)
        {
            var message = ex is OperationCanceledException ? "Indexing cancelled. Re-index to replace partial vectors." :
                ex is UserOperationException or ArgumentException ? ex.Message : "Indexing failed. Check Ollama and Qdrant, then retry.";
            logger.LogWarning(ex, "Indexing failed for dataset {DatasetId}", job.Id);
            if (dataset is not null) await repository.SaveAsync(dataset.FailIndexing(message), CancellationToken.None);
            Publish("Failed", true, message);
        }
    }
}

