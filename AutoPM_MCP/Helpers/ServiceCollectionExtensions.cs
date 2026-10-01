using Microsoft.Extensions.Options;
using System.Net.Http.Headers;

namespace AutoPM_MCP.Helpers;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Agile PM client, its bearer-token pipeline and the token source for the given transport.
    /// </summary>
    public static IServiceCollection AddAgilePm(this IServiceCollection services, IConfiguration configuration, McpTransportMode transport)
    {
        services.AddOptions<AgilePmOptions>()
            .Bind(configuration.GetSection(AgilePmOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.ApiBaseUrl is { IsAbsoluteUri: true }, "AgilePM:ApiBaseUrl must be an absolute URL.")
            .ValidateOnStart();

        if (transport == McpTransportMode.Http)
        {
            services.AddHttpContextAccessor();
            services.AddSingleton<IAccessTokenProvider, HttpHeaderTokenProvider>();
        }
        else
        {
            services.AddSingleton<IAccessTokenProvider, ConfigurationTokenProvider>();
        }

        services.AddSingleton<CurrentUser>();
        services.AddTransient<BearerTokenHandler>();
        services
            .AddHttpClient<AgilePmClient>((serviceProvider, httpClient) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<AgilePmOptions>>().Value;
                httpClient.BaseAddress = WithTrailingSlash(options.ApiBaseUrl!);
                httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddHttpMessageHandler<BearerTokenHandler>();

        return services;
    }

    /// <summary>
    /// Without a trailing slash, HttpClient would replace the last segment of the base URL ("api") with the request path.
    /// </summary>
    private static Uri WithTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
}
