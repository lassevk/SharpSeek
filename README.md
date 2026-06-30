# SharpSeek

A Roslyn-native [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server for
navigating .NET codebases — built specifically to see code that generic, LSP-based code
intelligence tools cannot.

## Why this exists

Generic multi-language code intelligence (for example Serena, built on language-neutral LSP
servers) works well for ordinary C# symbols, but it has one concrete blind spot: **references
that live in source-generated code**. The clearest example is Blazor. A handler referenced only
from `.razor` markup, like:

```razor
<a @onclick="ShowPreviousYearAsync">…</a>
```

is wired up inside the Razor source generator's `BuildRenderTree` output. A generic "find
references" query against the hand-written documents never sees that usage, so the handler looks
unused even though it is not.

## The core insight

This gap is not fundamental to code intelligence — it is specific to how a generic LSP-based
tool asks the question. A Roslyn-native tool closes it:

- `MSBuildWorkspace` loads the project **with its analyzers and source generators**.
- The resulting `Compilation` contains the generated documents **in memory** — no `obj/`
  juggling, no duplicate `partial class` problems.
- `SymbolFinder.FindReferencesAsync(...)` traverses **all** documents, including generated
  ones, so it finds the usage inside `BuildRenderTree`.
- The Razor generator emits `#line` mappings, so `SyntaxTree.GetMappedLineSpan()` maps the hit
  back to the original location in the `.razor` file.

The headline capability: *"find references to this method, including the `.razor` that wires up
`@onclick`, reported on the `.razor` line"* — something a generic LSP cannot do cleanly.

## Status

A working MCP server with 17 read-only navigation/analysis tools plus `activate_project` (see
[Running as an MCP server](#running-as-an-mcp-server)). It analyses a whole **solution**,
discovered from the session folder (so it follows whatever .NET repo you are working in), with a
warm + incremental workspace that picks up on-disk edits and a guard against the Roslyn↔SDK
version skew that silently breaks the Razor generator. Every tool has an automated test against an
in-repo [fixture](#testing).

The founding capability is proven end to end: `find_references` loads a Blazor project through
`MSBuildWorkspace` on .NET 10 (with the Razor generator running), finds the `@onclick`-only
handler `ShowPreviousYearAsync` inside the generated `BuildRenderTree`, and maps it back to the
original `.razor` line — a reference a generic LSP-based tool misses. The same generated-code
awareness flows through the other tools (e.g. `find_unused_symbols` does not flag a handler used
only from `.razor`).

Not yet implemented: write operations (`rename_symbol`, `apply_code_fix`).

## Architecture

| Project | Role |
| --- | --- |
| `src/SharpSeek.Engine` | Class library. Roslyn + `MSBuildWorkspace` loading, `find_references`. |
| `src/SharpSeek.Server` | MCP server (stdio) exposing the tools; `diagnose` subcommand for manual CLI checks. |
| `tests/SharpSeek.Engine.Tests` | xUnit v3 tests that load the fixture and assert behaviour. |
| `tests/fixtures/SampleBlazorApp` | Stable Blazor (Razor Class Library) fixture — the pinned test target. |

## Running as an MCP server

The server speaks MCP over stdio and operates on one .NET **solution** (or project). By default
it **discovers** the target from the session — it walks up from the working directory the client
launched it in (preferring a `.sln`/`.slnx`, else a `.csproj`) — so it follows whatever folder you
are working in, like a language server. You can override discovery with `--project <path>` or the
`SHARPSEEK_PROJECT` environment variable, and switch targets at runtime with the `activate_project`
tool. The solution is loaded once and kept warm; on-disk edits are picked up automatically —
source/`.razor` changes are applied incrementally in memory (re-running generators), while
structural or `.csproj`/`.sln` changes trigger a reload.

Because it follows the session folder, a single **global** registration works across all your
.NET repos (and the server instructions tell agents to use it only for .NET/C# projects):

```json
{
  "mcpServers": {
    "sharpseek": {
      "command": "D:\\Dev\\SharpSeek\\artifacts\\server\\SharpSeek.Server.exe"
    }
  }
}
```

Register it globally in Claude Code with:

```sh
claude mcp add sharpseek -s user -- "D:\Dev\SharpSeek\artifacts\server\SharpSeek.Server.exe"
```

(Add `--project <path>` after the executable to pin a fixed solution instead of following the
folder.) Build the executable first with `dotnet publish src/SharpSeek.Server -c Release -o artifacts/server`.

On **macOS/Linux**, publish on that machine (the apphost is OS-specific) and register via the
portable DLL:

```sh
dotnet publish src/SharpSeek.Server -c Release -o artifacts/server
claude mcp add sharpseek -s user -- dotnet "$(pwd)/artifacts/server/SharpSeek.Server.dll"
```

Any machine needs the **.NET 10 SDK**; the pinned Roslyn (see
[the SDK/Roslyn coupling](#the-net-sdk--roslyn-coupling-important)) must be compatible with that
SDK. If it isn't, the startup guard reports a clear error rather than failing silently — bump the
pinned versions to match.

### Staying up to date

The server records the git commit it was built from (see `server_info`) and, at startup, compares
it to the latest commit on `origin/main` (GitHub API, cached for ~6 h, short timeout, silent on
failure). When the running build is behind, it appends a notice to its server instructions so the
agent reminds you to rebuild and redeploy. Disable with `SHARPSEEK_NO_UPDATE_CHECK`, or run
`SharpSeek.Server check-update` to see the comparison yourself.

### Logging

All logging goes to **stderr** (stdout is the MCP channel); your MCP client captures it. The
default level is `Information` — set `Logging__LogLevel__Default=Warning` to quieten it.

For a persistent log, enable optional **file logging** with `--log-file <path>` or the
`SHARPSEEK_LOG_FILE` environment variable. The file is appended to and rotates to a single
`.old` backup at ~10 MB, so it stays bounded on a long-running server:

```sh
dotnet run --project src/SharpSeek.Server -- --project <path-to-csproj> --log-file sharpseek.log
```

Tools:

| Tool | Description |
| --- | --- |
| `find_references` | All references to a symbol (by name), including those in source-generated code, each mapped back to its original location and tagged `handwritten` or `generated`. Each reference also carries how it was used where Roslyn already knows it: `usage` (`read`/`write`/`readwrite` for fields, properties, locals, parameters), `assignedConstant` for writes of a constant value (so "set to `true`/`null`" is answerable directly; absent — never null — when the assigned value is not constant), `assignedType` for the static type assigned at a write (implicit conversions peeled, so `int` vs `int?` distinguishes a provably non-null write from a possibly-null one for value types), `role` for the distinctive syntactic forms (`nameof`/`typeof`/`construction`/`attribute`/`invocation`/`methodGroup`), plus `implicit`, `alias`, and `candidateReason` when they apply; the symbol's `kind` is reported too. |
| `go_to_definition` | Declaration location(s) of a symbol (by name). |
| `get_symbol_range` | Full declaration line range (start/end) of a symbol so you can read just that span; name may be simple, `Type.Member`, or fully-qualified. Covers leading XML-doc comment, attributes, signature, and body. |
| `get_symbol_source` | The declaration's source text directly (one round trip), including source-generated bodies (e.g. Razor `BuildRenderTree`) that a file reader cannot open. Same naming/coverage as `get_symbol_range`. |
| `find_implementations` | Implementations of an interface or abstract member (by name). |
| `type_hierarchy` | Base types and derived types of a type (by name). |
| `search_symbols` | Workspace symbol search by name pattern (substring / camel-case). |
| `get_symbol_info` | Kind, accessibility, containing type, XML doc, and location(s) of a symbol. |
| `find_symbol_at_position` | Resolve the symbol at a file + line/column (editor-style location). |
| `document_outline` | Types and members declared in a file, with kind and line. |
| `find_literal_usages` | Where a string/number/char literal appears, including in generated code. |
| `find_unused_symbols` | Members with no references (dead code) — counts generated usages, so `@onclick`-only handlers are not false-flagged, and honors JetBrains.Annotations (`[PublicAPI]`, `[UsedImplicitly]`, `[MeansImplicitlyUsed]`, matched by attribute name) so intentionally-implicit members are not flagged. `scope=private` (default, safe) or `scope=solution` (also public/internal unused across the solution; verify — may be public API/reflection). |
| `get_diagnostics` | Compiler errors/warnings for the project or a file, filterable by severity. |
| `call_hierarchy` | Incoming callers (incl. from generated code) and outgoing calls of a method. |
| `find_overrides` | Members that override a member, and the members it overrides. |
| `solution_overview` | The solution's projects with names, languages, document/generated counts, references. |
| `project_dependencies` | Per project: declared vs actually-used references (flags declared-but-unused ones) and dependents (who uses it). |
| `get_generated_document` | The C# a generator produced for a file (e.g. Razor `BuildRenderTree`). |
| `list_generators` | Which source generators ran and how many documents each produced. |
| `activate_project` | Switch the analysed solution/project to a given path or directory. |
| `server_info` | The server's own build identity — the git commit it was built from, dirty flag, build time, version. Compare `commit` to `git rev-parse HEAD` to confirm the running build; needs no activated project. |

## Testing

Tests run against an **in-repo fixture** (`tests/fixtures/SampleBlazorApp`) rather than any
external project, so results are deterministic and the build is portable. The fixture is loaded
at runtime via `MSBuildWorkspace` (by path), not referenced as an assembly.

```sh
dotnet test
```

The headline test loads the fixture, runs `find_references` for `ShowPreviousYearAsync`, and
asserts the only usage is found in generated code and mapped back to `Calendar.razor:3`.

## The .NET SDK / Roslyn coupling (important)

To instantiate and run the SDK's own source generators (especially the Razor generator), our
process must load a Roslyn **compiler** that is the same version as — or newer than — the one the
SDK's generators were built against. The .NET SDK ships a specific Roslyn build that is **not**
published to nuget.org.

For that reason the Roslyn packages are pinned (see
[`src/SharpSeek.Engine`](src/SharpSeek.Engine/SharpSeek.Engine.csproj)) to the build that matches
the installed SDK, restored from the `dotnet-tools` feed configured in
[`nuget.config`](nuget.config):

| Component | Value (dotnet 10.0.301) |
| --- | --- |
| SDK Roslyn build | `5.6.0-2.26270.133` |
| Razor compiler requires | `Microsoft.CodeAnalysis` ≥ `5.5.0.0` |
| nuget.org latest stable | `5.3.0` (too old — Razor generator fails to load) |

If the SDK is upgraded and its generators start requiring a newer Roslyn than the pinned
version, these package versions must be bumped to match.

**This failure mode is now guarded.** When the Razor generator is present but fails to load
(the skew), [`GeneratorHealthCheck`](src/SharpSeek.Engine/GeneratorHealthCheck.cs) detects it: the
MCP server throws a clear, actionable error on the first request, and `diagnose` prints a warning
— instead of silently returning results with the `.razor` references missing.

To bump the pin after an SDK upgrade, find the SDK's Roslyn build version and set the
`Microsoft.CodeAnalysis.*` versions in the Engine project to match:

```sh
# prints e.g. 5.6.0-2.26270.133
dotnet --version  # identify the SDK, then inspect its Roslyn:
#   <sdk-install>/sdk/<version>/Roslyn/bincore/Microsoft.CodeAnalysis.dll  (file version)
```

`MSBuildLocator` is registered against the newest installed .NET SDK before any MSBuild type is
touched (see [`MSBuildRegistration`](src/SharpSeek.Engine/MSBuildRegistration.cs)).

## Manual dogfooding against a real app (optional)

Automated tests use the in-repo fixture. As an *optional* real-world smoke test, the harness can
also be pointed at any .NET 10 Blazor project on disk — for example **SskWeb** at
`D:\Dev\SskWeb\sskweb-claude`, where `ShowPreviousYearAsync` is defined in
`Calendar.razor.cs` and used only from `Calendar.razor:11` via `@onclick`:

```sh
dotnet run --project src/SharpSeek.Server -- diagnose "D:\Dev\SskWeb\sskweb-claude\SskWeb\SskWeb.csproj"
```

This `diagnose` subcommand prints results to the console (it does not start the MCP server) and
is a convenience for manual checks only; the repository does not depend on that project existing.
