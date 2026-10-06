using ModelContextProtocol;
using System.Net;
using System.Net.Http.Headers;

namespace AutoPM_MCP.Helpers;

/// <summary>
/// Attaches the caller's bearer token to every outgoing Agile PM request.
/// Requests marked with <see cref="AllowAnonymous"/> (login, refresh) are sent without one.
/// When Agile PM rejects the token and the provider can get a fresh one (stdio sign-in), the request is retried once.
/// </summary>
internal sealed class BearerTokenHandler(IAccessTokenProvider tokenProvider) : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<bool> AllowAnonymous = new("AgilePM.AllowAnonymous");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(AllowAnonymous, out var anonymous) && anonymous)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var token = await GetTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized || !tokenProvider.TryInvalidate(token))
        {
            return response;
        }

        response.Dispose();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(cancellationToken));
        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken) =>
        await tokenProvider.GetAccessTokenAsync(cancellationToken)
            ?? throw new McpException($"You are not signed in to Agile PM. {tokenProvider.MissingTokenHint}");
}
