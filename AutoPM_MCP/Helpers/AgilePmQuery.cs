using System.Globalization;
using System.Text;

namespace AutoPM_MCP.Helpers;

/// <summary>
/// Builds query strings in the shape the Agile PM list endpoints bind (e.g. "paging.pageNumber=1&amp;ProjectId=...").
/// Null values are skipped so optional tool arguments can be passed straight through.
/// </summary>
public sealed class AgilePmQuery
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    private readonly List<KeyValuePair<string, string>> _parameters = [];

    public static AgilePmQuery Create() => new();

    /// <summary>A query for one page of results, with page and size clamped to sane bounds.</summary>
    public static AgilePmQuery Paged(int page, int pageSize)
    {
        var (normalizedPage, normalizedSize) = NormalizePaging(page, pageSize);
        return new AgilePmQuery().WithPaging(normalizedPage, normalizedSize);
    }

    public static (int Page, int PageSize) NormalizePaging(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));

    public AgilePmQuery Add(string name, object? value)
    {
        var formatted = value switch
        {
            null => null,
            string s => string.IsNullOrWhiteSpace(s) ? null : s,
            bool b => b ? "true" : "false",
            Guid g => g.ToString("D"),
            DateTime d => d.ToString("o", CultureInfo.InvariantCulture),
            DateTimeOffset d => d.ToString("o", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

        if (formatted is not null)
        {
            _parameters.Add(new(name, formatted));
        }

        return this;
    }

    /// <summary>Returns a copy of this query targeting the given page (replacing any existing paging).</summary>
    public AgilePmQuery WithPaging(int page, int pageSize)
    {
        var copy = new AgilePmQuery();
        copy._parameters.AddRange(_parameters.Where(p => !p.Key.StartsWith("paging.", StringComparison.Ordinal)));
        return copy.Add("paging.pageNumber", page).Add("paging.pageSize", pageSize);
    }

    public string ToQueryString()
    {
        if (_parameters.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder("?");
        foreach (var (name, value) in _parameters)
        {
            if (builder.Length > 1)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
        }

        return builder.ToString();
    }
}
