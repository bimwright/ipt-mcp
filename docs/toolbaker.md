# ToolBaker & send_code Security Model

`ipt-mcp` ships with two Bimwright **platform** layers carried over from the
`dwg-mcp` / `rvt-mcp` / `nwd-mcp` framework pattern: the `inventor_send_code` escape hatch
and the **ToolBaker** self-evolution engine. Neither is an Inventor domain tool — both are
separately gated and documented here.

Both let an AI agent run C# in-process inside the Inventor add-in against `Inventor.Application`,
so both are governed by a multi-layered safety policy: a two-sided opt-in, a source-level
banned-API gate, a dispatch deny-list, and the read-only-mode filter.

---

## The `inventor_send_code` Escape Hatch

`inventor_send_code` (wire command `send_code`, toolset `code`) is the direct execution command.
It compiles and evaluates a raw C# snippet in-process using Roslyn scripting
(`Microsoft.CodeAnalysis.CSharp.Scripting`). The captured `Inventor.Application` is exposed to the
snippet as the global `app`, and `System`, `System.Collections.Generic`, `System.Linq`, and
`Inventor` are imported by default. Console output is captured and returned as `stdout`.

Because this is a high-privilege escape hatch, it is **disabled by default** and protected by a
two-sided opt-in gate.

### Two-Sided Opt-In Gating

Dynamic execution is off until **both** sides opt in:

1. **Server-side opt-in** — boot the MCP server with either:
   - the `--enable-send-code` CLI flag, **or**
   - the `BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE=1` environment variable
     (also accepts `true` / `yes` / `on`).

   When server opt-in is missing, the `code` toolset is never registered, so the
   `inventor_send_code` tool is not even visible to the MCP client.

2. **Add-in-side opt-in** — the target Inventor add-in process must detect:
   - `BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1`
     (also accepts `true` / `yes` / `on`). Set this in the environment before launching Inventor.

If either side lacks its opt-in, the request is blocked with a **`SEND_CODE_DISABLED`** error. The
enforcement is defense-in-depth:

- `CommandDispatcher` rejects any `send_code` envelope with `SEND_CODE_DISABLED` unless the add-in
  context reports `EnableSendCode`.
- `SendCodeHandler` independently re-checks the add-in flag and refuses to run if it is off.

`inventor_send_code` is **never** exposed in read-only mode (`--read-only` strips the `code`
toolset along with every other write-capable toolset).

Read-only enforcement is also carried in the add-in command envelope. For a hard add-in-side
write lock independent of the server process, launch Inventor with
`BIMWRIGHT_INVENTOR_PLUGIN_READ_ONLY=1` (or `BIMWRIGHT_INVENTOR_READ_ONLY=1`).

---

## Compiler Safety Policy (banned APIs)

Before any dynamic C# snippet — from `inventor_send_code` **or** a baked tool — is compiled, its
source is validated against `BakeCompilerPolicy.ValidateSource`. If the source contains any
forbidden token (case-insensitive substring match), compilation is refused with an
`INVALID_ARGUMENT` error naming the offending token. The shared policy is the same one used by
`nwd-mcp`'s `BakeCompilerPolicy`.

The forbidden tokens block destructive file operations, process spawning, environment mutation,
external network access, reflection, and any attempt to re-enter the ToolBaker layer:

| Category | Forbidden tokens |
|---|---|
| File / disk | `System.IO`, `File.`, `Directory.` |
| Network | `System.Net`, `Socket`, `HttpClient` |
| Process / environment | `System.Diagnostics`, `Process.`, `Environment.`, `Microsoft.Win32` |
| Reflection / dynamic typing | `System.Reflection`, `Activator.`, `Assembly.`, `MethodInfo`, `PropertyInfo`, `FieldInfo`, `GetType(`, `typeof(` |
| ToolBaker re-entry | `Bimwright.Ipt.Shared.ToolBaker` |

> The policy is a coarse source-text gate, not a sandbox. It is one of several layers; the
> opt-in gates and the host Inventor process trust boundary are the others. Treat `send_code`
> as trusted-operator-only.

---

## ToolBaker: governed reusable tools

ToolBaker (toolsets `toolbaker` read-only + `toolbaker_write`) turns repeated `send_code` /
macro workflows into governed, named, reusable tools, so agents stop re-running raw C#. It is
enabled by default (disable with `--disable-toolbaker` or
`BIMWRIGHT_INVENTOR_ENABLE_TOOLBAKER=0`). Adaptive bake-suggestion generation is opt-in
(`--enable-adaptive-bake` or `BIMWRIGHT_INVENTOR_ENABLE_ADAPTIVE_BAKE=1`).

### The six ToolBaker tools

**Read-only (`toolbaker` toolset — available in read-only mode, server-side only, no add-in
round-trip):**

| Tool | Purpose |
|---|---|
| `inventor_list_baked_tools` | List verified, compiled, registered baked tools from the server registry. |
| `inventor_list_bake_suggestions` | List active adaptive ToolBaker suggestions from the server bake database. |
| `inventor_create_bake_issue_draft` | Build a GitHub issue draft for a suggestion **without** submitting it. |

**Write-capable (`toolbaker_write` toolset — hidden in read-only mode):**

| Tool | Purpose | Round-trips to add-in? |
|---|---|---|
| `inventor_run_baked_tool` | Execute a registered baked tool by name with JSON params (wire command `run_baked_tool`). | Yes |
| `inventor_accept_bake_suggestion` | Validate → compile → apply → persist a suggestion into the registry (add-in wire command `apply_bake`). | Yes |
| `inventor_dismiss_bake_suggestion` | Dismiss / snooze a suggestion or emit a gap signal. | No |

`inventor_dismiss_bake_suggestion` is intentionally server-side-only: it mutates the local bake
database state, not the Inventor model.

### Bake lifecycle

1. **Suggest** — adaptive clustering of recurring workflows produces suggestions
   (`inventor_list_bake_suggestions`). Each suggestion carries an id, title, source, score, and
   a JSON payload.
2. **Accept** — `inventor_accept_bake_suggestion(suggestionId, desiredName)`:
   - the desired name is validated for collisions against the existing registry,
   - the source passes the `BakeCompilerPolicy` banned-API gate,
   - `ToolCompiler` compiles it and a smoke validation runs,
   - the add-in applies it (`apply_bake`),
   - the result is persisted in the registry.
3. **Run** — `inventor_run_baked_tool(name, paramsJson)` looks the record up in the registry and
   dispatches it through the add-in (wire command `run_baked_tool`), subject to the dispatch
   deny-list below.
4. **Dismiss** — `inventor_dismiss_bake_suggestion(suggestionId)` snoozes a suggestion (default
   `snooze_30d`).
5. **Draft an issue** — `inventor_create_bake_issue_draft(id)` produces a GitHub issue draft for a
   gap/suggestion without submitting anything.

### Dispatch deny-list (`BakedToolDispatchAuthorizer`)

When a baked tool runs, every command it tries to invoke must pass
`BakedToolDispatchAuthorizer.IsAllowed`. This is an allow-list *and* a deny-list, preventing
privilege escalation and recursion.

**Allowed** — only the read-only Inventor query commands:

- `health`
- `get_document_info`
- `list_open_documents`
- `list_parameters`
- `get_parameter`
- `get_iproperty`
- `get_mass_properties`

**Denied** — the platform / mutating commands a baked tool must never reach:

- `send_code` (prevents a self-execution loop)
- `batch_execute`
- `run_baked_tool` (prevents nested recursion)
- `apply_bake`
- `accept_bake_suggestion`
- `dismiss_bake_suggestion`
- `list_baked_tools`

A command is allowed only if it is **not** in the denied set **and** is in the allowed set, so any
write/geometry command (e.g. `extrude`, `new_part`) is rejected by default.

### Persistence

All ToolBaker state lives under:

```text
%LOCALAPPDATA%\Bimwright\ipt-mcp\baked
```

- `bake.db` — SQLite registry of accepted baked tools and suggestion state.
- `audit.jsonl` — append-only audit log of bake operations.

The directory is created on demand (`BakePaths.EnsureDir`). It is distinct from the target
descriptor directory `%LOCALAPPDATA%\Bimwright\ipt-mcp\` (one level up), which holds the
per-instance `inventor-<year>-<pid>.json` discovery files.

---

## Read-only interaction summary

| Mode | `inventor_send_code` | `toolbaker` (read) | `toolbaker_write` |
|---|---|---|---|
| Default (`code` off) | hidden | exposed | exposed |
| `--enable-send-code` + add-in opt-in | exposed | exposed | exposed |
| `--read-only` | hidden | exposed | hidden |
| `--disable-toolbaker` | per send-code gate | hidden | hidden |
