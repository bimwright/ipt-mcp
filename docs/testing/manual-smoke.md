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

17. **Assembly batch smoke** (place / constrain / verify / part features / view)
    Fixtures `A.ipt` (plate 50×50×5 with a planar iMate `IF_MATE_TOP`) and `B.ipt` (Ø20×30 cylinder
    with an insert iMate `IF_INSERT_SHAFT`) from `C:\Temp\bimwright-spike\`.
    1. On the fixture parts, run `inventor_list_interfaces`, then create an additional named interface
       with `inventor_create_imate`. Intentionally submit one ambiguous selector first.
       **Expected:** the failure is `INVALID_ARGUMENT` with `candidates[{centroid_mm,area_mm2}]`; retry
       with `near_mm` succeeds and returns the iMate name.
    2. Run `inventor_new_assembly`, then `inventor_place_occurrence` for grounded A and two B occurrences.
       Exercise `inventor_add_constraint` with `mate`, `flush`, `insert`, and `angle` using compatible
       iMate/origin refs; use a fresh occurrence or assembly where needed to avoid over-constraint.
       **Expected:** every successful response has `health: "up_to_date"`; an unknown ref returns
       `INVALID_ARGUMENT` with the structured `available` names.
    3. Run `inventor_list_constraints`, `inventor_check_interference`,
       `inventor_measure_min_distance`, `inventor_get_assembly_bom`, and assembly-level
       `inventor_get_mass_properties`. **Expected:** all constraints are `up_to_date`, interference
       `count: 0`, mated-face distance `0`, grounded A has DOF `0/0`, and assembly mass approximates
       the sum of its occurrences.
    4. Open A and run `inventor_hole` with tapped `M6x1`, then both `inventor_circular_pattern` and
       `inventor_rectangular_pattern` using the returned hole feature name. **Expected:** `tapped: true`
       and both pattern calls return a pattern name with the requested instance count.
    5. Run `inventor_set_view_orientation` for at least two orientations, `inventor_view_fit`, and
       `inventor_capture_view` in output-path mode. **Expected:** each orientation is echoed, fit reports
       `fitted: true`, and every PNG exists, has non-zero size, and is visually non-blank.

18. **Call journal v2** (improvement spec F1; server-only, no add-in rebuild)
    `scripts\mcp-smoke.ps1` starts its own server with the journal redirected to
    `%TEMP%\ipt-mcp-smoke-calls.jsonl`, so MCP servers already running for other clients (which lock
    `src\server\bin\`) can stay up. Start Inventor with `BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1`,
    then in a pwsh 7 session (not `pwsh -File`, which cannot bind `-ToolCalls`):
    ```powershell
    dotnet build src/server -c Debug --artifacts-path bin\f1-artifacts
    # --enable-send-code alone does not register inventor_send_code: `code` is not in the default
    # toolset list, so request it explicitly (known gap, tracked outside F1).
    $env:BIMWRIGHT_INVENTOR_TOOLSETS = "all"
    & .\scripts\mcp-smoke.ps1 -EnableSendCode `
        -ServerExe .\bin\f1-artifacts\bin\Bimwright.Ipt.Server\debug\Bimwright.Ipt.Server.exe `
        -ToolCalls @(
            @{ name = 'inventor_get_current_target'; arguments = @{} },
            @{ name = 'inventor_new_part';           arguments = @{} },
            @{ name = 'inventor_get_document_info';  arguments = @{} },
            @{ name = 'inventor_send_code';          arguments = @{ code = 'var x = ;' } },
            @{ name = 'inventor_send_code';          arguments = @{ code = 'Console.WriteLine(app.ActiveDocument.DisplayName);' } })
    ```
    **Expected:** all `finish` lines share one `session_id` (`server-<yyyyMMddTHHmmssZ>-<pid>`) and
    always carry `error_code`, `target_id`, `response_bytes`, `plugin_duration_ms`, `data_ok`,
    `data_error`, `stdout_bytes` (null when not applicable). For add-in calls `target_id` equals the
    `inventor_get_current_target` id, `response_bytes > 0` and `plugin_duration_ms >= 0`.
    - `get_document_info` → `success: true`, `data_ok: null`.
    - `send_code` with `var x = ;` → `success: true`, `data_ok: false`, `data_error` starts with
      `compile error`.
    - `send_code` with `Console.WriteLine(...)` → `data_ok: true`, `stdout_bytes > 0`.
    - With no Inventor running, an add-in call such as `inventor_get_document_info` still leaves a
      `finish` line with `success: false`, `error_code: "NO_TARGET"`.

    **Recorded 2026-09-15** — branch `feat/call-journal-v2`, Inventor 2027 (`inventor-2027-74076`):
    all expectations met. `new_part` logged `duration_ms` 9073 / `plugin_duration_ms` 9055, i.e. the
    time is spent inside the add-in, not in transport.
    ```text
    {"timestamp":"2026-09-15T16:07:21.9410448Z","session_id":"server-20260915T160712Z-9516","request_id":"907b268f06594a479a01e4cf6d67cf2a","tool":"get_document_info","phase":"finish","success":true,"duration_ms":6,"error":null,"error_code":null,"target_id":"inventor-2027-74076","response_bytes":250,"plugin_duration_ms":0,"data_ok":null,"data_error":null,"stdout_bytes":null}
    {"timestamp":"2026-09-15T16:07:22.6167580Z","session_id":"server-20260915T160712Z-9516","request_id":"494f6b4a01e74a2dafbec2be7c040031","tool":"send_code","phase":"finish","success":true,"duration_ms":670,"error":null,"error_code":null,"target_id":"inventor-2027-74076","response_bytes":283,"plugin_duration_ms":223,"data_ok":false,"data_error":"compile error: (1,9): error CS1525: Invalid expression term ';'","stdout_bytes":0}
    {"timestamp":"2026-09-15T16:07:26.1950946Z","session_id":"server-20260915T160712Z-9516","request_id":"e780d3a3da9b40bab1ee9fd9f092e723","tool":"send_code","phase":"finish","success":true,"duration_ms":3574,"error":null,"error_code":null,"target_id":"inventor-2027-74076","response_bytes":231,"plugin_duration_ms":3571,"data_ok":true,"data_error":null,"stdout_bytes":7}
    ```
    No Inventor running (same build):
    ```text
    {"timestamp":"2026-09-15T16:03:54.1462388Z","session_id":"server-20260915T160354Z-27180","request_id":"b7596b5cdb5c456bb9fe74b41339dbc4","tool":"get_document_info","phase":"finish","success":false,"duration_ms":0,"error":"No live Inventor target. Start Inventor with the bimwright add-in loaded.","error_code":"NO_TARGET","target_id":null,"response_bytes":null,"plugin_duration_ms":null,"data_ok":null,"data_error":null,"stdout_bytes":null}
    ```

19. **send_code contract** (improvement spec F2; needs the REBUILT add-in deployed and the
    F2 server build — run via `scripts\mcp-smoke.ps1` like step 18):
    - `inventor_send_code` `return new { a = 1 };` → `ok:true`, `result:{a:1}` (F2-a return value).
    - `inventor_send_code` `return app;` → `ok:false`, error `"return a DTO (anonymous object /
      primitives / arrays), not an Inventor API object"` (F2-a API-object rejection).
    - `inventor_send_code` `var x = ;` → `ok:false`, `compile error` (script-level error still in data).
    - Denylist regression (F2-c): `"Drain_1p5NPT_VisibleSocket"` in a string literal and
      `ex.GetType().Name` inside a catch block both run (`ok:true`); `System.IO.File.Exists` is still
      rejected with `INVALID_ARGUMENT: send_code source uses forbidden token: System.IO`.
    - `inventor_send_code` `Thread.Sleep(40000)` with `timeout_ms=5000` → `TIMEOUT` after ~5 s with the
      new message warning the script may still be running; the script keeps occupying the STA thread.
    - `inventor_health` while the script is still running → `sta_busy:true`, `pending_commands:1`,
      `answered_without_sta:true` (fast-path, F2-b). After the queue drains → `sta_busy:false`,
      `pending_commands:0`.
    - A `send_code` submitted while the STA is still busy queues behind it: `duration_ms` includes the
      wait while `plugin_duration_ms` reflects only the actual script run.

    **Recorded 2026-09-16** — branch `feat/send-code-contract`, Inventor 2027
    (`inventor-2027-34876`, pipe): all expectations met. Queued `Thread.Sleep(5000)` returned
    `duration_ms` 38488 / `plugin_duration_ms` 5346 — the queue wait is visible in the journal.
    ```text
    {"tool":"send_code","phase":"finish","success":false,"duration_ms":5004,"error":"TIMEOUT: send_code exceeded 5000 ms. The script MAY STILL BE RUNNING on Inventor's STA thread and later commands will queue behind it. Call inventor_health to check sta_busy before retrying; do not resend the same script.","error_code":"TIMEOUT","target_id":"inventor-2027-34876",...}
    {"tool":"health","phase":"finish","success":true,"duration_ms":2014,...,"target_id":"inventor-2027-34876"}   # sta_busy:true, answered_without_sta:true
    {"tool":"send_code","phase":"finish","success":true,"duration_ms":38488,"plugin_duration_ms":5346,...}     # queued behind the 40 s script
    {"tool":"health","phase":"finish","success":true,"duration_ms":27,...}                                    # sta_busy:false, pending_commands:0
    ```
    Journal note: `plugin_duration_ms:0` on TIMEOUT/denylist/health lines means "not measured"
    (response generated before/without a STA dispatch), not a real 0 ms — `target_id` is now filled on
    these `Err()` lines (F1 hand-off item, verified).

20. **Output guardrails** (improvement spec F3; needs the REBUILT add-in deployed and the F3
    server build — run via `scripts\mcp-smoke.ps1` like step 18/19):
    - `inventor_send_code` `Console.Write(new string('x', 100*1024));` → `ok:true`,
      `stdout` = first 8 KiB only, `stdout_truncated:true`, `stdout_file` →
      `%LOCALAPPDATA%\Bimwright\ipt-mcp\spill\send_code-<ts>-<id>.txt` holding the full 100 KiB.
      Journal: `stdout_bytes:8192`, `response_bytes` stays ~8.5 KB (F3-b).
    - `inventor_capture_view` with no `output_path` → `{path,width,height,bytes}` pointing at
      `%LOCALAPPDATA%\Bimwright\ipt-mcp\captures\capture-<ts>-<seq>.png`, no base64 (F3-c default).
    - `inventor_capture_view` `inline=true` + small size → legacy `{mime_type,width,height,bytes,base64}`.
    - `inventor_list_parameters` on a small part → indented JSON text (F3-d small-payload path);
      the inline base64 response above is single-line compact (F3-d >4 KiB path).

    **Recorded 2026-09-16** — branch `feat/output-guardrails`, Inventor 2027
    (`inventor-2027-29540`, pipe): all expectations met.
    ```text
    {"tool":"send_code","phase":"finish","success":true,"duration_ms":1060,"target_id":"inventor-2027-29540","response_bytes":8568,"plugin_duration_ms":1051,"data_ok":true,"stdout_bytes":8192}   # 100 KiB stdout → 8 KiB inline + spill file (102400 B on disk)
    {"tool":"capture_view","phase":"finish","success":true,"response_bytes":332,...}    # file mode → captures\capture-20260915-193057-001.png (356233 B)
    {"tool":"capture_view","phase":"finish","success":true,"response_bytes":44272,...}  # inline=true → compact base64 response
    {"tool":"list_parameters","phase":"finish","success":true,"response_bytes":211,...} # indented small response
    ```

21. **Extrude v2** (improvement spec F4-P0-1; run via `scripts\mcp-smoke.ps1` like step 20):
    - `extrude {distance:"10 mm", operation:"new_body", name:"base"}` on a 50×30 sketch →
      `feature_name:"base"`, `volume_mm3:15000`.
    - `extrude {distance:5, operation:"cut", affected_bodies:["body:1"]}` on a 10×10 sketch →
      `volume_mm3:14500` (cut scoped to body 1).
    - `extrude {operation:"new_body", affected_bodies:[...]}` → `INVALID_ARGUMENT`
      ("affected_bodies has no meaning with operation=new_body…").
    - `send_code` verify: `SurfaceBodies.Count=1`, `get_Volume(0.01)`≈14.5 cm³.

    **Recorded 2026-09-16** — branch `feat/f4-typed-tools`, Inventor 2027
    (`inventor-2027-23888`, pipe): all expectations met. `volume_mm3` fix verified
    (`get_Volume(0.0)` → E_INVALIDARG; 0.01 works).

22. **Fixed work plane** (F4-P0-2):
    - `create_work_plane {type:"fixed", origin:{x:0,y:0,z:25}, x_axis:[1,0,0], y_axis:{x:0,y:1,z:0},
      name:"wp_z25", visible:true}` → `work_plane_name:"wp_z25"`, `type:"fixed"`, `visible:true`.
      `send_code` verify: `WorkPlanes.Count=4`, `[4].Name="wp_z25"`, `Plane.RootPoint.Z=2.5` cm,
      `Visible=true`.
    - Negatives: missing `origin` → INVALID_ARGUMENT ("origin must be {x,y,z} or [x,y,z]");
      parallel `x_axis=[1,0,0]`/`y_axis=[2,0,0]` → INVALID_ARGUMENT ("non-zero and non-parallel");
      `type:"offset"` without `refs` → INVALID_ARGUMENT ("refs[] is required").
    - Regression: `{type:"offset", refs:["XY"], offset:15}` → `Work Plane2`, count=5.

    **Recorded 2026-09-16** — branch `feat/f4-typed-tools`, Inventor 2027
    (`inventor-2027-45036`, pipe): all expectations met.

23. **Model queries** (F4-P0-3; run via `scripts\mcp-smoke.ps1`):
    - `list_bodies` / `list_features` on an empty part → `[]`, `total:0`, `truncated:false`.
    - After `extrude {operation:"new_body", name:"base"}` on a 50×30×10 box:
      `list_bodies` → `{id:"body:1", name:"base_body", visible:true, volume_mm3:15000,
      bbox_mm:{min:[0,0,0],max:[50,30,10]}, face_count:6, created_by:"base"}`;
      `list_features` → `{name:"base", type:"ExtrudeFeature", suppressed:false,
      health:"UpToDate", body_names:["base_body"]}`.
    - `list_features {include_health:false}` → same rows without the `health` key.

    **Recorded 2026-09-16** — branch `feat/f4-typed-tools`, Inventor 2027
    (`inventor-2027-80176`, pipe): all expectations met.

24. **set_camera** (F4-P0-4; run via `scripts\mcp-smoke.ps1`):
    - Full spec `{eye:[150,-120,100], target:[25,15,5], up:[0,0,1], perspective:true,
      extents_mm:[120,90]}` on a 50×30×10 box → applied; readback `perspective:true`,
      `extents_mm:[120,90]`, `eye_mm:[138.2,-107.2,91.0]` — Inventor slides the eye along the
      view direction to satisfy extents, which is exactly why the response is a readback.
    - `capture_view {640×480}` immediately after → file-mode PNG (125 KB) at the framed angle.
    - `{perspective:false, fit:true}` → ortho, reframed (`extents_mm`→~61).
    - Negatives all `INVALID_ARGUMENT`: empty call ("at least one of …"), `eye==target`
      ("zero-length view direction"), `up=[0,0,0]` ("non-zero direction vector"),
      up ∥ view dir ("parallel"), `extents_mm:[100,-5]` ("positive numeric").

    **Recorded 2026-09-16** — branch `feat/f4-typed-tools`, Inventor 2027
    (`inventor-2027-35216`, pipe): all expectations met.
