using System.Text.Json;

namespace AutoPM_MCP.Helpers;

internal static class JsonElementExtensions
{
    public static string? GetStringOrNull(this JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static int GetInt32OrDefault(this JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    /// <summary>The property's value, or null when it is missing or JSON null.</summary>
    public static JsonElement? GetPropertyOrNull(this JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value
            : null;

    public static IEnumerable<JsonElement> EnumerateArrayOrEmpty(this JsonElement element, string property) =>
        element.GetPropertyOrNull(property) is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : [];
}
