using AutoPM_MCP.Helpers;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AutoPM_MCP.Tools.Auth;

/// <summary>
/// Sign-in tools for the Agile PM Auth controller. These are the only tools that work without a bearer token.
/// This server is stateless and never stores tokens: the user must configure the returned token in their MCP client.
/// </summary>
[McpServerToolType]
internal class AuthTools(AgilePmClient client)
{
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
        return ToolResponse.Success($"Signed in. {ConfigureTokenNote}", response);
    }

    [McpServerTool(Destructive = false, OpenWorld = true)]
    [Description("Exchanges an expired (or expiring) access token and its refresh token for a new pair. " + ConfigureTokenNote)]
    public async Task<string> RefreshToken(
        [Description("The current (possibly expired) access token (JWT)")] string jwtToken,
        [Description("The refresh token returned by login or a previous refresh")] string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var response = await client.PostAnonymousAsync("Auth/RefreshToken", new RefreshTokenRequest(jwtToken, refreshToken), cancellationToken);
        return ToolResponse.Success($"Token refreshed. {ConfigureTokenNote}", response);
    }

    private sealed record LoginRequest(string Email, string Password);

    private sealed record RefreshTokenRequest(string JwtToken, string RefreshToken);
}
