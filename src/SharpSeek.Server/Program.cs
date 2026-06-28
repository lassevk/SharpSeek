using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using SharpSeek.Engine;
using SharpSeek.Server;

// The "diagnose" subcommand runs find_references from the CLI for manual checks against a
// project on disk. Anything else starts the MCP server over stdio.
if (args.Length > 0 && string.Equals(args[0], "diagnose", StringComparison.Ordinal))
{
    return await Diagnostics.RunAsync(args[1..]);
}

string? projectPath = ResolveProjectPath(args);
if (projectPath is null)
{
    await Console.Error.WriteLineAsync(
        "No project configured. Pass --project <path-to-csproj> or set SHARPSEEK_PROJECT.");
    return 1;
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// The stdio transport uses stdout for protocol messages, so all logging must go to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

string fullProjectPath = Path.GetFullPath(projectPath);
builder.Services.AddSingleton(serviceProvider =>
    new ProjectSession(fullProjectPath, serviceProvider.GetRequiredService<ILogger<ProjectSession>>()));
builder.Services.AddSingleton<ReferenceFinder>();
builder.Services.AddSingleton<SymbolNavigator>();
builder.Services.AddSingleton<SymbolExplorer>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
return 0;

static string? ResolveProjectPath(string[] args)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] is "--project" or "-p")
        {
            return args[i + 1];
        }
    }

    return Environment.GetEnvironmentVariable("SHARPSEEK_PROJECT");
}
