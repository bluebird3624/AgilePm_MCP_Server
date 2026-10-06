using ModelContextProtocol;
using System.Text;
using System.Text.Json;

namespace AutoPM_MCP.Helpers;

/// <summary>
/// Identifies the calling user from the claims in their bearer token.
/// The token is only decoded, not validated: Agile PM validates it on every request we forward,
/// and this identity is used only to filter data Agile PM already allowed the caller to read.
/// </summary>
public sealed class CurrentUser(IAccessTokenProvider tokenProvider)
{
    private static readonly string[] UserIdClaims =
    [
        "nameid",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier",
        "uid",
        "userId",
        "UserId",
        "id",
        "sub",
    ];

    private static readonly string[] EmailClaims =
    [
        "email",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress",
        "unique_name",
        "preferred_username",
    ];

    public async Task<UserIdentity> GetIdentityAsync(CancellationToken cancellationToken = default)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken)
            ?? throw new McpException($"You are not signed in to Agile PM. {tokenProvider.MissingTokenHint}");

        var claims = DecodePayload(token);
        var ids = ReadClaims(claims, UserIdClaims);
        var emails = ReadClaims(claims, EmailClaims).Where(v => v.Contains('@')).ToList();

        if (ids.Count == 0 && emails.Count == 0)
        {
            throw new McpException("Could not identify the current user: the access token has no user id or email claim.");
        }

        return new UserIdentity(ids, emails);
    }

    internal static JsonElement DecodePayload(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            throw new McpException("The Agile PM access token is not a valid JWT.");
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return document.RootElement.Clone();
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new McpException("The Agile PM access token is not a valid JWT.", ex);
        }
    }

    private static List<string> ReadClaims(JsonElement claims, string[] names)
    {
        var values = new List<string>();
        foreach (var name in names)
        {
            if (!claims.TryGetProperty(name, out var claim))
            {
                continue;
            }

            var candidates = claim.ValueKind == JsonValueKind.Array ? claim.EnumerateArray().ToArray() : [claim];
            values.AddRange(candidates
                .Where(c => c.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(c.GetString()))
                .Select(c => c.GetString()!));
        }

        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public sealed record UserIdentity(IReadOnlyList<string> Ids, IReadOnlyList<string> Emails)
{
    /// <summary>The user's Agile PM id, for endpoints that filter by user (the first id claim that is a GUID).</summary>
    public Guid? UserId => Ids.Select(id => Guid.TryParse(id, out var guid) ? guid : (Guid?)null).FirstOrDefault(g => g is not null);

    public bool IsUser(string? idOrEmail) =>
        !string.IsNullOrWhiteSpace(idOrEmail)
        && (Ids.Contains(idOrEmail, StringComparer.OrdinalIgnoreCase) || Emails.Contains(idOrEmail, StringComparer.OrdinalIgnoreCase));
}
