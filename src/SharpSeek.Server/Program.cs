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

// Optional file logging (off by default): --log-file <path> or SHARPSEEK_LOG_FILE.
string? logFile = builder.Configuration["log-file"]
    ?? Environment.GetEnvironmentVariable("SHARPSEEK_LOG_FILE");
if (!string.IsNullOrWhiteSpace(logFile))
{
    builder.Logging.AddProvider(new FileLoggerProvider(logFile));
}

string fullProjectPath = Path.GetFullPath(projectPath);
builder.Services.AddSingleton(serviceProvider =>
    new ProjectSession(fullProjectPath, serviceProvider.GetRequiredService<ILogger<ProjectSession>>()));
builder.Services.AddSingleton<ReferenceFinder>();
builder.Services.AddSingleton<SymbolNavigator>();
builder.Services.AddSingleton<SymbolExplorer>();
builder.Services.AddSingleton<DeadCodeFinder>();
builder.Services.AddSingleton<DiagnosticReader>();
builder.Services.AddSingleton<CallHierarchyAnalyzer>();
builder.Services.AddSingleton<ProjectInspector>();
builder.Services
    .AddMcpServer(options => options.ServerInstructions =
        "SharpSeek is a Roslyn-based code navigation server for a SINGLE .NET/C# project. It is " +
        "bound for its entire lifetime to exactly one project:\n" +
        $"    {fullProjectPath}\n" +
        "Every tool operates on that project only - results always pertain to it, regardless of " +
        "the current working directory or any other project that may be open. Use these tools " +
        "ONLY for navigating and analysing that .NET/C# project. They do not apply to other " +
        "projects, other solutions, or non-.NET languages (e.g. JavaScript, Python, Go). A key " +
        "strength is that references are found even inside source-generated code (e.g. Blazor / " +
        "Razor) and mapped back to the original source.")
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
