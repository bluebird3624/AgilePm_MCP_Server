using AutoPM_MCP.Helpers;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AutoPM_MCP.Tools.Auth;

/// <summary>
/// Sign-in tools for the Agile PM Auth controller. These are the only tools that work without a bearer token.
/// In stdio mode the returned token is used for the rest of the session; in HTTP mode the server is stateless
/// and the user must configure the token in their MCP client.
/// </summary>
[McpServerToolType]
internal class AuthTools(AgilePmClient client, IAccessTokenProvider tokenProvider)
{
    private const string SessionTokenNote = "This server will use the new access token for the rest of this session.";

    private const string ConfigureTokenNote =
        "This server does not store tokens. Configure the returned access token in your MCP client " +
        "(HTTP: 'Authorization: Bearer <token>' header; stdio: AGILEPM_TOKEN environment variable) so later tool calls are authenticated.";

    [McpServerTool(Destructive = false, OpenWorld = true)]
    [Description("Signs in to Agile PM with email and password and returns an access token and refresh token. " + ConfigureTokenNote)]
    public async Task<string> Login(
        [Description("Agile PM account email")] string email,
        [Description("Agile PM account password")] string password,
        CancellationToken cancellationToken = default)
    {
        var response = await client.PostAnonymousAsync("Auth/Login", new LoginRequest(email, password), cancellationToken);
        return ToolResponse.Success($"Signed in. {RememberToken(response)}", response);
    }

    [McpServerTool(Destructive = false, OpenWorld = true)]
    [Description("Exchanges an expired (or expiring) access token and its refresh token for a new pair. " + ConfigureTokenNote)]
    public async Task<string> RefreshToken(
        [Description("The current (possibly expired) access token (JWT)")] string jwtToken,
        [Description("The refresh token returned by login or a previous refresh")] string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var response = await client.PostAnonymousAsync("Auth/RefreshToken", new RefreshTokenRequest(jwtToken, refreshToken), cancellationToken);
        return ToolResponse.Success($"Token refreshed. {RememberToken(response)}", response);
    }

    /// <summary>stdio mode keeps the new token for later calls; HTTP mode can't, so the user has to configure it.</summary>
    private string RememberToken(AgilePmResponse response) =>
        response.Data is { } body && StdioTokenProvider.FindToken(body) is { } token && tokenProvider.Remember(token)
            ? SessionTokenNote
            : ConfigureTokenNote;

    private sealed record LoginRequest(string Email, string Password);

    private sealed record RefreshTokenRequest(string JwtToken, string RefreshToken);
}
