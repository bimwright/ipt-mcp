# Manual smoke checklist

> **Stub (Phase 1).** The full 16-step real-Inventor checklist is written in Phase 4.
> Requires a machine with a runnable Inventor + the matching .NET SDK; not runnable in CI.

## Planned steps (Phase 4)

1. Install the add-in bundle and launch Inventor.
2. `inventor_list_available_targets` shows the live instance.
3. `inventor_health` returns year / pid / active-document state.
4. `inventor_new_part` → create sketch → draw → dimension → `inventor_extrude`.
5. `inventor_list_parameters` / `inventor_get_mass_properties`.
6. `inventor_export_step` / `inventor_export_stl`.
7. Confirm `inventor_send_code` is absent by default; enable opt-in and run a harmless snippet.
8. Confirm the baked-tool registry is empty.
9. Multi-target: open a second Inventor version and `inventor_switch_target`.
