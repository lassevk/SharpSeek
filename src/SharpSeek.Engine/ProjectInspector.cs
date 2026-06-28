using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SharpSeek.Engine;

/// <summary>High-level information about a single project.</summary>
public sealed record ProjectOverview(
    string Name,
    string? AssemblyName,
    string Language,
    string? FilePath,
    int DocumentCount,
    int AdditionalDocumentCount,
    int GeneratedDocumentCount,
    int MetadataReferenceCount,
    IReadOnlyList<string> ProjectReferences);

/// <summary>High-level information about the loaded solution.</summary>
public sealed record SolutionOverview(string? FilePath, IReadOnlyList<ProjectOverview> Projects);

/// <summary>The text of a source-generated document.</summary>
public sealed record GeneratedDocumentInfo(string Name, string? FilePath, string Text);

/// <summary>A source generator that ran, and how many documents it produced.</summary>
public sealed record GeneratorInfo(string Assembly, int GeneratorCount, int OutputDocuments);

/// <summary>Reports high-level structure of a loaded solution.</summary>
public sealed class ProjectInspector
{
    public async Task<SolutionOverview> GetOverviewAsync(
        Solution solution,
        CancellationToken cancellationToken = default)
    {
        List<ProjectOverview> projects = [];
        foreach (Project project in solution.Projects)
        {
            IEnumerable<Document> generated = await project
                .GetSourceGeneratedDocumentsAsync(cancellationToken)
                .ConfigureAwait(false);

            List<string> projectReferences =
            [
                .. project.ProjectReferences
                    .Select(reference => solution.GetProject(reference.ProjectId)?.Name)
                    .Where(name => name is not null)
                    .Select(name => name!)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            ];

            projects.Add(new ProjectOverview(
                project.Name,
                project.AssemblyName,
                project.Language,
                project.FilePath,
                project.Documents.Count(),
                project.AdditionalDocuments.Count(),
                generated.Count(),
                project.MetadataReferences.Count,
                projectReferences));
        }

        return new SolutionOverview(
            solution.FilePath,
            [.. projects.OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>
    /// Returns the source-generated documents matching a query, by generated file name/path or by
    /// the originating source file (e.g. <c>Calendar.razor</c> matches <c>Calendar_razor.g.cs</c>).
    /// </summary>
    public async Task<IReadOnlyList<GeneratedDocumentInfo>> GetGeneratedDocumentsAsync(
        Solution solution,
        string query,
        CancellationToken cancellationToken = default)
    {
        string normalized = query.Replace('\\', '/');
        string underscored = normalized.Replace('.', '_');

        List<GeneratedDocumentInfo> results = [];
        foreach (Project project in solution.Projects)
        {
            IEnumerable<Document> generated = await project
                .GetSourceGeneratedDocumentsAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (Document document in generated)
            {
                string path = (document.FilePath ?? string.Empty).Replace('\\', '/');
                bool matches = path.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                    || document.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                    || document.Name.Contains(underscored, StringComparison.OrdinalIgnoreCase);
                if (!matches)
                {
                    continue;
                }

                SourceText text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                results.Add(new GeneratedDocumentInfo(document.Name, document.FilePath, text.ToString()));
            }
        }

        return results;
    }

    /// <summary>Lists the source generators that ran across the solution and their output counts.</summary>
    public async Task<IReadOnlyList<GeneratorInfo>> ListGeneratorsAsync(
        Solution solution,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, (int Generators, int Outputs)> byAssembly =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (Project project in solution.Projects)
        {
            IEnumerable<Document> generated = await project
                .GetSourceGeneratedDocumentsAsync(cancellationToken)
                .ConfigureAwait(false);
            List<string> generatedPaths =
                [.. generated.Select(document => (document.FilePath ?? string.Empty).Replace('\\', '/'))];

            foreach (var reference in project.AnalyzerReferences)
            {
                int generatorCount = reference.GetGenerators(LanguageNames.CSharp).Length;
                if (generatorCount == 0)
                {
                    continue;
                }

                string assembly = Path.GetFileNameWithoutExtension(
                    reference.FullPath ?? reference.Display ?? string.Empty);
                int outputs = generatedPaths.Count(path =>
                    path.Contains(assembly, StringComparison.OrdinalIgnoreCase));

                byAssembly[assembly] = byAssembly.TryGetValue(assembly, out var existing)
                    ? (Math.Max(existing.Generators, generatorCount), existing.Outputs + outputs)
                    : (generatorCount, outputs);
            }
        }

        return
        [
            .. byAssembly
                .Select(entry => new GeneratorInfo(entry.Key, entry.Value.Generators, entry.Value.Outputs))
                .OrderBy(generator => generator.Assembly, StringComparer.OrdinalIgnoreCase)
        ];
    }
}
