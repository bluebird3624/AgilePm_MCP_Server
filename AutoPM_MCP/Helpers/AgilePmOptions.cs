using System.ComponentModel.DataAnnotations;

namespace AutoPM_MCP.Helpers;

/// <summary>
/// Settings for the Agile PM API, bound from the "AgilePM" section of appsettings.json.
/// </summary>
public sealed class AgilePmOptions
{
    public const string SectionName = "AgilePM";

    /// <summary>
    /// Base URL of the Agile PM API, e.g. https://agilepm.example.com/api/
    /// </summary>
    [Required]
    public Uri? ApiBaseUrl { get; set; }

    [Range(5, 300)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// stdio mode only: the Agile PM account the server signs in as at startup. Ignored in HTTP mode,
    /// where every client sends its own token.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>stdio mode only: the password for <see cref="Email"/>.</summary>
    public string? Password { get; set; }
}

/// <summary>
/// How MCP clients talk to this server.
/// </summary>
public enum McpTransportMode
{
    /// <summary>Streamable HTTP. The caller's token arrives in the Authorization header of every request.</summary>
    Http,

    /// <summary>stdio ("bus mode"). The server signs in with AgilePM:Email / AgilePM:Password, or uses AGILEPM_TOKEN.</summary>
    Stdio,
}
