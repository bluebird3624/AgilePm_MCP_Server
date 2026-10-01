using ModelContextProtocol;
using System.Net.Http.Headers;

namespace AutoPM_MCP.Helpers;

/// <summary>
/// Attaches the caller's bearer token to every outgoing Agile PM request.
/// Requests marked with <see cref="AllowAnonymous"/> (login, refresh) are sent without one.
/// </summary>
internal sealed class BearerTokenHandler(IAccessTokenProvider tokenProvider) : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<bool> AllowAnonymous = new("AgilePM.AllowAnonymous");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(AllowAnonymous, out var anonymous) && anonymous)
        {
            return base.SendAsync(request, cancellationToken);
        }

        var token = tokenProvider.GetAccessToken()
            ?? throw new McpException($"You are not signed in to Agile PM. {tokenProvider.MissingTokenHint}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return base.SendAsync(request, cancellationToken);
    }
}
