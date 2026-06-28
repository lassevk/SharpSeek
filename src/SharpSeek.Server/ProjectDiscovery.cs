namespace SharpSeek.Server;

/// <summary>
/// Discovers the .NET solution or project to analyse from a starting directory, walking up the
/// directory tree. A solution (<c>.slnx</c>/<c>.sln</c>) is preferred over a single project.
/// </summary>
internal static class ProjectDiscovery
{
    public static string? Discover(string startDirectory)
    {
        DirectoryInfo? directory = SafeDirectory(startDirectory);
        while (directory is { Exists: true })
        {
            string? found = FindInDirectory(directory);
            if (found is not null)
            {
                return found;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string? FindInDirectory(DirectoryInfo directory)
    {
        foreach (string pattern in (string[])[".slnx", ".sln", ".csproj"])
        {
            FileInfo? match = directory
                .EnumerateFiles("*" + pattern, SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (match is not null)
            {
                return match.FullName;
            }
        }

        return null;
    }

    private static DirectoryInfo? SafeDirectory(string path)
    {
        try
        {
            return new DirectoryInfo(path);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
