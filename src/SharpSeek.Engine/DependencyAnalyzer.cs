using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpSeek.Engine;

/// <summary>
/// A project's dependencies on other projects in the solution: those declared via
/// <c>&lt;ProjectReference&gt;</c>, those actually used (a symbol from the other project is
/// referenced anywhere, including in generated code), and declared references that are never used.
/// </summary>
public sealed record ProjectDependencies(
    string Project,
    IReadOnlyList<string> DeclaredReferences,
    IReadOnlyList<string> UsedReferences,
    IReadOnlyList<string> UnusedReferences,
    IReadOnlyList<string> Dependents);

/// <summary>
/// Computes usage-based project dependencies across a solution by resolving which other projects'
/// symbols each project actually references — surfacing, in particular, declared project references
/// that are never used.
/// </summary>
public sealed class DependencyAnalyzer
{
    public async Task<IReadOnlyList<ProjectDependencies>> AnalyzeAsync(
        Solution solution,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> assemblyToProject = new(StringComparer.OrdinalIgnoreCase);
        foreach (Project project in solution.Projects)
        {
            assemblyToProject[project.AssemblyName ?? project.Name] = project.Name;
        }

        Dictionary<string, HashSet<string>> declaredByProject = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<string>> usedByProject = new(StringComparer.OrdinalIgnoreCase);
        foreach (Project project in solution.Projects)
        {
            HashSet<string> declared = new(StringComparer.OrdinalIgnoreCase);
            foreach (ProjectReference reference in project.ProjectReferences)
            {
                if (solution.GetProject(reference.ProjectId)?.Name is { } name)
                {
                    declared.Add(name);
                }
            }

            HashSet<string> used =
                await CollectUsedProjectsAsync(project, assemblyToProject, cancellationToken)
                    .ConfigureAwait(false);
            used.Remove(project.Name);

            declaredByProject[project.Name] = declared;
            usedByProject[project.Name] = used;
        }

        List<ProjectDependencies> results = [];
        foreach (Project project in solution.Projects)
        {
            HashSet<string> declared = declaredByProject[project.Name];
            HashSet<string> used = usedByProject[project.Name];

            // Dependents: projects that actually use this one (reverse of the used edges).
            IReadOnlyList<string> dependents = Sorted(usedByProject
                .Where(entry => entry.Value.Contains(project.Name))
                .Select(entry => entry.Key));

            results.Add(new ProjectDependencies(
                project.Name,
                Sorted(declared),
                Sorted(used),
                Sorted(declared.Where(name => !used.Contains(name))),
                dependents));
        }

        return results;
    }

    private static async Task<HashSet<string>> CollectUsedProjectsAsync(
        Project project,
        Dictionary<string, string> assemblyToProject,
        CancellationToken cancellationToken)
    {
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
        Compilation? compilation = await project.GetCompilationAsync(cancellationToken)
            .ConfigureAwait(false);
        if (compilation is null)
        {
            return used;
        }

        string ownAssembly = project.AssemblyName ?? project.Name;
        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            SemanticModel model = compilation.GetSemanticModel(tree);
            SyntaxNode root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            foreach (SimpleNameSyntax name in root.DescendantNodes().OfType<SimpleNameSyntax>())
            {
                IAssemblySymbol? assembly = model.GetSymbolInfo(name, cancellationToken).Symbol?.ContainingAssembly;
                if (assembly is null
                    || string.Equals(assembly.Name, ownAssembly, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (assemblyToProject.TryGetValue(assembly.Name, out string? projectName))
                {
                    used.Add(projectName);
                }
            }
        }

        return used;
    }

    private static IReadOnlyList<string> Sorted(IEnumerable<string> names) =>
        [.. names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];
}
