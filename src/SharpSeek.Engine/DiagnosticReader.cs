using System.Globalization;

using Microsoft.CodeAnalysis;

namespace SharpSeek.Engine;

/// <summary>A single compiler diagnostic.</summary>
public sealed record DiagnosticInfo(
    string Id,
    string Severity,
    string Message,
    ReferenceLocationInfo? Location);

/// <summary>
/// Reads compiler diagnostics (errors, warnings, etc.) from a loaded project's compilation,
/// including diagnostics in source-generated code, mapped back to their original location.
/// </summary>
public sealed class DiagnosticReader
{
    /// <summary>
    /// Returns compiler diagnostics at or above <paramref name="minimumSeverity"/> (default
    /// <c>Warning</c>), optionally restricted to a single file.
    /// </summary>
    /// <param name="solution">A loaded solution.</param>
    /// <param name="filePath">When set, only diagnostics in this file (matched by path suffix).</param>
    /// <param name="minimumSeverity">One of <c>error</c>, <c>warning</c>, <c>info</c>, <c>hidden</c>.</param>
    /// <param name="cancellationToken">Token used to cancel the work.</param>
    public async Task<IReadOnlyList<DiagnosticInfo>> GetDiagnosticsAsync(
        Solution solution,
        string? filePath = null,
        string? minimumSeverity = null,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> handwrittenPaths = LocationDescriptor.HandwrittenPaths(solution);
        DiagnosticSeverity minimum = ParseSeverity(minimumSeverity);
        string? fileFilter = filePath is null ? null : filePath.Replace('\\', '/');

        List<(Diagnostic Diagnostic, ReferenceLocationInfo? Location)> matched = [];
        foreach (Project project in solution.Projects)
        {
            Compilation? compilation = await project.GetCompilationAsync(cancellationToken)
                .ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            foreach (Diagnostic diagnostic in compilation.GetDiagnostics(cancellationToken))
            {
                if (diagnostic.Severity < minimum)
                {
                    continue;
                }

                ReferenceLocationInfo? location = null;
                if (diagnostic.Location is { IsInSource: true, SourceTree: { } tree })
                {
                    location = LocationDescriptor.Describe(tree, diagnostic.Location.SourceSpan, handwrittenPaths);
                }

                if (fileFilter is not null
                    && (location is null
                        || !location.FilePath.Replace('\\', '/')
                            .EndsWith(fileFilter, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                matched.Add((diagnostic, location));
            }
        }

        return
        [
            .. matched
                .OrderByDescending(entry => entry.Diagnostic.Severity)
                .ThenBy(entry => entry.Location?.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Location?.Line ?? 0)
                .Select(entry => new DiagnosticInfo(
                    entry.Diagnostic.Id,
                    entry.Diagnostic.Severity.ToString(),
                    entry.Diagnostic.GetMessage(CultureInfo.InvariantCulture),
                    entry.Location))
        ];
    }

    private static DiagnosticSeverity ParseSeverity(string? severity) =>
        severity?.Trim().ToLowerInvariant() switch
        {
            "error" => DiagnosticSeverity.Error,
            "info" or "information" => DiagnosticSeverity.Info,
            "hidden" => DiagnosticSeverity.Hidden,
            _ => DiagnosticSeverity.Warning,
        };
}
