using System.ComponentModel;

using ModelContextProtocol.Server;

namespace SharpSeek.Server;

/// <summary>
/// MCP tools about the server itself, independent of any loaded project.
/// </summary>
[McpServerToolType]
internal sealed class ServerTools
{
    [McpServerTool(Name = "server_info")]
    [Description(
        "Report this SharpSeek server's own build identity: the git commit it was built from " +
        "(commit/shortCommit), whether the working tree was dirty at build time, the build " +
        "timestamp, and version. Use it to confirm the running server matches a given SharpSeek " +
        "source revision - compare 'commit' to `git rev-parse HEAD` in the SharpSeek repo. This is " +
        "about the server binary itself, not the analysed project, and needs no activated project.")]
    public static ServerInfoDto ServerInfo() => ServerInfoDto.From(BuildInfo.Current);
}
