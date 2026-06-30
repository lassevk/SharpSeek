using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using SharpSeek.Engine;
using SharpSeek.Server;

// The "version" subcommand prints this server's build identity (the git commit it was built from)
// without going through MCP - the CLI equivalent of the server_info tool, handy for verifying a
// deployed build on a machine.
if (args.Length > 0 && (string.Equals(args[0], "version", StringComparison.Ordinal)
    || string.Equals(args[0], "--version", StringComparison.Ordinal)))
{
    ServerBuildInfo info = BuildInfo.Current;
    Console.WriteLine($"commit:  {info.Commit ?? "unknown"}");
    Console.WriteLine($"dirty:   {(info.Dirty is { } dirty ? dirty.ToString().ToLowerInvariant() : "unknown")}");
    Console.WriteLine($"built:   {info.BuildTimeUtc?.ToString("o") ?? "unknown"}");
    Console.WriteLine($"version: {info.Version ?? "unknown"}");
    return 0;
}

// The "diagnose" subcommand runs find_references from the CLI for manual checks against a
// project on disk. Anything else starts the MCP server over stdio.
if (args.Length > 0 && string.Equals(args[0], "diagnose", StringComparison.Ordinal))
{
    return await Diagnostics.RunAsync(args[1..]);
}

// The project is optional: if not given, it is discovered from the session (the client's
// workspace roots, else the working directory).
string? explicitProject = ResolveProjectPath(args);

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

string? explicitFullPath = explicitProject is null ? null : Path.GetFullPath(explicitProject);
builder.Services.AddSingleton(serviceProvider =>
    new ProjectSession(explicitFullPath, serviceProvider.GetRequiredService<ILogger<ProjectSession>>()));
builder.Services.AddSingleton<ReferenceFinder>();
builder.Services.AddSingleton<SymbolNavigator>();
builder.Services.AddSingleton<DeclarationReader>();
builder.Services.AddSingleton<SymbolExplorer>();
builder.Services.AddSingleton<DeadCodeFinder>();
builder.Services.AddSingleton<DiagnosticReader>();
builder.Services.AddSingleton<CallHierarchyAnalyzer>();
builder.Services.AddSingleton<ProjectInspector>();
builder.Services.AddSingleton<DependencyAnalyzer>();
builder.Services
    .AddMcpServer(options => options.ServerInstructions =
        "SharpSeek is a Roslyn-based code navigation server for .NET/C#. It analyses ONE .NET " +
        "solution (or project) per session, discovered from the current workspace folder (the " +
        "directory you are working in) unless set explicitly. Every tool operates on that one " +
        ".NET solution. Use these tools ONLY for navigating and analysing .NET/C# code - they do " +
        "not apply to non-.NET languages (e.g. JavaScript, Python, Go); if the current project is " +
        "not .NET/C#, do not use them. A key strength is that references are found even inside " +
        "source-generated code (e.g. Blazor / Razor) and mapped back to the original source. Use " +
        "the activate_project tool to point the server at a different solution or project. " +
        "When working in a .NET/C# codebase, prefer these tools over plain text search for " +
        "navigation, references, type/call hierarchy, and dead-code questions: they are semantic " +
        "and see source-generated code. Be critical of how well they fit the task - if you hit a " +
        "navigation or analysis need they do not cover, or an output shape that is awkward for " +
        "what the user is doing, raise it with the user as a candidate SharpSeek improvement (and, " +
        "with the user's agreement, file it as an issue on the SharpSeek repository) rather than " +
        "silently working around it.")
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
