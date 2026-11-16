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
- Install a .NET SDK, clone the repo, or restore NuGet packages for a **client** install. Use the GitHub Release ZIP only.

---

## Rules for agents

**Read these before touching anything. They exist so ipt-mcp stays predictable, auditable, and reversible.**

1. **Preview every change.** Use `-WhatIf` or a printed diff before any write. Tell the user the exact file path and the exact change.
2. **Install from the official GitHub Release ZIP only** for an Inventor client machine. First installation uses the latest stable release; an update uses the exact release from a fresh, permitted health proposal (see Step 1). Do not fall back to `dotnet tool install`, old tags, a source build, or a repo clone unless the user explicitly asks for developer installation.
3. **Two explicit approval gates — do not collapse without the user saying so:**
   - Before running `install.ps1` without `-WhatIf`.
   - Before editing any MCP client config (show the exact command or diff first).
4. **On any failure, verify rollback.** The installer restores the previous bundle and server on any caught error and reports retained `.iptmcp-rollback-*` backups if recovery fails. It never edits MCP client configs, so back up a client config yourself before editing it. Do not use full uninstall as an upgrade rollback: upgrades replace the bundle and server in place. Personal data is only removed with `-Purge`.
5. **Verify before claiming done.** After connecting a client, run `tools/list` and confirm the single `ipt-mcp` entry responds. Select the intended target if needed, then call `inventor_health` and verify `host_connected=true` and the running server/add-in identities. A target-registry entry alone does not verify a host connection.

If the user explicitly says "skip the prompts, just install" — still do gate 1 (preview) and gate 5 (verify), but collapse gates 2 and 3 into a single upfront approval. **Never silently skip preview or verify.**

### Baked-tool routing

When the user's request may match a personal baked tool, call `inventor_list_baked_tools` first.
Run accepted tools through `inventor_run_baked_tool` name=<tool_name>.

### Session startup and version checks

At the start of every Inventor session, select the intended target if multiple instances are present, then call `inventor_health(check_updates=true)` once to discover newer stable releases. If network access is unavailable or disallowed, use the offline default and report the update check as not checked/unknown. Later routine health probes stay offline. Health reports document/modal/STA state and loaded `mcp` server/plugin versions, build ids, and version/revision alignment. Preserve `setup_gateway` and `setup_plugin`. Their assembly version/MVID identify the loaded runtime; `artifact_sha256` observes the disk file at identity capture, not loaded memory. A file copied to disk does not prove the server or add-in reloaded. Alignment is not proof of compatibility or publisher trust.

Use `check_updates=true` to compare both `update.components` against one stable release. Report notification code/message according to `agent_action`: `NEW_RELEASE_AVAILABLE` reports a newer version; `RELEASE_REVIEW_REQUIRED`/`RELEASE_IDENTITY_UNVERIFIED` request evidence review; `LOCAL_VERSION_AHEAD` neutrally identifies newer local components. `UPDATE_CHECK_UNAVAILABLE` reports the external `lookup_reason`, not a local runtime fault, and must not prompt for installation authorization. Explain `protected_component_reasons` and preserve the named observed components; an `ahead_of_release` component must not be downgraded. Treat `unobserved_components` separately: obtain the intended Inventor connection/runtime metadata without inferring corruption or that Inventor is closed. Follow `update_guidance.mode`; only `prepare_update` with `bundle_update_proposal_allowed=true` contains installation steps and needs installation authorization, reusing existing permission. Other modes contain only check/report/review actions. Never automatically install or replace a development build. Null notification and semantic `update_available=false` do not prove release identity. `checked` confirms release lookup, not artifact bytes, compatibility or publisher trust. No target preserves server identity with `ok:false` and `host_connected:false`. Health never installs updates or saves/closes documents to enable an update.

After updating, restart Inventor and reconnect/restart the MCP client. On the **same new client connection**, record `initialize.serverInfo` and a fresh `tools/list`, then call health. Compare server name/full informational version with `client_verification.expected_server_info`; compare the health tool's `_meta["bimwright/ipt-mcp/runtime"]` with `client_verification.expected_runtime` (version, MVID, PID, start time). Verify the current tool schema and annotations too: optional boolean `check_updates`, read-only true, destructive false, idempotent true, open-world true. Do not rely on tool count. The server reports `not_observable_by_server`, never a client-refresh pass. If the client cannot expose handshake/catalog evidence, record client verification as unverified; a separate direct-server probe cannot replace it.

Read both component comparisons, `lookup_reason`, `protected_component_reasons`, `unobserved_components` and the full notification message before summarizing. Notification `components` names the primary affected set, not every component fact. For example, an ahead server with an unobserved plugin returns `RELEASE_IDENTITY_UNVERIFIED` for the plugin while separately protecting the server: report both facts. If both component versions match the release but tag resolution fails, `comparison_reason=release_revision_unavailable` and `lookup_reason` identify missing external release evidence. The component need not be in `protected_components`; the false proposal flag still blocks installation. Report the lookup failure and stop the update workflow, without treating it as a runtime fault or stopping otherwise healthy Inventor work.

---

## Prerequisites (check first, stop if any are missing)

| Requirement | How to check (PowerShell) | If missing |
|---|---|---|
| Windows | `[System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform('Windows')` | Stop — both the gateway runtime and Inventor require Windows. |
| Inventor 2022–2027 | `Get-ChildItem 'HKLM:\SOFTWARE\Autodesk\Inventor\' -ErrorAction SilentlyContinue` | Tell the user to install Inventor. You cannot. |
| PowerShell ≥5.1 | `$PSVersionTable.PSVersion` | Prompt: <https://aka.ms/powershell>. |

If Inventor is not running when the user first tries a tool call, that's fine — the server only needs Inventor alive at tool-call time, not at install time.

---

## Step 1 — Download the client setup ZIP

Enter Steps 1–2 only for an authorized first installation or a permitted update. A first installation means neither the ipt-mcp server nor add-in is installed; a missing connection is not evidence of that. For an existing installation, obtain fresh `inventor_health(check_updates=true)` JSON for the intended target and require both `update.notification.bundle_update_proposal_allowed=true` and `update_guidance.mode=prepare_update`. Otherwise stop the update workflow; do not download or install a replacement ZIP. User installation authorization does not override these eligibility checks.

For an update, set `$health` to that parsed health response. Keep its release tag through download and installation; do not query `/releases/latest` again and substitute another release.

```powershell
$ErrorActionPreference = 'Stop'
$isFirstInstall = $false # Set true only after confirming an authorized first installation.
if ($isFirstInstall) {
    $tag = (Invoke-RestMethod https://api.github.com/repos/bimwright/ipt-mcp/releases/latest).tag_name
} else {
    if ($null -eq $health -or
        $health.update.notification.bundle_update_proposal_allowed -ne $true -or
        $health.update_guidance.mode -ne 'prepare_update') {
        throw 'Update is not eligible. Report and review the health response; do not install.'
    }
    $tag = ([Uri]$health.update.release_url).Segments[-1]
    if (($tag -replace '^v', '') -ne $health.update.latest_version) {
        throw 'The proposed release tag does not match the health comparison.'
    }
}
if ($tag -notmatch '^v?\d+\.\d+\.\d+$') { throw 'No valid stable release tag was provided.' }
$zip = "$env:TEMP\IptMcp.Setup-$tag-win-x64.zip"
$dir = Join-Path $env:TEMP ("IptMcp.Setup-$tag-win-x64-" + [Guid]::NewGuid().ToString('N'))
$asset = "https://github.com/bimwright/ipt-mcp/releases/download/$tag/IptMcp.Setup-$tag-win-x64.zip"
Invoke-WebRequest $asset -OutFile $zip
Invoke-WebRequest "$asset.sha256" -OutFile "$zip.sha256"
$checksum = (Get-Content -LiteralPath "$zip.sha256" -Raw).Trim()
if ($checksum -notmatch '\A([0-9a-fA-F]{64})  ([^\r\n]+)\z' -or
    $Matches[2] -cne (Split-Path -Path $zip -Leaf)) {
    throw 'The official ZIP checksum is missing or malformed.'
}
$expectedHash = $Matches[1]
if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'ZIP checksum verification failed. Do not extract or install.'
}
Expand-Archive $zip -DestinationPath $dir
if (-not (Test-Path -LiteralPath "$dir\manifest.json" -PathType Leaf)) {
    throw 'The official Setup manifest is missing. Do not run the installer.'
}
```

If the release, ZIP, checksum asset or manifest is unavailable, stop the installation workflow. Do not clone, build, or install the .NET SDK for a client machine. The `.zip.sha256` asset verifies the whole ZIP before extraction; `install.ps1 -WhatIf` then validates extracted files against `manifest.json` before replacement. Health supplies no ZIP digest and does not perform either check. The installer can accept manifest-less developer payloads, so the official-release workflow must enforce manifest presence here. These checks establish integrity, not independent publisher authentication.

---

## Step 2 — Preview, then install

Use only the package verified in Step 1. For updates, the permitted health proposal must still apply to the intended runtime; if the target or loaded identities changed, recheck eligibility before closing Inventor. Stop when the proposal is blocked, including ahead, unresolved or unobserved components. Preview first, then obtain or reuse installation authorization before the second command; authorization alone does not make a blocked update eligible.

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

**Updates.** Only after the eligibility, package-verification and authorization checks above, close Inventor manually and run that verified ZIP's installer without uninstalling first. The server path stays the same; restart Inventor and the MCP client, then verify their loaded identities and the client's fresh handshake/catalog as described above.

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

2. **Handshake call.** With Inventor 2022–2027 running, select the intended target if needed and call `inventor_health` with no args. Require `host_connected=true`; inspect `mcp` and the full runtime identities before claiming that the intended server and add-in are loaded. A document is not required to verify the connection.

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
