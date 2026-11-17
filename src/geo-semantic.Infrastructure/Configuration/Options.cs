using System.ComponentModel.DataAnnotations;
namespace GeoSemantic.Infrastructure.Configuration;

public sealed class OllamaOptions
{
    [Required, Url] public string BaseUrl { get; set; } = "http://localhost:11434";
    [Required] public string EmbeddingModel { get; set; } = "qwen3-embedding:0.6b";
    [Range(1, 65536)] public int EmbeddingDimensions { get; set; } = 1024;
    [Range(1, 128)] public int BatchSize { get; set; } = 16;
    [Range(5, 600)] public int TimeoutSeconds { get; set; } = 120;
}
public sealed class QdrantOptions
{
    [Required] public string Host { get; set; } = "localhost";
    [Range(1, 65535)] public int GrpcPort { get; set; } = 6334;
    [Range(1, 65535)] public int HttpPort { get; set; } = 6333;
    [RegularExpression("^[a-zA-Z0-9_-]{1,40}$")] public string CollectionPrefix { get; set; } = "geosemantic";
}
public sealed class StorageOptions
{
    [Required] public string DataDirectory { get; set; } = "AppData";
    [Range(1, 200)] public int MaxUploadSizeMb { get; set; } = 50;
    [Range(1000, 100000)] public int MaxFeatures { get; set; } = 10000;
}
public sealed class MapOptions
{
    [Required, Url] public string StyleUrl { get; set; } = "https://tiles.openfreemap.org/styles/dark";
    [Range(-180, 180)] public double DefaultLongitude { get; set; } = 32.8597;
    [Range(-90, 90)] public double DefaultLatitude { get; set; } = 39.9334;
    [Range(0, 22)] public double DefaultZoom { get; set; } = 11;
}

