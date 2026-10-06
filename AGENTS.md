<!-- agents-install-guide -->
<!-- mcp-name: io.github.bimwright/ipt-mcp -->

# AGENTS.md — ipt-mcp install guide for AI agents

This file is machine-readable install instructions for AI coding agents (Claude Code, Cursor, Cline, VS Code Copilot, Gemini CLI, Antigravity, OpenCode, Codex, and any other stdio MCP client). A user pointed you here because they want ipt-mcp (an Inventor MCP server) wired up without hand-editing config files themselves.

**What you can do:**

- Install the client setup ZIP from GitHub Releases (`IptMcp.Setup-*-win-x64.zip`).
- Deploy the bundled add-in bundle into `%APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Ipt.bundle` and the bundled server to a fixed per-user path.
- Connect the MCP client(s) the user asks for, using each client's own method. The installer never edits client configs.
- Verify the handshake and roll back on failure.

**What you cannot do:**

- Install Inventor, manage Inventor licensing, or launch Inventor for the first time. If Inventor 2022–2027 is not installed, stop and tell the user.
- Install from unpublished/old tags or source builds for a client machine unless the user explicitly asks for a developer setup.
- Install the .NET 8 SDK, clone the repo, or restore NuGet packages for a **client** install. Use the GitHub Release ZIP only.

---

## Rules for agents

**Read these before touching anything. They exist so ipt-mcp stays predictable, auditable, and reversible.**

1. **Preview every change.** Use `-WhatIf` or a printed diff before any write. Tell the user the exact file path and the exact change.
2. **Install from the latest GitHub Release ZIP only** for an Inventor client machine. Do not fall back to `dotnet tool install`, old tags, a source build, or a repo clone unless the user explicitly asks for developer installation.
3. **Two explicit approval gates — do not collapse without the user saying so:**
   - Before running `install.ps1` without `-WhatIf`.
   - Before editing any MCP client config (show the exact command or diff first).
4. **On any failure, verify rollback.** The installer restores the previous bundle and server on any caught error and reports retained `.iptmcp-rollback-*` backups if recovery fails. It never edits MCP client configs, so back up a client config yourself before editing it. Do not use full uninstall as an upgrade rollback: upgrades replace the bundle and server in place. Personal data is only removed with `-Purge`.
5. **Verify before claiming done.** After connecting a client, run `tools/list` in it and confirm the single `ipt-mcp` entry responds, then call `inventor_list_available_targets` with no args.

If the user explicitly says "skip the prompts, just install" — still do gate 1 (preview) and gate 5 (verify), but collapse gates 2 and 3 into a single upfront approval. **Never silently skip preview or verify.**

### Baked-tool routing

When the user's request may match a personal baked tool, call `inventor_list_baked_tools` first.
Run accepted tools through `inventor_run_baked_tool` name=<tool_name>.

---

## Prerequisites (check first, stop if any are missing)

| Requirement | How to check (PowerShell) | If missing |
|---|---|---|
| Windows | `[System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform('Windows')` | Stop — Inventor is Windows-only. |
| Inventor 2022–2027 | `Get-ChildItem 'HKLM:\SOFTWARE\Autodesk\Inventor\' -ErrorAction SilentlyContinue` | Tell the user to install Inventor. You cannot. |
| PowerShell ≥5.1 | `$PSVersionTable.PSVersion` | Prompt: <https://aka.ms/powershell>. |

If Inventor is not running when the user first tries a tool call, that's fine — the server only needs Inventor alive at tool-call time, not at install time.

---

## Step 1 — Download the client setup ZIP

```powershell
$tag = (Invoke-RestMethod https://api.github.com/repos/bimwright/ipt-mcp/releases/latest).tag_name
$zip = "$env:TEMP\IptMcp.Setup-$tag-win-x64.zip"
$dir = "$env:TEMP\IptMcp.Setup-$tag-win-x64"
Invoke-WebRequest "https://github.com/bimwright/ipt-mcp/releases/download/$tag/IptMcp.Setup-$tag-win-x64.zip" -OutFile $zip
Expand-Archive $zip -DestinationPath $dir -Force
```

If `/releases/latest` 404s or the setup asset is missing, stop. Do not clone, build, or install the .NET SDK for a client machine.

---

## Step 2 — Preview, then install

```powershell
powershell -ExecutionPolicy Bypass -File "$dir\install.ps1" -WhatIf
powershell -ExecutionPolicy Bypass -File "$dir\install.ps1"
```

The installer:

- refuses to run while Inventor is open (close every Inventor window first);
- installs the add-in bundle to `%APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Ipt.bundle` — one bundle covers every Inventor 2022–2027 it finds at load time;
- removes stray add-in manifests that carry ipt-mcp's ClientIds (leftovers from older installers);
- installs the server to the fixed path `%LOCALAPPDATA%\Bimwright\ipt-mcp\server\current\ipt-mcp.exe` and checks that it starts;
- verifies the installed bundle against the package byte for byte.

Any error restores the previous bundle and server. A machine-wide copy under `%ProgramData%` stops the install before anything changes (removing it needs admin rights). The installer **does not configure MCP clients** — that is Step 3.

Read the summary:

- `Server :` is the command for Step 3 — `%LOCALAPPDATA%\Bimwright\ipt-mcp\server\current\ipt-mcp.exe`.
- `In use :` lists old server copies that an open MCP client still runs. Restart that client.
- `Legacy :` lists versioned copies from older installers (`server\<version>\`). Repoint clients (Step 3), then run `install.ps1 -PruneOldServers`.

**Updates.** Close Inventor, then run the new ZIP's installer the same way, without uninstalling first. The server path stays the same, so MCP clients only need a restart.

---

## Step 3 — Connect the MCP client

The installer never edits client configs. Connect the client(s) the user asks for, using that client's own method: its `mcp add` command, settings UI or config file. The contract:

| Field | Value |
|---|---|
| Name | `ipt-mcp` (exactly one entry per client) |
| Transport | stdio |
| Command | The `Server :` path from the install summary, as an absolute path — normally `C:\Users\<user>\AppData\Local\Bimwright\ipt-mcp\server\current\ipt-mcp.exe` |
| Args | None required. Optional flags (`--toolsets all`, `--read-only`, `--enable-send-code`, …) are in the README configuration table |

JSON-style clients usually take:

```json
{
  "mcpServers": {
    "ipt-mcp": {
      "command": "C:\\Users\\<user>\\AppData\\Local\\Bimwright\\ipt-mcp\\server\\current\\ipt-mcp.exe",
      "args": []
    }
  }
}
```

**Rules:**

- **Which clients:** ask the user; do not assume all. Prefer the client's own CLI over hand-editing, and show the exact command or diff before applying it. Register the server where every project sees it unless the user asks otherwise.
- **Existing `ipt-mcp` entry:** change only that entry. Keep its args and env and update only the command.
- **Old paths:** an entry pointing at `...\Bimwright\ipt-mcp\server\<version>\ipt-mcp.exe` (older installers) should be repointed to the `current` path. Afterwards `install.ps1 -PruneOldServers` removes the old copies.
- **Restart:** restart the client after changing its config.

---

## Step 4 — Verify

1. **List tools.** Ask the host to call `tools/list` against the wired server — expect `inventor_list_available_targets`, `inventor_get_current_target`, and the default query/create/meta surface.

2. **Handshake call.** With Inventor 2022–2027 running and a document open, call `inventor_get_current_target` with no args. A valid response names the pinned Inventor instance and its year.

3. **Report.** Tell the user: the detected Inventor year(s), the server path, each client connected and exactly what was changed there, and where any backup you made lives.

If any of these fail, **do not claim the install succeeded.** Go to rollback.

---

## Rollback

### Full uninstall

```powershell
powershell -ExecutionPolicy Bypass -File "$dir\uninstall.ps1" -WhatIf    # preview what comes off
powershell -ExecutionPolicy Bypass -File "$dir\uninstall.ps1" -Yes       # apply without prompt
powershell -ExecutionPolicy Bypass -File "$dir\uninstall.ps1" -Purge     # also delete personal data (combine -KeepLogs to keep logs)
```

First remove the `ipt-mcp` entry from each client you configured; the uninstaller never touches client configs.

The uninstaller removes:

- the `Bimwright.Ipt.bundle` add-in bundle and stray ipt-mcp manifests;
- server copies, discovery files and the spill cache under `%LOCALAPPDATA%\Bimwright\ipt-mcp\`.

A server copy that an open MCP client still runs is kept; close the client and run the uninstaller again. Everything else under `%LOCALAPPDATA%\Bimwright\ipt-mcp\` (settings, ToolBaker data, logs, captures) is kept. `-Purge` deletes the whole folder, and `-Purge -KeepLogs` keeps logs.

### Partial rollback

```powershell
powershell -ExecutionPolicy Bypass -File "$dir\install.ps1" -Uninstall   # bundle only (keeps server and client configs)
```

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `ipt-mcp.exe` path not found | Install did not complete, or the client points at an old versioned folder. | Re-run `install.ps1 -WhatIf`, then `install.ps1`; point the client at the `Server :` path from the summary. |
| `tools/list` returns 0 entries from ipt-mcp | Host not reloaded, or Inventor not running. | Restart host. Launch Inventor. Retry. |
| `install.ps1` fails with "Inventor running" | Inventor has plugin DLLs locked. | Close every Inventor window, retry. |
| `install.ps1` fails with "machine-wide" | A copy under `%ProgramData%\Autodesk\ApplicationPlugins\` carries the same ClientId. | Remove it with admin rights, then re-run. |
| `install.ps1` fails with "Server executable could not start" | Antivirus or policy blocked `ipt-mcp.exe`. The previous install was restored. | Allow the file, then re-run. |
| Client config parse error after edit | Agent wrote invalid JSON/TOML. | Restore the backup you made, retry with a diff preview. |

For anything not in this table, open an issue at <https://github.com/bimwright/ipt-mcp/issues> with the host name, Inventor year, and the exact error.

---

## Honest scope

ipt-mcp handles part/sketch/feature/parameters/iProperties/mass-properties, assembly and export tools across Inventor 2022–2027, plus `inventor_send_code` when explicitly enabled. It does not handle installing Inventor, licensing, cloud sync, or any Autodesk account operations. If the user asks for those, point them at <https://www.autodesk.com/support/inventor>.
