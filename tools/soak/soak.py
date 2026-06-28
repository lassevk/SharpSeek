#!/usr/bin/env python3
"""Longevity / leak soak test for the SharpSeek MCP server.

Drives the real server process over MCP (stdio) with sustained tool calls plus
periodic incremental edits and structural reloads, sampling the server's working
set and descendant process count over time. Runs against an isolated temp copy
of the sample fixture, so nothing in the repository is modified.

Usage:
    python tools/soak/soak.py [iterations]

Requires: a .NET 10 SDK on PATH and Windows PowerShell (for process sampling).
"""

import csv
import io
import json
import os
import queue
import shutil
import subprocess
import sys
import tempfile
import threading
import time

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SERVER_PROJECT = os.path.join(REPO_ROOT, "src", "SharpSeek.Server", "SharpSeek.Server.csproj")
FIXTURE_DIR = os.path.join(REPO_ROOT, "tests", "fixtures", "SampleBlazorApp")


def run(cmd, **kw):
    return subprocess.run(cmd, capture_output=True, text=True, **kw)


def sample_processes():
    """Return {pid: (parent_pid, name, working_set_bytes)} for all dotnet.exe."""
    ps = run([
        "powershell", "-NoProfile", "-Command",
        "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | "
        "Select-Object ProcessId,ParentProcessId,WorkingSetSize | ConvertTo-Csv -NoTypeInformation",
    ])
    result = {}
    for row in csv.DictReader(io.StringIO(ps.stdout)):
        try:
            result[int(row["ProcessId"])] = (
                int(row["ParentProcessId"]), int(row["WorkingSetSize"]))
        except (ValueError, KeyError):
            continue
    return result


def descendants(pid, procs):
    """Count dotnet.exe processes in the tree rooted at pid (excluding pid itself)."""
    children = {}
    for p, (parent, _ws) in procs.items():
        children.setdefault(parent, []).append(p)
    seen, stack = set(), list(children.get(pid, []))
    while stack:
        cur = stack.pop()
        if cur in seen:
            continue
        seen.add(cur)
        stack.extend(children.get(cur, []))
    return len(seen)


class McpClient:
    def __init__(self, proc):
        self._proc = proc
        self._responses = {}
        self._lock = threading.Lock()
        self._next_id = 1
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self):
        for line in self._proc.stdout:
            line = line.strip()
            if not line:
                continue
            try:
                msg = json.loads(line)
            except json.JSONDecodeError:
                continue
            if "id" in msg:
                with self._lock:
                    self._responses[msg["id"]] = msg

    def _send(self, obj):
        self._proc.stdin.write(json.dumps(obj) + "\n")
        self._proc.stdin.flush()

    def notify(self, method, params=None):
        self._send({"jsonrpc": "2.0", "method": method, "params": params or {}})

    def call(self, method, params=None, timeout=60):
        with self._lock:
            rid = self._next_id
            self._next_id += 1
        self._send({"jsonrpc": "2.0", "id": rid, "method": method, "params": params or {}})
        deadline = time.time() + timeout
        while time.time() < deadline:
            with self._lock:
                if rid in self._responses:
                    return self._responses.pop(rid)
            time.sleep(0.02)
        raise TimeoutError(f"No response to {method} (id {rid})")


def main():
    iterations = int(sys.argv[1]) if len(sys.argv) > 1 else 150
    reload_every = int(sys.argv[2]) if len(sys.argv) > 2 else 50
    edit_every, sample_every = 15, 10

    # Build the server once.
    print("Building server (Release)...")
    build = run(["dotnet", "build", SERVER_PROJECT, "-c", "Release", "-v", "quiet"])
    if build.returncode != 0:
        print(build.stdout, build.stderr)
        sys.exit("Build failed.")
    dll = os.path.join(
        REPO_ROOT, "src", "SharpSeek.Server", "bin", "Release", "net10.0", "SharpSeek.Server.dll")

    # Isolated temp copy of the fixture (so the repo is never touched), then restore it.
    workdir = tempfile.mkdtemp(prefix="sharpseek-soak-")
    fixture = os.path.join(workdir, "SampleBlazorApp")
    shutil.copytree(FIXTURE_DIR, fixture,
                    ignore=shutil.ignore_patterns("bin", "obj"))
    csproj = os.path.join(fixture, "SampleBlazorApp.csproj")
    print("Restoring temp fixture...")
    if run(["dotnet", "restore", csproj]).returncode != 0:
        sys.exit("Fixture restore failed.")

    edit_file = os.path.join(fixture, "Domain", "Greeting.cs")
    edit_base = open(edit_file, encoding="utf-8").read()

    print(f"Launching server against {csproj}\n")
    proc = subprocess.Popen(
        ["dotnet", dll, "--project", csproj],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
        text=True, bufsize=1)

    samples = []
    try:
        client = McpClient(proc)
        client.call("initialize", {
            "protocolVersion": "2024-11-05", "capabilities": {},
            "clientInfo": {"name": "soak", "version": "1.0"}})
        client.notify("notifications/initialized")

        calls = [
            ("find_references", {"symbolName": "ShowPreviousYearAsync"}),
            ("type_hierarchy", {"typeName": "Dog"}),
            ("search_symbols", {"query": "Greet"}),
            ("get_diagnostics", {}),
            ("find_unused_symbols", {}),
            ("call_hierarchy", {"methodName": "Used"}),
            ("project_overview", {}),
        ]

        edits = 0
        reloads = 0
        for i in range(1, iterations + 1):
            name, args = calls[i % len(calls)]
            resp = client.call("tools/call", {"name": name, "arguments": args})
            if resp.get("error"):
                print(f"  iter {i}: tool {name} error: {resp['error']}")

            if i % edit_every == 0:
                edits += 1
                with open(edit_file, "w", encoding="utf-8") as handle:
                    handle.write(edit_base + f"\npublic class SoakType{edits};\n")

            if i % reload_every == 0:
                reloads += 1
                # Touch the .csproj to trigger the structural-reload path.
                with open(csproj, "a", encoding="utf-8") as handle:
                    handle.write(f"<!-- soak {reloads} -->\n")

            if i % sample_every == 0:
                procs = sample_processes()
                ws = procs.get(proc.pid, (0, 0))[1] / (1024 * 1024)
                kids = descendants(proc.pid, procs)
                samples.append((i, ws, kids))
                print(f"  iter {i:>4}  server={ws:7.1f} MB  descendant dotnet procs={kids}")

        # Settle, then a final sample.
        time.sleep(3)
        procs = sample_processes()
        final_ws = procs.get(proc.pid, (0, 0))[1] / (1024 * 1024)
        final_kids = descendants(proc.pid, procs)
        print(f"\nFinal (after settle)  server={final_ws:7.1f} MB  descendant dotnet procs={final_kids}")
        print(f"Edits applied: {edits}, reloads triggered: {reloads}")

        if samples:
            warm = samples[0][1]
            peak = max(s[1] for s in samples + [(0, final_ws, 0)])
            max_kids = max(s[2] for s in samples + [(0, 0, final_kids)])
            print(f"\nWorking set: warm={warm:.1f} MB, peak={peak:.1f} MB, final={final_ws:.1f} MB")
            print(f"Descendant dotnet procs: max={max_kids}")
            mem_ok = final_ws < warm * 2.0 + 100
            proc_ok = final_kids <= 2
            print("\nVERDICT:",
                  "PASS" if (mem_ok and proc_ok) else "INVESTIGATE",
                  f"(memory {'bounded' if mem_ok else 'GREW'}, "
                  f"processes {'bounded' if proc_ok else 'ACCUMULATED'})")
    finally:
        try:
            proc.stdin.close()
        except Exception:
            pass
        try:
            proc.wait(timeout=5)
        except subprocess.TimeoutExpired:
            proc.kill()
        shutil.rmtree(workdir, ignore_errors=True)


if __name__ == "__main__":
    main()
