using Microsoft.CodeAnalysis;

namespace SharpSeek.Engine;

/// <summary>High-level information about a loaded project.</summary>
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

/// <summary>Reports high-level structure of a loaded project.</summary>
public sealed class ProjectInspector
{
    public async Task<ProjectOverview> GetOverviewAsync(
        Project project,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Document> generated = await project
            .GetSourceGeneratedDocumentsAsync(cancellationToken)
            .ConfigureAwait(false);

        Solution solution = project.Solution;
        List<string> projectReferences =
        [
            .. project.ProjectReferences
                .Select(reference => solution.GetProject(reference.ProjectId)?.Name)
                .Where(name => name is not null)
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        ];

        return new ProjectOverview(
            project.Name,
            project.AssemblyName,
            project.Language,
            project.FilePath,
            project.Documents.Count(),
            project.AdditionalDocuments.Count(),
            generated.Count(),
            project.MetadataReferences.Count,
            projectReferences);
    }
}
