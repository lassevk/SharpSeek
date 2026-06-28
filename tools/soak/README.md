# Soak / longevity test

`soak.py` exercises the SharpSeek MCP server the way a long-running session would, to check for
memory leaks and process accumulation that unit tests can't catch.

It:

1. builds the server (Release) and makes an **isolated temp copy** of the sample fixture (the
   repository is never modified);
2. launches the real server over MCP (stdio) and drives a mix of tool calls for many iterations;
3. periodically applies **incremental edits** (file content changes → in-memory refresh) and
   **structural reloads** (touches the `.csproj` → full reload);
4. samples the server's working set and the number of descendant `dotnet` processes over time.

## Run

```sh
python tools/soak/soak.py [iterations] [reload_every]
# examples
python tools/soak/soak.py 150        # default: ~3 reloads
python tools/soak/soak.py 200 20     # reload-heavy: ~10 reloads
```

Requires a .NET 10 SDK on PATH and Windows PowerShell (used to sample process memory).

## What to look for

- **Descendant `dotnet` processes** should stay near 0. Growth here means MSBuild BuildHost /
  worker processes are accumulating (the reason node reuse is disabled in `MSBuildRegistration`).
- **Working set** is expected to rise from cold to a warm steady state and then stay in a band
  (GC reclaims; the OS does not return all memory immediately). A continuous per-reload climb
  would indicate workspaces/compilations being retained across reloads.

The printed `VERDICT` is a heuristic (final memory < ~2× warm, descendants ≤ 2); always read the
trend, not just the verdict. Numbers scale with the analysed project's size — the tiny fixture
settles around ~250 MB; a large solution will be higher.
