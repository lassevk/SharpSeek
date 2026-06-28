using SharpSeek.Engine;

// NOTE: This is a temporary verification harness for the walking-skeleton milestone. It loads a
// project and exercises the find_references engine logic from the command line. It will be
// replaced by the MCP host (see issue #3) once the first tool is wired up.

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: SharpSeek.Server <path-to-csproj> [symbolName]");
    return 1;
}

string projectPath = Path.GetFullPath(args[0]);
string symbolName = args.Length >= 2 ? args[1] : "ShowPreviousYearAsync";

Console.WriteLine($"Loading project: {projectPath}");
WorkspaceLoader loader = new();
ProjectLoadResult loadResult = await loader.LoadProjectAsync(projectPath);

if (loadResult.Diagnostics.Count > 0)
{
    Console.WriteLine($"Workspace diagnostics ({loadResult.Diagnostics.Count}):");
    foreach (var diagnostic in loadResult.Diagnostics)
    {
        Console.WriteLine($"  [{diagnostic.Kind}] {diagnostic.Message}");
    }
}

var generated = await loadResult.Project.GetSourceGeneratedDocumentsAsync();
Console.WriteLine(
    $"Loaded: {loadResult.Project.Name} " +
    $"({loadResult.Project.Documents.Count()} documents, {generated.Count()} generated)");

Console.WriteLine($"\nFinding references to '{symbolName}'...\n");
ReferenceFinder finder = new();
IReadOnlyList<SymbolReferences> results = await finder.FindReferencesAsync(loadResult.Project, symbolName);

if (results.Count == 0)
{
    Console.WriteLine($"No source-declared symbol named '{symbolName}' was found.");
    return 1;
}

foreach (SymbolReferences result in results)
{
    Console.WriteLine($"Symbol: {result.SymbolDisplay}");

    Console.WriteLine($"  Definitions ({result.Definitions.Count}):");
    foreach (ReferenceLocationInfo definition in result.Definitions)
    {
        Console.WriteLine($"    {Format(definition)}");
    }

    Console.WriteLine($"  References ({result.References.Count}):");
    foreach (ReferenceLocationInfo reference in result.References)
    {
        Console.WriteLine($"    {Format(reference)}");
    }
}

return 0;

static string Format(ReferenceLocationInfo location)
{
    string where = $"{location.FilePath}:{location.Line}:{location.Column}";
    if (location.Origin == ReferenceOrigin.Generated)
    {
        string generatedName = Path.GetFileName(location.GeneratedFilePath ?? "<generated>");
        return $"[generated] {where}  (in {generatedName})";
    }

    return $"[handwritten] {where}";
}
