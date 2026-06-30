using System.Net.Http.Headers;
using System.Text.Json;

namespace SharpSeek.Server;

/// <summary>The outcome of checking this server's build commit against origin/main.</summary>
/// <param name="LocalCommit">The commit this server was built from, or <c>null</c> when unknown.</param>
/// <param name="RemoteCommit">The latest commit on origin/main, or <c>null</c> when the check could not run.</param>
/// <param name="UpdateAvailable">Whether the build differs from origin/main (and so is out of date).</param>
internal sealed record UpdateStatus(string? LocalCommit, string? RemoteCommit, bool UpdateAvailable);

/// <summary>
/// Checks whether a newer commit exists on origin/main than the one this server was built from, so
/// the agent can be told (via ServerInstructions) to remind the user to update. main is treated as
/// authoritative: any difference means out of date. The check is cached on disk (so it does not hit
/// GitHub on every start), bounded by a short timeout, opt-out via <c>SHARPSEEK_NO_UPDATE_CHECK</c>,
/// and fails silently (offline, rate-limited, GitHub down) so it never blocks or breaks startup.
/// </summary>
internal static class UpdateCheck
{
    private const string Repository = "lassevk/SharpSeek";
    private const string DisableEnvVar = "SHARPSEEK_NO_UPDATE_CHECK";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    public static async Task<UpdateStatus> GetStatusAsync(string? localCommit, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DisableEnvVar))
            || string.IsNullOrEmpty(localCommit))
        {
            return new UpdateStatus(localCommit, null, false);
        }

        string? remote = await GetLatestMainShaAsync(cancellationToken).ConfigureAwait(false);
        bool updateAvailable = remote is not null
            && !string.Equals(remote, localCommit, StringComparison.OrdinalIgnoreCase);

        return new UpdateStatus(localCommit, remote, updateAvailable);
    }

    /// <summary>The notice to append to ServerInstructions when out of date; otherwise <c>null</c>.</summary>
    public static string? Notice(UpdateStatus status) =>
        status.UpdateAvailable
            ? $"SHARPSEEK UPDATE AVAILABLE: this server is built from commit {Short(status.LocalCommit)}, "
              + $"but the latest commit on {Repository} main is {Short(status.RemoteCommit)}. In your first "
              + "reply to the user, remind them to update SharpSeek - git pull, dotnet publish to the "
              + "registered artifacts folder, then restart - so the running build matches main. (server_info "
              + "reports the running build; SHARPSEEK_NO_UPDATE_CHECK silences this check.)"
            : null;

    private static string Short(string? sha) => sha is { Length: >= 7 } ? sha[..7] : sha ?? "unknown";

    private static async Task<string?> GetLatestMainShaAsync(CancellationToken cancellationToken)
    {
        if (ReadCache() is { } cache && DateTime.UtcNow - cache.FetchedUtc < CacheLifetime)
        {
            return cache.Sha;
        }

        string? fetched = await FetchLatestMainShaAsync(cancellationToken).ConfigureAwait(false);
        if (fetched is not null)
        {
            WriteCache(new CacheEntry(fetched, DateTime.UtcNow));
            return fetched;
        }

        // On failure fall back to any cached value, even if stale - a slightly old answer beats none.
        return ReadCache()?.Sha;
    }

    private static async Task<string?> FetchLatestMainShaAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpClient client = new() { Timeout = RequestTimeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SharpSeek-UpdateCheck");
            // This media type makes the commits endpoint return just the SHA as the body.
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github.sha"));

            using HttpResponseMessage response = await client
                .GetAsync($"https://api.github.com/repos/{Repository}/commits/main", cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string body = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))
                .Trim();
            return IsCommitSha(body) ? body : null;
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            return null;
        }
    }

    private static bool IsCommitSha(string value) =>
        value.Length is >= 7 and <= 64
        && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F'));

    private static string CachePath => Path.Combine(Path.GetTempPath(), "sharpseek-update-check.json");

    private static CacheEntry? ReadCache()
    {
        try
        {
            string path = CachePath;
            return File.Exists(path) ? JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteCache(CacheEntry entry)
    {
        try
        {
            File.WriteAllText(CachePath, JsonSerializer.Serialize(entry));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Caching is best-effort; a failure to write just means the next start re-checks.
        }
    }

    private sealed record CacheEntry(string Sha, DateTime FetchedUtc);
}
