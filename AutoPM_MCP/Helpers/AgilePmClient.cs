using ModelContextProtocol;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoPM_MCP.Helpers;

/// <summary>
/// Typed HTTP client for the Agile PM API. Tools use this and never touch HttpClient, headers or JSON directly.
/// Every failure is surfaced as an <see cref="McpException"/>, whose message the MCP client (and the LLM) sees.
/// </summary>
/// <remarks>
/// Paths are relative to AgilePM:ApiBaseUrl (which already ends in "/api/") and must not start with "/",
/// otherwise HttpClient would discard the "/api/" segment of the base address.
/// </remarks>
public sealed class AgilePmClient(HttpClient httpClient, ILogger<AgilePmClient> logger)
{
    private const int GetAllPageSize = 100;
    private const int GetAllMaxPages = 50;
    private const int MaxErrorBodyLength = 500;

    /// <summary>Request bodies: camelCase, nulls omitted, enums as numbers (what the API expects).</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<AgilePmResponse> GetAsync(string path, AgilePmQuery? query = null, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, path + query?.ToQueryString(), body: null, anonymous: false, cancellationToken);

    public Task<AgilePmResponse> PostAsync(string path, object? body, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, path, body, anonymous: false, cancellationToken);

    /// <summary>POST without the caller's bearer token, for endpoints such as login and token refresh.</summary>
    public Task<AgilePmResponse> PostAnonymousAsync(string path, object? body, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, path, body, anonymous: true, cancellationToken);

    /// <summary>
    /// Fetches a single record through a list endpoint's "id" filter (the API has no dedicated get-by-id endpoints).
    /// </summary>
    public async Task<T> GetByIdAsync<T>(string path, Guid id, string entityName, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync(path, AgilePmQuery.Create().Add("id", id), cancellationToken);
        var item = response.Items.FirstOrDefault();
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new McpException($"{entityName} '{id}' was not found, or you do not have access to it.");
        }

        return item.Deserialize<T>(JsonOptions)
            ?? throw new McpException($"Agile PM returned an unreadable {entityName} record for '{id}'.");
    }

    /// <summary>
    /// Reads every page of a paginated endpoint. Use only where the API cannot filter for us (e.g. "my projects").
    /// </summary>
    public async Task<IReadOnlyList<JsonElement>> GetAllPagesAsync(string path, AgilePmQuery query, CancellationToken cancellationToken = default)
    {
        var items = new List<JsonElement>();
        for (var page = 1; page <= GetAllMaxPages; page++)
        {
            var response = await GetAsync(path, query.WithPaging(page, GetAllPageSize), cancellationToken);
            var before = items.Count;
            items.AddRange(response.Items);

            var noMorePages = response.Page is null
                || !response.Page.HasNextPage
                || page >= response.Page.TotalPages
                || items.Count == before;
            if (noMorePages)
            {
                return items;
            }
        }

        logger.LogWarning("Stopped reading {Path} after {MaxPages} pages; results may be incomplete", path, GetAllMaxPages);
        return items;
    }

    private async Task<AgilePmResponse> SendAsync(HttpMethod method, string path, object? body, bool anonymous, CancellationToken cancellationToken)
    {
        if (path.StartsWith('/'))
        {
            throw new ArgumentException("Agile PM paths must be relative to the base URL (no leading '/').", nameof(path));
        }

        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        if (anonymous)
        {
            request.Options.Set(BearerTokenHandler.AllowAnonymous, true);
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Agile PM request {Method} {Path} failed to connect", method, path);
            throw new McpException("Could not reach Agile PM. Check the network connection and AgilePM:ApiBaseUrl.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Agile PM request {Method} {Path} timed out", method, path);
            throw new McpException("Agile PM did not respond in time. Try again shortly.", ex);
        }

        using (response)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Agile PM request {Method} {Path} returned {StatusCode}", method, path, (int)response.StatusCode);
                throw CreateHttpError(response.StatusCode, content);
            }

            return AgilePmResponse.Parse(content);
        }
    }

    private static McpException CreateHttpError(HttpStatusCode statusCode, string content)
    {
        switch (statusCode)
        {
            case HttpStatusCode.Unauthorized:
                return new McpException(
                    "Agile PM rejected the access token (401): it is missing, invalid or expired. " +
                    "Get a new one with the refresh_token tool (or login) and update the token your MCP client sends.");
            case HttpStatusCode.Forbidden:
                return new McpException("You do not have permission to do this in Agile PM (403).");
            case HttpStatusCode.NotFound:
                return new McpException("Agile PM could not find the requested resource (404).");
        }

        var detail = DescribeErrorBody(content);
        return new McpException($"Agile PM returned {(int)statusCode} ({statusCode}){(detail is null ? "." : $": {detail}")}");
    }

    private static string? DescribeErrorBody(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            var messages = ErrorMessages.Extract(document.RootElement);
            if (messages.Count > 0)
            {
                return string.Join(" ", messages);
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall through to the raw text.
        }

        return content.Length <= MaxErrorBodyLength ? content : content[..MaxErrorBodyLength] + "...";
    }
}
