using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoPM_MCP.Helpers;

/// <summary>
/// Shapes what tools return to the LLM: compact JSON with consistent paging and success fields.
/// </summary>
internal static class ToolResponse
{
    private static readonly JsonSerializerOptions OutputOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>A page of results straight from a paginated endpoint.</summary>
    public static string List(AgilePmResponse response) =>
        Json(new { items = response.Items, paging = response.Page });

    /// <summary>A page of results that this server filtered and paged itself.</summary>
    public static string List<T>(IReadOnlyList<T> allItems, int page, int pageSize)
    {
        var totalPages = (int)Math.Ceiling(allItems.Count / (double)pageSize);
        var items = allItems.Skip((page - 1) * pageSize).Take(pageSize);
        return Json(new { items, paging = new PageInfo(page, pageSize, allItems.Count, totalPages, page < totalPages) });
    }

    /// <summary>The raw payload of a read (a single record or an unpaged list).</summary>
    public static string Data(AgilePmResponse response) => Json(new { data = response.Data });

    /// <summary>Confirmation of a write, including anything Agile PM said and returned.</summary>
    public static string Success(string summary, AgilePmResponse response) =>
        Json(new
        {
            succeeded = true,
            summary,
            messages = response.Messages.Count > 0 ? response.Messages : null,
            data = response.Data,
        });

    public static string Json(object value) => JsonSerializer.Serialize(value, OutputOptions);
}
