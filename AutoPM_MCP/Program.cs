using AutoPM_MCP.Helpers;
using Serilog;
using Serilog.Events;

// 1. Configure the Bootstrap Logger for application startup.
//    It writes to stderr so it can never corrupt the protocol stream in stdio mode.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose)
    .CreateBootstrapLogger();
try
{
    // Transport: "--stdio" on the command line, or McpTransport = Http | Stdio in config / environment.
    var useStdioFlag = args.Contains("--stdio", StringComparer.OrdinalIgnoreCase);
    var hostArgs = args.Where(a => !string.Equals(a, "--stdio", StringComparison.OrdinalIgnoreCase)).ToArray();
    var transport = useStdioFlag ? McpTransportMode.Stdio : ResolveTransport(hostArgs);

    Log.Information("Starting AutoPM MCP Server in {Transport} mode", transport);

    if (transport == McpTransportMode.Stdio)
    {
        await RunStdioAsync(hostArgs);
    }
    else
    {
        await RunHttpAsync(hostArgs);
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "MCP Server terminated unexpectedly");

    // The configured logger may have no sinks (e.g. appsettings.json missing), so always tell the operator on stderr.
    Console.Error.WriteLine($"AutoPM MCP Server failed to start: {ex.Message}");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

static McpTransportMode ResolveTransport(string[] args)
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables()
        .AddCommandLine(args)
        .Build();

    var value = configuration["McpTransport"];
    if (string.IsNullOrWhiteSpace(value))
    {
        return McpTransportMode.Http;
    }

    return Enum.TryParse<McpTransportMode>(value, ignoreCase: true, out var mode)
        ? mode
        : throw new InvalidOperationException($"Unknown McpTransport '{value}'. Use 'Http' or 'Stdio'.");
}

static async Task RunHttpAsync(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Logging.ClearProviders();

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
    });

    builder.Services.AddAgilePm(builder.Configuration, McpTransportMode.Http);

    // Add the MCP services: the transport to use (http) and the tools to register.
    builder.Services
        .AddMcpServer(ConfigureServerInfo)
        .WithHttpTransport(options =>
        {
            // Stateless mode is recommended for servers that don't need
            // server-to-client requests like sampling or elicitation.
            // See https://csharp.sdk.modelcontextprotocol.io/concepts/transports/transports.html for details.
            // The caller's bearer token is read from each request, so nothing is kept between requests.
            options.Stateless = true;
        })
        .WithToolsFromAssembly();

    var app = builder.Build();

    app.UseCors();
    app.UseSerilogRequestLogging();
    app.MapMcp();
    //app.UseHttpsRedirection();
    await app.RunAsync();
}

static async Task RunStdioAsync(string[] args)
{
    // MCP clients launch stdio servers from arbitrary working directories, so load
    // appsettings.json from the executable's folder instead of the current directory.
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });

    // stdout carries the MCP protocol in stdio mode: every log line must go to stderr.
    RouteConsoleLogsToStandardError(builder.Configuration);

    builder.Logging.ClearProviders();
    builder.Services.AddSerilog((services, configuration) => configuration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddAgilePm(builder.Configuration, McpTransportMode.Stdio);

    builder.Services
        .AddMcpServer(ConfigureServerInfo)
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
}

static void ConfigureServerInfo(ModelContextProtocol.Server.McpServerOptions options)
{
    options.ServerInfo = new()
    {
        Name = "AutoPM MCP Server",
        Description = "An MCP server for use with LLMs to work with AgilePM",
        Version = "1.0.0"
    };
}

static void RouteConsoleLogsToStandardError(ConfigurationManager configuration)
{
    foreach (var sink in configuration.GetSection("Serilog:WriteTo").GetChildren())
    {
        if (string.Equals(sink["Name"], "Console", StringComparison.OrdinalIgnoreCase))
        {
            configuration[$"{sink.Path}:Args:standardErrorFromLevel"] = "Verbose";
        }
    }
}
