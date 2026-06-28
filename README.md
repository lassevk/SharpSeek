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

## Status: walking skeleton

The core idea is proven end to end and exposed over MCP. The `find_references` engine loads a
Blazor project through `MSBuildWorkspace` on .NET 10 (with the Razor generator running), finds
the `@onclick`-only handler `ShowPreviousYearAsync` inside the generated `BuildRenderTree`, and
maps it back to the original `.razor` line. It is reachable as the `find_references` MCP tool
(see [Running as an MCP server](#running-as-an-mcp-server)) and verified by an automated test
against an in-repo [fixture](#testing).

## Architecture

| Project | Role |
| --- | --- |
| `src/SharpSeek.Engine` | Class library. Roslyn + `MSBuildWorkspace` loading, `find_references`. |
| `src/SharpSeek.Server` | MCP server (stdio) exposing the tools; `diagnose` subcommand for manual CLI checks. |
| `tests/SharpSeek.Engine.Tests` | xUnit v3 tests that load the fixture and assert behaviour. |
| `tests/fixtures/SampleBlazorApp` | Stable Blazor (Razor Class Library) fixture — the pinned test target. |

## Running as an MCP server

The server speaks MCP over stdio. It operates on a single project, configured with `--project`
or the `SHARPSEEK_PROJECT` environment variable. The project is loaded once and kept warm;
on-disk edits are picked up automatically — source/`.razor` changes are applied incrementally
in memory (re-running generators), while structural or `.csproj` changes trigger a reload.

```sh
dotnet run --project src/SharpSeek.Server -- --project <path-to-csproj>
```

Example MCP client registration (e.g. an editor/agent `mcp.json`):

```json
{
  "mcpServers": {
    "sharpseek": {
      "command": "dotnet",
      "args": ["run", "--project", "src/SharpSeek.Server", "--", "--project", "<path-to-csproj>"]
    }
  }
}
```

Tools:

| Tool | Description |
| --- | --- |
| `find_references` | All references to a symbol (by name), including those in source-generated code, each mapped back to its original location and tagged `handwritten` or `generated`. |
| `go_to_definition` | Declaration location(s) of a symbol (by name). |
| `find_implementations` | Implementations of an interface or abstract member (by name). |
| `type_hierarchy` | Base types and derived types of a type (by name). |
| `search_symbols` | Workspace symbol search by name pattern (substring / camel-case). |
| `get_symbol_info` | Kind, accessibility, containing type, XML doc, and location(s) of a symbol. |
| `find_symbol_at_position` | Resolve the symbol at a file + line/column (editor-style location). |
| `document_outline` | Types and members declared in a file, with kind and line. |
| `find_literal_usages` | Where a string/number/char literal appears, including in generated code. |
| `find_unused_symbols` | Private members with no references (dead code) — counts generated usages, so `@onclick`-only handlers are not false-flagged. |
| `get_diagnostics` | Compiler errors/warnings for the project or a file, filterable by severity. |
| `call_hierarchy` | Incoming callers (incl. from generated code) and outgoing calls of a method. |
| `find_overrides` | Members that override a member, and the members it overrides. |
| `project_overview` | Project name, language, document/generated counts, references. |
| `get_generated_document` | The C# a generator produced for a file (e.g. Razor `BuildRenderTree`). |
| `list_generators` | Which source generators ran and how many documents each produced. |

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
