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

The core idea is proven end to end. The `find_references` engine loads a Blazor project through
`MSBuildWorkspace` on .NET 10 (with the Razor generator running), finds the `@onclick`-only
handler `ShowPreviousYearAsync` inside the generated `BuildRenderTree`, and maps it back to the
original `.razor` line. This is verified by an automated test against an in-repo
[fixture](#testing). What remains is exposing it as an MCP tool (the host wiring).

## Architecture

| Project | Role |
| --- | --- |
| `src/SharpSeek.Engine` | Class library. Roslyn + `MSBuildWorkspace` loading, `find_references`. |
| `src/SharpSeek.Server` | Host process. Currently a verification harness; becomes the MCP host. |
| `tests/SharpSeek.Engine.Tests` | xUnit v3 tests that load the fixture and assert behaviour. |
| `tests/fixtures/SampleBlazorApp` | Stable Blazor (Razor Class Library) fixture — the pinned test target. |

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

`MSBuildLocator` is registered against the newest installed .NET SDK before any MSBuild type is
touched (see [`MSBuildRegistration`](src/SharpSeek.Engine/MSBuildRegistration.cs)).

## Manual dogfooding against a real app (optional)

Automated tests use the in-repo fixture. As an *optional* real-world smoke test, the harness can
also be pointed at any .NET 10 Blazor project on disk — for example **SskWeb** at
`D:\Dev\SskWeb\sskweb-claude`, where `ShowPreviousYearAsync` is defined in
`Calendar.razor.cs` and used only from `Calendar.razor:11` via `@onclick`:

```sh
dotnet run --project src/SharpSeek.Server -- "D:\Dev\SskWeb\sskweb-claude\SskWeb\SskWeb.csproj"
```

This is a convenience for manual checks only; the repository does not depend on that project
existing.
