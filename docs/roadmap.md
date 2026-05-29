# Bimwright Inventor MCP Roadmap

This document records what Phase 1 ships and which deduped candidates are deferred to later
phases. It is grounded in the design spec's *Roadmap After Phase 1* and *Non-Goals* sections.

## Phase 1 (current)

Phase 1 establishes the two-process architecture (MCP server + per-version in-process add-ins for
Inventor 2022–2027) and **46 tools** when all platform toolsets are enabled:

```text
39 Inventor domain/meta tools   (36 functional + 3 target/meta)
+ 1 inventor_send_code
+ 6 ToolBaker tools
= 46 MCP tools
```

The 39 Phase 1 domain/meta tools are the first production slice of an **85-candidate** deduped
Inventor roadmap — not a replacement for it. Everything below is deferred.

## Deferred Domains (Phase 2 / Phase 3 candidates)

### Assembly tools
- Place component, ground component.
- Assembly constraints and joints.
- Bill of materials (BOM).
- Interference / clash detection.

> Note: creating an *empty* assembly document via `inventor_new_assembly` is already in Phase 1 as
> a document operation. It does not imply any assembly-modeling workflow.

### Drawing tools
- Drawing generation (base/projected/section/detail views).
- PDF export.
- Balloons and parts-list workflows, annotations.

### Advanced features
- Hole, shell, sweep, loft, thread.
- Feature patterns (rectangular/circular/mirror).
- Sketch modify; body split / combine / mirror.
- Work point (work plane and work axis are already in Phase 1).

### Diagnostics
- List features, list faces.
- Command search, API documentation lookup.
- Undo.
- Model health / sick-feature reporting.

### Escape hatches
- iLogic rule execution.
- iLogic log readback.
- Python execution (`inventor_run_python` is an explicit Non-Goal as a public tool).

### Runtime / sessions
- Installed-version discovery.
- Start / adopt / list / terminate managed Inventor sessions.

### Out-of-process / cloud
- Inventor Apprentice Server read workflows.
- Autodesk Platform Services (APS) Design Automation for Inventor.

## Explicit Non-Goals (not on the roadmap unless re-scoped)

These are excluded by design, not merely deferred:

- APS / Design Automation for Inventor in Phase 1.
- Inventor Apprentice Server support in Phase 1.
- Fusion, Vault, CAM, or Product Design Extension workflows.
- The full 85 canonical candidates in a single release.
- Public Python execution.
- A single add-in binary shared across all Inventor versions.
- Redistribution of Autodesk Inventor binaries or SDK DLLs.

## Future .NET / Framework Support

The add-in target framework changes twice across the supported range, mirroring `rvt-mcp`:

| Inventor years | Target framework | Transport |
|---|---|---|
| 2022 / 2023 / 2024 | `net48` | TCP |
| 2025 / 2026 | `net8.0-windows7.0` | Named Pipe |
| 2027 | `net10.0-windows7.0` | Named Pipe |

> **TFM Upgrade Directive:**
> When Autodesk ships a new Inventor major version, add a new `plugin-invNN` shell targeting the
> Autodesk-supported .NET runtime for that version. Inventor 2027's host runtime is .NET 10 (.NET 8
> add-ins remain binary-compatible, but .NET 10 is the native target). Do **not** copy the
> `nwd-mcp` "all plug-ins target net48" model into Inventor.
