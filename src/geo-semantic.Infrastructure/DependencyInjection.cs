using GeoSemantic.Application.Abstractions;
using GeoSemantic.Application.Features.Search;
using GeoSemantic.Infrastructure.AI;
using GeoSemantic.Infrastructure.Configuration;
using GeoSemantic.Infrastructure.Geospatial;
using GeoSemantic.Infrastructure.Health;
using GeoSemantic.Infrastructure.Persistence;
using GeoSemantic.Infrastructure.VectorStorage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Qdrant.Client;

namespace GeoSemantic.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OllamaOptions>().Bind(configuration.GetSection("Ollama")).ValidateDataAnnotations()
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https", "Ollama BaseUrl must be an absolute HTTP(S) URL.").ValidateOnStart();
        services.AddOptions<QdrantOptions>().Bind(configuration.GetSection("Qdrant")).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection("Storage")).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<MapOptions>().Bind(configuration.GetSection("Map")).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<SearchOptions>().Bind(configuration.GetSection("Search"))
            .Validate(o => o.DefaultTopK >= 1 && o.MaxTopK >= o.DefaultTopK && o.MaxTopK <= 50, "Search limits must satisfy 1 <= DefaultTopK <= MaxTopK <= 50.").ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<SearchOptions>>().Value);
        services.AddHttpClient("Ollama", (sp, client) => {
            var o = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(o.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });
        services.AddSingleton(sp => {
            var o = sp.GetRequiredService<IOptions<QdrantOptions>>().Value;
            return new QdrantClient(o.Host, o.GrpcPort, https: false, grpcTimeout: TimeSpan.FromSeconds(30));
        });
        services.AddSingleton<IGeoJsonService, GeoJsonService>();
        services.AddSingleton<IDatasetRepository, FileSystemDatasetRepository>();
        services.AddSingleton<IEmbeddingService, OllamaEmbeddingService>();
        services.AddSingleton<IVectorStore, QdrantVectorStore>();
        services.AddSingleton<ServiceHealthMonitor>();
        return services;
    }
}

