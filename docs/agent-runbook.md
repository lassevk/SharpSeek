# Agent runbook (notes to self)

Operational notes for a Claude agent working in this repo. The owner runs SharpSeek on **multiple
machines** by cloning this repo on each, building, verifying the SDK, installing the server, and
registering it globally as an MCP. This file is the memory that survives across those fresh clones;
the user-scoped auto-memory does **not** travel between machines, so durable cross-machine facts
belong here.

## Setting up SharpSeek on a fresh machine

The full reference lives in [`README.md`](../README.md); this is the short sequence.

1. **Verify the SDK.** Needs the **.NET 10 SDK** (`dotnet --version`). The pinned Roslyn must be
   compatible with the installed SDK — see *The .NET SDK / Roslyn coupling* in the README. If it
   isn't, the startup guard (`GeneratorHealthCheck`) reports a clear error instead of failing
   silently; bump the pinned `Microsoft.CodeAnalysis.*` versions in
   [`src/SharpSeek.Engine`](../src/SharpSeek.Engine/SharpSeek.Engine.csproj) to match the SDK's
   Roslyn build and re-test.
2. **Build + verify.** `dotnet test` (loads the in-repo fixture; green means the workspace +
   generators + Roslyn pin all work on this machine). For a real-world smoke test, point
   `diagnose` at a project on disk (see the README's dogfooding section).
3. **Publish the server.** `dotnet publish src/SharpSeek.Server -c Release -o artifacts/server`.
4. **Register globally** (a single global registration follows whatever folder the session is in,
   so it works across all .NET repos):
   - Windows: `claude mcp add sharpseek -s user -- "<repo>/artifacts/server/SharpSeek.Server.exe"`
   - macOS/Linux: publish on that machine (the apphost is OS-specific), then
     `claude mcp add sharpseek -s user -- dotnet "<repo>/artifacts/server/SharpSeek.Server.dll"`

Derive `<repo>` from the actual clone location on this machine; do not assume a fixed path.

After changing `ServerInstructions` or any server behaviour, the server must be **re-published and
re-registered/restarted** for the change to take effect in live sessions.

**Verify which build is running.** The server embeds the git commit it was built from. Call the
`server_info` tool (or run the executable with the `version` argument) and compare its `commit` to
`git rev-parse HEAD` in this repo — equal means the running binary is built from the checked-out
source. `dirty: true` means it was built from an uncommitted working tree. Prefer this over inferring
the build from whether a new feature appears in tool output.

## Using SharpSeek in other projects

In a session against an arbitrary .NET/C# project where the `sharpseek` MCP is connected, the
server's own `ServerInstructions` already nudge toward using it and toward flagging gaps. Two
durable facts that back that up:

- **Prefer it for .NET/C# navigation** (references, type/call hierarchy, dead code, source-generated
  code) over plain text search.
- **Be critical and capture ideas.** When a navigation/analysis need isn't covered, or a result
  shape is awkward, surface it to the user as a candidate SharpSeek improvement. The
  **`github-lassevk` MCP is registered globally on the owner's machines**, so `lassevk/SharpSeek` is
  reachable from *any* session — with the user's go-ahead, file the issue directly there (follow the
  repo's issue conventions: reference issue numbers in commits, `Closes`/`Refs`). You do not have to
  wait until you are back in this repo.

## Repo conventions worth remembering

- English everywhere (identifiers, comments, human-visible text).
- Treat compiler warnings as errors.
- Split work into logical commits; reference GitHub issues (`Closes #N` / `Refs #N`) before the
  `Co-Authored-By` trailer.
- Tests use **xUnit v3**.
- Challenge the user's assumptions; solve the issue as presented, discuss likely future problems
  rather than pre-solving them.
