using Microsoft.Extensions.Options;
using ModelContextProtocol;
using System.Text.Json;

namespace AutoPM_MCP.Helpers;

/// <summary>
/// Supplies the bearer token of the user the current MCP request is running for.
/// </summary>
public interface IAccessTokenProvider
{
    /// <summary>The raw JWT (without the "Bearer " prefix), or null when none is available.</summary>
    ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>Explains to the caller how to supply a token in the current transport mode.</summary>
    string MissingTokenHint { get; }

    /// <summary>
    /// Discards a token Agile PM rejected. Returns true when a fresh token can be obtained, so the request is worth retrying.
    /// </summary>
    bool TryInvalidate(string rejectedToken) => false;

    /// <summary>
    /// Uses a token obtained through the login / refresh tools for later requests. Returns false when this mode cannot keep tokens.
    /// </summary>
    bool Remember(string token) => false;
}

/// <summary>
/// HTTP mode: reads the token from the Authorization header of the incoming MCP request.
/// Singleton-safe because <see cref="IHttpContextAccessor"/> resolves the current request per async flow.
/// The server is stateless here and never stores tokens: each client sends its own.
/// </summary>
internal sealed class HttpHeaderTokenProvider(IHttpContextAccessor httpContextAccessor) : IAccessTokenProvider
{
    private const string BearerPrefix = "Bearer ";

    public string MissingTokenHint =>
        "Send the Agile PM access token in the Authorization header of your MCP requests ('Authorization: Bearer <token>'). " +
        "Use the login tool to obtain one.";

    public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var header = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult<string?>(null);
        }

        var token = header[BearerPrefix.Length..].Trim();
        return ValueTask.FromResult(token.Length > 0 ? token : null);
    }
}

/// <summary>
/// stdio mode: one user per process. Signs in with AgilePM:Email / AgilePM:Password (appsettings.json, or the
/// AGILEPM_EMAIL / AGILEPM_PASSWORD environment variables) and keeps the token in memory, signing in again
/// when it expires or Agile PM rejects it. Without credentials, falls back to the AGILEPM_TOKEN setting.
/// </summary>
internal sealed class StdioTokenProvider(
    IConfiguration configuration,
    IOptions<AgilePmOptions> options,
    IServiceProvider services,
    ILogger<StdioTokenProvider> logger) : IAccessTokenProvider
{
    public const string TokenKey = "AGILEPM_TOKEN";

    /// <summary>Sign in again this long before the token's "exp" claim, so requests in flight don't expire mid-way.</summary>
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(60);

    private static readonly string[] TokenProperties = ["jwtToken", "token", "accessToken", "access_token"];

    private readonly SemaphoreSlim signInLock = new(1, 1);
    private SessionToken? session;

    private bool HasCredentials =>
        !string.IsNullOrWhiteSpace(options.Value.Email) && !string.IsNullOrEmpty(options.Value.Password);

    public string MissingTokenHint =>
        "Set AgilePM:Email and AgilePM:Password in appsettings.json (next to the executable), or AGILEPM_EMAIL and AGILEPM_PASSWORD " +
        $"(or {TokenKey}) in the env block of your MCP client's server configuration. The login tool also signs you in for this session.";

    public async ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (session is { } current && !current.IsExpiring)
        {
            return current.Token;
        }

        if (!HasCredentials)
        {
            var configured = configuration[TokenKey]?.Trim();
            return string.IsNullOrEmpty(configured) ? null : configured;
        }

        await signInLock.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have signed in while we waited.
            if (session is { } refreshed && !refreshed.IsExpiring)
            {
                return refreshed.Token;
            }

            return await SignInAsync(cancellationToken);
        }
        finally
        {
            signInLock.Release();
        }
    }

    public bool TryInvalidate(string rejectedToken)
    {
        var current = session;
        if (current is not null && current.Token == rejectedToken)
        {
            Interlocked.CompareExchange(ref session, null, current);
        }

        return HasCredentials;
    }

    public bool Remember(string token)
    {
        session = SessionToken.From(token);
        return true;
    }

    private async Task<string> SignInAsync(CancellationToken cancellationToken)
    {
        var email = options.Value.Email!.Trim();
        logger.LogInformation("Signing in to Agile PM as {Email}", email);

        // Resolved per call: AgilePmClient's pipeline depends on this provider, so it can't be a constructor dependency.
        var client = services.GetRequiredService<AgilePmClient>();
        var response = await client.PostAnonymousAsync("Auth/Login", new { email, password = options.Value.Password }, cancellationToken);

        var token = (response.Data is { } body ? FindToken(body) : null)
            ?? throw new McpException("Agile PM accepted the sign-in but returned no access token.");

        session = SessionToken.From(token);
        logger.LogInformation("Signed in to Agile PM as {Email}; token valid until {Expiry:u}", email, session.ExpiresAt);
        return token;
    }

    /// <summary>
    /// The Auth envelope's "data" shape isn't documented: accept a bare JWT string or a known token property at any depth.
    /// </summary>
    internal static string? FindToken(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String when LooksLikeJwt(element.GetString()):
                return element.GetString();
            case JsonValueKind.Object:
                foreach (var name in TokenProperties)
                {
                    if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && LooksLikeJwt(value.GetString()))
                    {
                        return value.GetString();
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Object && FindToken(property.Value) is { } nested)
                    {
                        return nested;
                    }
                }

                // "data" may itself be the bare token.
                return element.TryGetProperty("data", out var data) ? FindToken(data) : null;
            default:
                return null;
        }
    }

    private static bool LooksLikeJwt(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Count(c => c == '.') == 2;

    private sealed record SessionToken(string Token, DateTimeOffset ExpiresAt)
    {
        public bool IsExpiring => DateTimeOffset.UtcNow >= ExpiresAt - ExpirySkew;

        /// <summary>Expiry comes from the JWT "exp" claim; without one, the token is used until Agile PM rejects it.</summary>
        public static SessionToken From(string token)
        {
            var expiresAt = DateTimeOffset.MaxValue;
            try
            {
                var claims = CurrentUser.DecodePayload(token);
                if (claims.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds))
                {
                    expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
                }
            }
            catch (McpException)
            {
                // Not a JWT we can read; let Agile PM decide.
            }

            return new SessionToken(token, expiresAt);
        }
    }
}

/// <summary>
/// stdio mode: signs in once at startup so the first tool call doesn't wait, and so bad credentials show up in the log early.
/// Runs in the background: the MCP handshake must not wait on Agile PM.
/// </summary>
internal sealed class StartupSignInService(IAccessTokenProvider tokenProvider, IOptions<AgilePmOptions> options, ILogger<StartupSignInService> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.Email) || string.IsNullOrEmpty(options.Value.Password))
        {
            logger.LogInformation("AgilePM:Email / AgilePM:Password not set; using {TokenKey} if configured", StdioTokenProvider.TokenKey);
            return Task.CompletedTask;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await tokenProvider.GetAccessTokenAsync();
            }
            catch (Exception ex)
            {
                // Tool calls retry the sign-in and report the error to the client.
                logger.LogError("Startup sign-in to Agile PM failed: {Error}", ex.Message);
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
