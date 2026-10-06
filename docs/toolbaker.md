# ToolBaker & send_code Security Model

`ipt-mcp` ships with two Bimwright **platform** layers carried over from the
`dwg-mcp` / `rvt-mcp` / `nwd-mcp` framework pattern: the `inventor_send_code` escape hatch
and the **ToolBaker** self-evolution engine. Neither is an Inventor domain tool — both are
separately gated and documented here.

Both let an AI agent run C# in-process inside the Inventor add-in against `Inventor.Application`,
so both are governed by a multi-layered safety policy: explicit execution switches, a source-level
banned-API gate, a dispatch deny-list, and the read-only-mode filter.

---

## The `inventor_send_code` Escape Hatch

`inventor_send_code` (wire command `send_code`, toolset `code`) is the direct execution command.
It compiles and evaluates a raw C# snippet in-process using Roslyn scripting
(`Microsoft.CodeAnalysis.CSharp.Scripting`). The captured `Inventor.Application` is exposed to the
snippet as the global `app`, and `System`, `System.Collections.Generic`, `System.Linq`, and
`Inventor` are imported by default. Console output is captured and returned as `stdout`.

This is a high-privilege escape hatch and is enabled by default in v0.2.1.

### Execution switches

`--enable-send-code` / `--disable-send-code` control the server's four code tools.
The add-in requires no opt-in; `BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_SEND_CODE=1`
is an explicit host kill switch and returns `SEND_CODE_DISABLED`.
`--read-only` excludes execution, while retaining read-only code-module listing.
ToolBaker has its own default-on switch, `--disable-toolbaker`.

The nullable global `doc` is the active document at execution start. Its changes
share one Inventor undo transaction. Runtime/host errors abort that transaction;
host warnings are returned. Other documents, creation/closing and file writes are
outside its rollback scope. Do not end or abort the wrapper transaction yourself.

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

1. **Suggest** — suggestions in the bake database (adaptive clustering is not wired yet, see above)
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
- `list_interfaces`
- `check_interference`
- `measure_min_distance`
- `get_assembly_bom`
- `list_constraints`
- `list_bodies`
- `list_features`
- `probe_brep`

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
| Default (all toolsets) | exposed | exposed | exposed |
| `--disable-send-code` | hidden | exposed | exposed |
| `--read-only` | hidden | exposed | hidden |
| `--disable-toolbaker` | per send-code gate | hidden | hidden |

---

## Code modules (reusable send_code helpers)

When `send_code` is enabled, three more tools in the `code` toolset keep helper functions on the
server so scripts stop re-sending them:

| Tool | Purpose |
|---|---|
| `inventor_save_code_module(name, code, description?, requires?)` | Store a module of declarations (static methods, classes, constants — no top-level statements). It passes `BakeCompilerPolicy`, is dry-compiled in the add-in (`send_code` with `compile_only`), and only then is written to `%LOCALAPPDATA%\Bimwright\ipt-mcp\modules\<name>.csx` + `index.json`. `requires` names other modules it calls. |
| `inventor_list_code_modules(include_code?)` | Name, hash, description, requires, size and the declared signatures. |
| `inventor_delete_code_module(name)` | Remove a module; refused while another module requires it. |

`inventor_send_code(code, modules: [...])` expands the listed modules and their `requires`
(dependencies first), compiles them in front of the script as one submission and echoes
`modules: [{name, hash}]` for provenance. Each part is tagged with a `#line` directive, so compile
diagnostics and runtime failures point at `module:<name>` or `script` plus the line. The journal
records module names and hashes, not their code.
