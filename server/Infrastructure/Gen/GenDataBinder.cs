using System.Text.Json;

namespace NexusDocs.Api.Infrastructure.Gen;

/// <summary>
/// Converts a caller-supplied JSON payload (a <see cref="DocumentTemplate"/>'s SampleDataJson, or a
/// generation's InputDataJson) into the plain CLR object graph <see cref="ITemplateRenderer.RenderToPdfAsync"/>
/// expects as its <c>data</c> parameter. Scriban's reflection-based model binding
/// (<c>Template.Render(model, ...)</c>, see <see cref="ScribanTemplateRenderer"/>) walks
/// <see cref="Dictionary{TKey,TValue}"/>/<see cref="List{T}"/> graphs and plain scalars fine, so JSON
/// objects/arrays/scalars are mapped 1:1 onto those rather than round-tripping through a POCO.
/// </summary>
public static class GenDataBinder
{
    /// <summary>Parses a JSON payload into a Dictionary/List/scalar graph, or null if it isn't valid JSON.</summary>
    public static object? ParseDataJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object?>();

        try
        {
            using var document = JsonDocument.Parse(json);
            return Convert(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static object? Convert(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new Dictionary<string, object?>();
                foreach (var property in element.EnumerateObject())
                {
                    obj[property.Name] = Convert(property.Value);
                }
                return obj;

            case JsonValueKind.Array:
                return element.EnumerateArray().Select(Convert).ToList();

            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                return element.TryGetInt64(out var l) ? l : element.GetDouble();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.Null:
            default:
                return null;
        }
    }
}
