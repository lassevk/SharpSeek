using SharpSeek.Engine;

namespace SharpSeek.Server;

/// <summary>
/// CLI diagnostic path: loads a project and runs find_references from the command line, for
/// manual checks against real projects without going through the MCP transport.
/// </summary>
internal static class Diagnostics
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 1)
        {
            await Console.Error.WriteLineAsync(
                "Usage: SharpSeek.Server diagnose <path-to-csproj> [symbolName]");
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

        string? generatorProblem = GeneratorHealthCheck.DetectRazorGeneratorSkew(loadResult.Project.Solution);
        if (generatorProblem is not null)
        {
            Console.WriteLine($"\nWARNING: {generatorProblem}");
        }

        Console.WriteLine($"\nFinding references to '{symbolName}'...\n");
        ReferenceFinder finder = new();
        IReadOnlyList<SymbolReferences> results =
            await finder.FindReferencesAsync(loadResult.Project.Solution, symbolName);

        if (results.Count == 0)
        {
            Console.WriteLine($"No source-declared symbol named '{symbolName}' was found.");
            return 1;
        }

        foreach (SymbolReferences result in results)
        {
            Console.WriteLine($"Symbol: {result.SymbolDisplay} [{result.SymbolKind}]");

            Console.WriteLine($"  Definitions ({result.Definitions.Count}):");
            foreach (ReferenceLocationInfo definition in result.Definitions)
            {
                Console.WriteLine($"    {Format(definition)}");
            }

            Console.WriteLine($"  References ({result.References.Count}):");
            foreach (ReferenceInfo reference in result.References)
            {
                Console.WriteLine($"    {Format(reference)}");
            }
        }

        return 0;
    }

    private static string Format(ReferenceLocationInfo location)
    {
        string where = $"{location.FilePath}:{location.Line}:{location.Column}";
        if (location.Origin == ReferenceOrigin.Generated)
        {
            string generatedName = Path.GetFileName(location.GeneratedFilePath ?? "<generated>");
            return $"[generated] {where}  (in {generatedName})";
        }

        return $"[handwritten] {where}";
    }

    private static string Format(ReferenceInfo reference)
    {
        string line = Format(reference.Location);

        List<string> tags = [];
        if (reference.Usage is { } usage)
        {
            tags.Add(usage.ToString().ToLowerInvariant());
        }

        if (reference.AssignedConstant is { } constant)
        {
            tags.Add($"= {Render(constant.Value)}");
        }

        if (reference.Role is { } role)
        {
            tags.Add(role.ToString());
        }

        if (reference.IsImplicit)
        {
            tags.Add("implicit");
        }

        if (reference.Alias is { } alias)
        {
            tags.Add($"alias={alias}");
        }

        if (reference.CandidateReason is { } candidateReason)
        {
            tags.Add($"candidate={candidateReason}");
        }

        return tags.Count == 0 ? line : $"{line}  {{{string.Join(", ", tags)}}}";
    }

    private static string Render(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        _ => value.ToString() ?? "",
    };
}
