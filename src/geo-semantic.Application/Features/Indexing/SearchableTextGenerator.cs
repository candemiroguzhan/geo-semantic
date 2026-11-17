using System.Text.Json;
namespace GeoSemantic.Application.Features.Indexing;

public sealed class SearchableTextGenerator
{
    public string Generate(IReadOnlyDictionary<string, JsonElement> properties, IEnumerable<string> fields)
    {
        var parts = new List<string>();
        foreach (var field in fields.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(field)) continue;
            if (!properties.TryGetValue(field, out var value)) continue;
            var text = value.ValueKind switch {
                JsonValueKind.String => value.GetString()?.Trim(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
                _ => null
            };
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (text.Length > 4000) throw new ArgumentException($"Property '{field}' exceeds 4,000 characters. Choose a shorter field.");
            parts.Add($"{char.ToUpperInvariant(field[0])}{field[1..]}: {text.TrimEnd('.') }.");
        }
        var result = string.Join(" ", parts);
        if (result.Length > 16000) throw new ArgumentException("Selected properties exceed 16,000 characters per feature.");
        return result;
    }
}

