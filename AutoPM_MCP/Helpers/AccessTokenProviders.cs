namespace AutoPM_MCP.Helpers;

/// <summary>
/// Supplies the bearer token of the user the current MCP request is running for.
/// The same token is forwarded to Agile PM; this server never stores or issues tokens.
/// </summary>
public interface IAccessTokenProvider
{
    /// <summary>The raw JWT (without the "Bearer " prefix), or null when the caller supplied none.</summary>
    string? GetAccessToken();

    /// <summary>Explains to the caller how to supply a token in the current transport mode.</summary>
    string MissingTokenHint { get; }
}

/// <summary>
/// HTTP mode: reads the token from the Authorization header of the incoming MCP request.
/// Singleton-safe because <see cref="IHttpContextAccessor"/> resolves the current request per async flow.
/// </summary>
internal sealed class HttpHeaderTokenProvider(IHttpContextAccessor httpContextAccessor) : IAccessTokenProvider
{
    private const string BearerPrefix = "Bearer ";

    public string MissingTokenHint =>
        "Send the Agile PM access token in the Authorization header of your MCP requests ('Authorization: Bearer <token>'). " +
        "Use the login tool to obtain one.";

    public string? GetAccessToken()
    {
        var header = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = header[BearerPrefix.Length..].Trim();
        return token.Length > 0 ? token : null;
    }
}

/// <summary>
/// stdio mode: there are no request headers, so the token comes from configuration (the AGILEPM_TOKEN
/// environment variable set in the MCP client's server config, or user secrets during development).
/// </summary>
internal sealed class ConfigurationTokenProvider(IConfiguration configuration) : IAccessTokenProvider
{
    public const string TokenKey = "AGILEPM_TOKEN";

    public string MissingTokenHint =>
        $"Set the {TokenKey} environment variable in your MCP client's server configuration to an Agile PM access token. " +
        "Use the login tool to obtain one.";

    public string? GetAccessToken()
    {
        var token = configuration[TokenKey]?.Trim();
        return string.IsNullOrEmpty(token) ? null : token;
    }
}
