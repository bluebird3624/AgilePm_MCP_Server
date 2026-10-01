using System.ComponentModel.DataAnnotations;

namespace AutoPM_MCP.Helpers;

/// <summary>
/// Settings for the Agile PM API, bound from the "AgilePM" section of appsettings.json.
/// </summary>
public sealed class AgilePmOptions
{
    public const string SectionName = "AgilePM";

    /// <summary>
    /// Base URL of the Agile PM API, e.g. https://your-agilepm-host/api/
    /// </summary>
    [Required]
    public Uri? ApiBaseUrl { get; set; }

    [Range(5, 300)]
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// How MCP clients talk to this server.
/// </summary>
public enum McpTransportMode
{
    /// <summary>Streamable HTTP. The caller's token arrives in the Authorization header of every request.</summary>
    Http,

    /// <summary>stdio ("bus mode"). The token is read from the AGILEPM_TOKEN environment variable.</summary>
    Stdio,
}
