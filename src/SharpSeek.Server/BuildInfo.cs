using System.Reflection;

namespace SharpSeek.Server;

/// <summary>
/// The build identity of this server: the git commit it was built from (captured at build time and
/// embedded in the assembly), whether the working tree was dirty then, the build timestamp, and the
/// informational version. This describes the server binary itself, not any analysed project.
/// </summary>
/// <param name="Commit">The full git commit SHA, or <c>null</c> when it could not be captured at build time.</param>
/// <param name="ShortCommit">The abbreviated commit SHA, or <c>null</c>.</param>
/// <param name="Dirty">Whether the working tree had uncommitted changes at build time, or <c>null</c> when unknown.</param>
/// <param name="BuildTimeUtc">The assembly's last-write time (UTC), or <c>null</c> when it could not be read.</param>
/// <param name="Version">The assembly informational version, or <c>null</c>.</param>
internal sealed record ServerBuildInfo(
    string? Commit,
    string? ShortCommit,
    bool? Dirty,
    DateTime? BuildTimeUtc,
    string? Version);

/// <summary>Reads the embedded build identity of this server assembly.</summary>
internal static class BuildInfo
{
    /// <summary>The build identity of the running server, read once.</summary>
    public static ServerBuildInfo Current { get; } = Read();

    private static ServerBuildInfo Read()
    {
        Assembly assembly = typeof(BuildInfo).Assembly;

        IReadOnlyList<AssemblyMetadataAttribute> metadata =
            [.. assembly.GetCustomAttributes<AssemblyMetadataAttribute>()];

        string? commit = Value(metadata, "GitCommit");
        string? dirtyText = Value(metadata, "GitDirty");
        bool? dirty = dirtyText is null ? null : string.Equals(dirtyText, "true", StringComparison.OrdinalIgnoreCase);

        string? shortCommit = commit is { Length: >= 7 } ? commit[..7] : commit;

        DateTime? buildTimeUtc = null;
        string location = assembly.Location;
        if (!string.IsNullOrEmpty(location) && File.Exists(location))
        {
            buildTimeUtc = File.GetLastWriteTimeUtc(location);
        }

        string? version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return new ServerBuildInfo(commit, shortCommit, dirty, buildTimeUtc, version);
    }

    private static string? Value(IReadOnlyList<AssemblyMetadataAttribute> metadata, string key)
    {
        foreach (AssemblyMetadataAttribute attribute in metadata)
        {
            if (string.Equals(attribute.Key, key, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(attribute.Value))
            {
                return attribute.Value;
            }
        }

        return null;
    }
}
