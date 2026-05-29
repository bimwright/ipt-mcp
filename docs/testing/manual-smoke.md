# Inventor Manual Smoke Testing Checklist

Follow this numbered checklist on a machine with a runnable Autodesk Inventor desktop
(2022–2027) plus the matching .NET SDK installed, to verify the add-in, transport, and server
integration end to end.

> **Not runnable in CI.** The build machine has only Inventor interop assemblies and no confirmed
> runnable `Inventor.exe`. Do **not** claim packaging is production-ready until this checklist
> passes on a real Inventor install (see the Non-Goals in the design spec).
>
> Tool names below use the `inventor_` MCP prefix. Lengths are millimetres at the tool boundary;
> the add-in converts to Inventor's internal centimetres.

---

1. **Install the add-in bundle**
   Run the packaging script for your installed version so the per-user bundle lands at
   `%APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Ipt.bundle\` (run with `-DryRun` first to
   preview the plan without writing anything):
   ```powershell
   pwsh -File .\scripts\package-bundle.ps1 -Years 2025 -Configuration Release
   ```
   **Expected:** `Bimwright.Ipt.bundle\` exists with `PackageContents.xml`, a per-version
   subfolder under `Contents\`, the `Bimwright.Ipt.Plugin.InvNN.dll`, and the matching
   `.addin` manifest.

2. **Launch Inventor**
   Start the Inventor desktop version you deployed for. Close any older instance first so the right
   add-in loads.
   **Expected:** Inventor starts; the add-in loads with no error dialog.

3. **Confirm add-in initialization + descriptor**
   Confirm the add-in loaded (no load error in Inventor's Add-In Manager) and that a session
   descriptor file `inventor-<year>-<pid>.json` was written under:
   ```text
   %LOCALAPPDATA%\Bimwright\ipt-mcp\
   ```
   **Expected:** the JSON contains `inventor_year`, `process_id`, `host_app: "Inventor"`,
   `transport` (`tcp` for 2022–2024, `pipe` for 2025–2027), `port` or `pipe_name`, `auth_token`,
   and a recent `last_heartbeat_utc`.
   The descriptor file contains the private token; `inventor_list_available_targets` and
   `inventor_get_current_target` must not return `auth_token`.

4. **Start the MCP server**
   In a separate terminal, start the stdio MCP server:
   ```powershell
   .\src\server\bin\Debug\net8.0\Bimwright.Ipt.Server.exe
   ```
   **Expected:** the server boots and waits on stdio (register it with your MCP client per
   `.mcp.json.example`).

5. **List targets** — `inventor_list_available_targets`
   **Expected:** the running Inventor instance is listed with its `target_id`
   (`inventor-<year>-<pid>`), year, pid, and transport. (`inventor_get_current_target` reports the
   pinned one, or `NO_TARGET` if none.)

6. **Health** — `inventor_health`
   **Expected:** `ok: true` with `inventor_year`, `process_id`, `has_active_document`, and
   `document_type`.

7. **New part** — `inventor_new_part`
   **Expected:** `ok: true`; a new throwaway part (`.ipt`) becomes the active document.

8. **Create a sketch and draw geometry**
   - `inventor_create_sketch` with `plane="XY"` → **Expected:** a new sketch is created and named;
     the response returns its `sketch_name`.
   - `inventor_draw_line` with `x1=0, y1=0, x2=50, y2=0` → **Expected:** a line segment is added.
   - `inventor_draw_circle` with `cx=25, cy=25, radius=10` → **Expected:** a circle is added.
   - `inventor_draw_rectangle` with `x1=0, y1=0, x2=40, y2=20` → **Expected:** a 4-line rectangle
     is added.
   **Expected overall:** each call returns `ok: true` and the geometry appears in the sketch.

9. **Add a sketch dimension** — `inventor_add_sketch_dimension`
   Dimension one of the entities from step 8 (e.g. the rectangle width to `40`).
   **Expected:** `ok: true`; the sketch shows the constraining dimension.

10. **Extrude** — `inventor_extrude`
    Close the sketch (`inventor_close_sketch`) if required, then extrude the profile, e.g.
    `sketchName="<from step 8>", distance=10, operation="join", direction="positive"`.
    **Expected:** `ok: true` with the created `feature_name`; a solid body appears.

11. **Parameters + mass properties**
    - `inventor_list_parameters` → **Expected:** model + user parameters with names, expressions,
      values, and units.
    - `inventor_get_mass_properties` → **Expected:** mass, volume, surface area, and centre of mass
      for the part.

12. **Export STEP and STL**
    - `inventor_export_step` to a temp path, e.g. `%TEMP%\smoke.step`.
    - `inventor_export_stl` to a temp path, e.g. `%TEMP%\smoke.stl`.
    **Expected:** `ok: true` for each, and both files exist on disk afterward.

13. **send_code absent by default**
    With the server started normally (no `--enable-send-code`), confirm `inventor_send_code` is
    **not** offered by the client. If the client forces the call, the dispatcher returns
    `SEND_CODE_DISABLED`.
    **Expected:** the tool is not listed / is rejected with `SEND_CODE_DISABLED`.

14. **Enable the two-sided opt-in and run a harmless snippet**
    Set `BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1` in the environment **before** launching
    Inventor, and restart the server with `--enable-send-code` (or
    `BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE=1`). On the throwaway model, call `inventor_send_code`
    with a harmless read-only snippet:
    ```csharp
    System.Console.WriteLine("Active doc: " + app.ActiveDocument.DisplayName);
    ```
    **Expected:** the response contains `ok: true` and the captured `stdout` with the document name.
    (A snippet referencing a banned token such as `System.IO` must be rejected with
    `INVALID_ARGUMENT`.)

15. **Baked-tool registry initialized** — `inventor_list_baked_tools`
    **Expected:** returns an initialized registry (empty `tools` array on a fresh install) read from
    `bake.db` under `%LOCALAPPDATA%\Bimwright\ipt-mcp\baked`.

16. **Multi-target listing and switching**
    If licensing permits, open a SECOND Inventor instance (same or different supported version).
    - `inventor_list_available_targets` → **Expected:** BOTH instances are listed with distinct
      `target_id`s.
    - `inventor_switch_target` with the second instance's id → **Expected:** `ok: true`; subsequent
      commands (e.g. `inventor_health`) route to the chosen target. Note this changes the
      server-side target selection only, not any Inventor document.
