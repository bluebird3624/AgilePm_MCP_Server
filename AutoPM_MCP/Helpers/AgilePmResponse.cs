using ModelContextProtocol;
using System.Text.Json;

namespace AutoPM_MCP.Helpers;

/// <summary>
/// A successful Agile PM response, unwrapped from its Result / PaginatedResult envelope
/// ({ succeeded, messages, data, currentPage, totalPages, totalCount, pageSize, hasNextPage }).
/// </summary>
public sealed class AgilePmResponse
{
    private AgilePmResponse(JsonElement? data, IReadOnlyList<string> messages, PageInfo? page)
    {
        Data = data;
        Messages = messages;
        Page = page;
    }

    /// <summary>The "data" payload (or the whole body if the endpoint does not use the envelope).</summary>
    public JsonElement? Data { get; }

    public IReadOnlyList<string> Messages { get; }

    /// <summary>Paging details, present only for paginated endpoints.</summary>
    public PageInfo? Page { get; }

    /// <summary>The payload as a sequence: array items, a single object, or nothing.</summary>
    public IEnumerable<JsonElement> Items => Data switch
    {
        { ValueKind: JsonValueKind.Array } array => array.EnumerateArray(),
        { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } or null => [],
        { } single => [single],
    };

    /// <summary>
    /// Parses a 2xx response body. Throws an <see cref="McpException"/> if the envelope reports failure,
    /// because Agile PM sometimes returns 200 with succeeded=false.
    /// </summary>
    public static AgilePmResponse Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new AgilePmResponse(null, [], null);
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(content);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Some endpoints return plain text; hand it back as a JSON string.
            return new AgilePmResponse(JsonSerializer.SerializeToElement(content), [], null);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return new AgilePmResponse(root, [], null);
        }

        // Most controllers use { succeeded, messages, data }; the Auth controller uses { isSuccess, message, data, expieryDate }.
        var isAuthEnvelope = root.TryGetProperty("isSuccess", out var isSuccess);
        if (!isAuthEnvelope && !root.TryGetProperty("succeeded", out isSuccess))
        {
            return new AgilePmResponse(root, [], null);
        }

        var messages = ErrorMessages.Extract(root);
        if (isSuccess.ValueKind == JsonValueKind.False)
        {
            throw new McpException(messages.Count > 0
                ? $"Agile PM rejected the request: {string.Join(" ", messages)}"
                : "Agile PM rejected the request without giving a reason.");
        }

        if (isAuthEnvelope)
        {
            // Keep the whole body: token details such as the expiry live next to "data", not inside it.
            return new AgilePmResponse(root, messages, null);
        }

        root.TryGetProperty("data", out var data);
        return new AgilePmResponse(data.ValueKind == JsonValueKind.Undefined ? null : data, messages, PageInfo.From(root));
    }
}

public sealed record PageInfo(int Page, int PageSize, int TotalCount, int TotalPages, bool HasNextPage)
{
    internal static PageInfo? From(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("totalCount", out var totalCount) || totalCount.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return new PageInfo(
            envelope.GetInt32OrDefault("currentPage"),
            envelope.GetInt32OrDefault("pageSize"),
            totalCount.GetInt32(),
            envelope.GetInt32OrDefault("totalPages"),
            envelope.TryGetProperty("hasNextPage", out var next) && next.ValueKind == JsonValueKind.True);
    }
}

/// <summary>
/// Pulls human-readable messages out of Agile PM envelopes and ASP.NET ProblemDetails bodies.
/// </summary>
internal static class ErrorMessages
{
    public static IReadOnlyList<string> Extract(JsonElement body)
    {
        var messages = new List<string>();
        if (body.ValueKind != JsonValueKind.Object)
        {
            return messages;
        }

        AddStrings(body, "messages", messages);
        AddStrings(body, "message", messages);
        AddStrings(body, "validationErrors", messages);

        // ProblemDetails: { title, detail, errors: { field: [ "..." ] } }
        if (body.GetStringOrNull("detail") is { } detail)
        {
            messages.Add(detail);
        }
        else if (body.GetStringOrNull("title") is { } title)
        {
            messages.Add(title);
        }

        if (body.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
        {
            foreach (var field in errors.EnumerateObject())
            {
                var fieldMessages = new List<string>();
                CollectStrings(field.Value, fieldMessages);
                messages.Add($"{field.Name}: {string.Join(" ", fieldMessages)}");
            }
        }

        return messages;
    }

    private static void AddStrings(JsonElement body, string property, List<string> messages)
    {
        if (body.TryGetProperty(property, out var value))
        {
            CollectStrings(value, messages);
        }
    }

    private static void CollectStrings(JsonElement value, List<string> messages)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String when !string.IsNullOrWhiteSpace(value.GetString()):
                messages.Add(value.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    CollectStrings(item, messages);
                }
                break;
            case JsonValueKind.Object:
                messages.Add(value.GetRawText());
                break;
        }
    }
}
