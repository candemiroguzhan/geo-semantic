using System.Net;
using GeoSemantic.Application.Abstractions;
using GeoSemantic.Infrastructure.AI;
using GeoSemantic.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
namespace GeoSemantic.Infrastructure.Tests;

public sealed class OllamaHealthTests
{
    [Fact] public async Task OfflineServiceReturnsActionableStatusWithoutThrowing() {
        var service = Create(_ => throw new HttpRequestException("offline"));
        var health = await service.CheckHealthAsync();
        Assert.False(health.Online);
        Assert.Contains("Start Ollama", health.Message);
    }
    [Fact] public async Task MissingModelDoesNotTriggerModelDownload() {
        var requests = new List<string>();
        var service = Create(request => { requests.Add(request.RequestUri!.AbsolutePath); return Json("""{"models":[]}"""); });
        var health = await service.CheckHealthAsync();
        Assert.False(health.Online);
        Assert.Equal(["/api/tags"], requests);
        Assert.Contains("No download", health.Message);
    }
    [Fact] public async Task WrongResponseModelIsRejected() {
        var service = Create(_ => Json("""{"model":"another-model","embeddings":[[1,0]]}"""));
        await Assert.ThrowsAsync<UserOperationException>(() => service.GenerateEmbeddingAsync("text"));
    }
    [Fact] public async Task BatchesAndValidatesActualHttpResponses() {
        var calls = 0;
        var service = Create(_ => { calls++; return Json("""{"model":"qwen3-embedding:0.6b","embeddings":[[1,0]]}"""); });
        var vectors = await service.GenerateEmbeddingsAsync(["Park", "Hastane", "Üniversite"]);
        Assert.Equal(3, calls);
        Assert.Equal(3, vectors.Count);
    }
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private static OllamaEmbeddingService Create(Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new(new Factory(response), Options.Create(new OllamaOptions { EmbeddingDimensions = 2, BatchSize = 1 }), NullLogger<OllamaEmbeddingService>.Instance);
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request));
    }
    private sealed class Factory(Func<HttpRequestMessage, HttpResponseMessage> response) : IHttpClientFactory {
        public HttpClient CreateClient(string name) => new(new Handler(response)) { BaseAddress = new Uri("http://localhost:11434") };
    }
}

