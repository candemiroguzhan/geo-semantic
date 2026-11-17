using GeoSemantic.Application.Features.Datasets;
using GeoSemantic.Application.Features.Indexing;
using GeoSemantic.Application.Features.Search;
using Microsoft.Extensions.DependencyInjection;
namespace GeoSemantic.Application;
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<SearchableTextGenerator>();
        services.AddSingleton<DatasetOperations>();
        services.AddSingleton<IndexingCoordinator>();
        services.AddHostedService(sp => sp.GetRequiredService<IndexingCoordinator>());
        services.AddScoped<DatasetService>();
        services.AddScoped<SemanticSearchService>();
        return services;
    }
}

