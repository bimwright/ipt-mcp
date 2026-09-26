# Inventor Manual Smoke Testing Checklist

Follow this numbered checklist on a machine with a runnable Autodesk Inventor desktop
(2022–2027) plus the matching .NET SDK installed, to verify the add-in, transport, and server
integration end to end.

> **Requires a real Inventor desktop.** Interop-only builds or CI checks do not replace
> this checklist. Verify packaging on a runnable Inventor install before claiming it is
> production-ready. The examples below are synthetic test recipes, not recorded results.
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
    Create throwaway fixtures `A.ipt` (plate 50×50×5 with a planar iMate `IF_MATE_TOP`) and
    `B.ipt` (Ø20×30 cylinder with an insert iMate `IF_INSERT_SHAFT`) in a temporary test folder.
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

18. **Call journal**
    `scripts\mcp-smoke.ps1` starts its own server with the journal redirected to
    `%TEMP%\ipt-mcp-smoke-calls.jsonl`, so MCP servers already running for other clients (which lock
    `src\server\bin\`) can stay up. Start Inventor with `BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1`,
    then in a pwsh 7 session (not `pwsh -File`, which cannot bind `-ToolCalls`):
    ```powershell
    dotnet build src/server -c Debug --artifacts-path bin\smoke-artifacts
    # --enable-send-code alone does not register inventor_send_code: `code` is not in the default
    # toolset list, so request it explicitly.
    $env:BIMWRIGHT_INVENTOR_TOOLSETS = "all"
    & .\scripts\mcp-smoke.ps1 -EnableSendCode `
        -ServerExe .\bin\smoke-artifacts\bin\Bimwright.Ipt.Server\debug\Bimwright.Ipt.Server.exe `
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

19. **send_code contract** (run via `scripts\mcp-smoke.ps1` like step 18 with matching
    server and add-in versions):
    - `inventor_send_code` `return new { a = 1 };` → `ok:true`, `result:{a:1}`.
    - `inventor_send_code` `return app;` → `ok:false`, error `"return a DTO (anonymous object /
      primitives / arrays), not an Inventor API object"`.
    - `inventor_send_code` `var x = ;` → `ok:false`, `compile error` (script-level error still in data).
    - Denylist regression: `"Drain_1p5NPT_VisibleSocket"` in a string literal and
      `ex.GetType().Name` inside a catch block both run (`ok:true`); `System.IO.File.Exists` is still
      rejected with `INVALID_ARGUMENT: send_code source uses forbidden token: System.IO`.
    - `inventor_send_code` `Thread.Sleep(40000)` with `timeout_ms=5000` → `TIMEOUT` after ~5 s with the
      message warning the script may still be running; the script keeps occupying the STA thread.
    - `inventor_health` while the script is still running → `sta_busy:true`, `pending_commands:1`,
      `answered_without_sta:true` (fast-path). After the queue drains → `sta_busy:false`,
      `pending_commands:0`.
    - A `send_code` submitted while the STA is still busy queues behind it: `duration_ms` includes the
      wait while `plugin_duration_ms` reflects only the actual script run.

    Journal note: `plugin_duration_ms:0` on TIMEOUT/denylist/health lines means "not measured"
    (response generated before/without a STA dispatch), not a real 0 ms. Check that `target_id`
    identifies the selected target on these responses. After a timeout, call `inventor_health`
    before retrying; do not resend the same script while it may still be running.

20. **Output guardrails** (run via `scripts\mcp-smoke.ps1` like step 18/19):
    - `inventor_send_code` `Console.Write(new string('x', 100*1024));` → `ok:true`,
      `stdout` = first 8 KiB only, `stdout_truncated:true`, `stdout_file` →
      `%LOCALAPPDATA%\Bimwright\ipt-mcp\spill\send_code-<ts>-<id>.txt` holding the full 100 KiB.
      Journal: `stdout_bytes:8192`, `response_bytes` stays ~8.5 KB.
    - `inventor_capture_view` with no `output_path` → `{path,width,height,bytes}` pointing at
      `%LOCALAPPDATA%\Bimwright\ipt-mcp\captures\capture-<ts>-<seq>.png`, no base64 by default.
    - `inventor_capture_view` `inline=true` + small size → legacy `{mime_type,width,height,bytes,base64}`.
    - `inventor_list_parameters` on a small part → indented JSON text;
      responses larger than 4 KiB, such as inline base64 captures, use single-line compact JSON.

21. **Extrude** (run via `scripts\mcp-smoke.ps1` like step 20):
    - `extrude {distance:"10 mm", operation:"new_body", name:"base"}` on a 50×30 sketch →
      `feature_name:"base"`, `volume_mm3:15000`.
    - `extrude {distance:5, operation:"cut", affected_bodies:["body:1"]}` on a 10×10 sketch →
      `volume_mm3:14500` (cut scoped to body 1).
    - `extrude {operation:"new_body", affected_bodies:[...]}` → `INVALID_ARGUMENT`
      ("affected_bodies has no meaning with operation=new_body…").
    - `send_code` verify: `SurfaceBodies.Count=1`, `get_Volume(0.01)`≈14.5 cm³.

22. **Fixed work plane**:
    - `create_work_plane {type:"fixed", origin:{x:0,y:0,z:25}, x_axis:[1,0,0], y_axis:{x:0,y:1,z:0},
      name:"wp_z25", visible:true}` → `work_plane_name:"wp_z25"`, `type:"fixed"`, `visible:true`.
      `send_code` verify: `WorkPlanes.Count=4`, `[4].Name="wp_z25"`, `Plane.RootPoint.Z=2.5` cm,
      `Visible=true`.
    - Negatives: missing `origin` → INVALID_ARGUMENT ("origin must be {x,y,z} or [x,y,z]");
      parallel `x_axis=[1,0,0]`/`y_axis=[2,0,0]` → INVALID_ARGUMENT ("non-zero and non-parallel");
      `type:"offset"` without `refs` → INVALID_ARGUMENT ("refs[] is required").
    - Regression: `{type:"offset", refs:["XY"], offset:15}` → `Work Plane2`, count=5.

23. **Model queries** (run via `scripts\mcp-smoke.ps1`):
    - `list_bodies` / `list_features` on an empty part → `[]`, `total:0`, `truncated:false`.
    - After `extrude {operation:"new_body", name:"base"}` on a 50×30×10 box:
      `list_bodies` → `{id:"body:1", name:"base_body", visible:true, volume_mm3:15000,
      bbox_mm:{min:[0,0,0],max:[50,30,10]}, face_count:6, created_by:"base"}`;
      `list_features` → `{name:"base", type:"ExtrudeFeature", suppressed:false,
      health:"UpToDate", body_names:["base_body"]}`.
    - `list_features {include_health:false}` → same rows without the `health` key.

24. **set_camera** (run via `scripts\mcp-smoke.ps1`):
    - Full spec `{eye:[150,-120,100], target:[25,15,5], up:[0,0,1], perspective:true,
      extents_mm:[120,90]}` on a 50×30×10 box → applied; readback `perspective:true`,
      `extents_mm:[120,90]`. Inspect `eye_mm`: Inventor may slide the eye along the
      view direction to satisfy extents, so the response reports the readback.
    - `capture_view {640×480}` immediately after → non-empty file-mode PNG at the framed angle.
    - `{perspective:false, fit:true}` → orthographic view, reframed to fit the model.
    - Negatives all `INVALID_ARGUMENT`: empty call ("at least one of …"), `eye==target`
      ("zero-length view direction"), `up=[0,0,0]` ("non-zero direction vector"),
      up ∥ view dir ("parallel"), `extents_mm:[100,-5]` ("positive numeric").

25. **export_sat** (run via `scripts\mcp-smoke.ps1`):
    - `export_sat {output_path:%TEMP%\smoke-part.sat}` on the 50×30×10 box →
      `{format:"SAT", acis_version:7, exported:true}`; a non-empty file exists with ACIS header
      `700 0 1 0` — leading 700 = ACIS 7.0, so the translator `Version` option took effect.
    - `acis_version:4` → INVALID_ARGUMENT ("ACIS 7.0 only"), rejected server-side before the
      wire call; `acis_version:7.0` explicit → exported.
    - An `output_path` outside the allowed export roots → INVALID_ARGUMENT.
      Use `BIMWRIGHT_INVENTOR_EXPORT_ROOT` to configure an additional permitted root.
    - `output_path` ending `.step` → INVALID_ARGUMENT "must end in .sat".

26. **combine** (sequential-driver smoke):
    The MCP SDK can dispatch piped `tools/call` concurrently, so send each dependent call
    only after the previous response id arrives. Match numeric types when using response ids
    as PowerShell hashtable keys (`ConvertFrom-Json` returns `Int64`).
    - `base` box 50×30×10 (15000 mm³) + `post` box 20×20×15 overlapping →
      `combine {base_body:"base_body", tool_bodies:["post_body"], operation:"join",
      name:"joined"}` → `{feature_name:"joined", body_names:["base_body"],
      volume_mm3:17000}` — exact union math (15000+6000−4000 overlap); subsequent
      `list_bodies` shows total:1.
    - `combine {base_body:"body:1", tool_bodies:["slot_tool_body"], operation:"cut"}`
      (slot 40×10×8 fully inside base) → `volume_mm3:13800` (17000−3200).
    - `combine {base_body:"1", tool_bodies:["clip_body"], operation:"intersect",
      keep_tool_bodies:true}` → `volume_mm3:2900` (3000 slab − 600 slot void + 500
      post region); `list_bodies` → total:2 (result + kept `clip_body`).
    - Negatives all `INVALID_ARGUMENT`: `tool_bodies:[]` ("non-empty array"),
      base∈tools ("base and tools must differ"), unknown ref ("unknown body
      'nosuch'"), `operation:"new_body"` ("not valid for combine").

27. **batch_execute** (sequential driver):
    - A six-command modeling sequence as one call: `[create_work_plane fixed,
      create_sketch, draw_rectangle, close_sketch, extrude new_body, list_bodies]` →
      `executed:6, rolled_back:false`. Use a 40×20 rectangle on a fixed plane at z=20
      and a 5 mm extrusion named `batched`: expect `batched_body` 4000 mm³ at z 20–25.
    - Stop-at-error default: batch with a bad `sketch_name` at index 3 →
      `executed:4, rolled_back:true`. Verify that sketches created earlier in the batch
      are absent afterward.
    - Repeat a five-command batch with `continue_on_error:true`, a bad `sketch_name`
      at index 3, and a valid independent query at index 4: expect
      `executed:5, rolled_back:false`, index 3's `INVALID_ARGUMENT`, and a successful
      query at index 4. Avoid relying on hardcoded auto-generated sketch names.
    - Blocked: `send_code` / nested `batch_execute` → "'X' cannot run inside
      batch_execute"; unknown command → "unknown command: fly_to_moon";
      21 commands → "at most 20"; `commands:[]` → "non-empty array".

    Additional regression cases:
    - Case-variant bypass attempts: `Batch_Execute` and `SEND_CODE` both →
      "'X' cannot run inside batch_execute" (OrdinalIgnoreCase block list).
    - Document lifecycle inside a batch: `new_part` and `Save_Document` →
      blocked at index 0, `rolled_back:true`; the transaction must keep the same document.
    - `params:"not-an-object"` → step failure "'params' must be an object".
    - `commands:"bogus"` → `INVALID_ARGUMENT` at the MCP layer (server-side
      ValueKind check — never reaches the wire).
    - Sanity: 2-step batch (`list_bodies`+`get_document_info`) →
      `executed:2, rolled_back:false`.

28. **inventor_fillet edge selector**:
    Create two bosses r=8 at (50,0,0..10) and (-50,0,0..10), plus a cylinder r=15
    from z=0 to z=30. Use fresh geometry for each selector case:
    - `edges:{kind:"circular"}` scoped to the r15 cylinder → a fillet over 2 edges
      (top+bottom), `matched_edges` reporting `radius_mm:15`,
      `center_mm` z=30/z=0, `adjacent:["cylinder","plane"]`.
    - `radius_mm:8` → only the boss edges match (r15 edges skipped).
    - `on_body:"bossB_body"` → only bossB edges (centers x=-50).
    - `center_mm:[50,0,10], center_tol_mm:2` → exactly one edge: bossA top.
    - `adjacent_surface_types:["plane"]` → 0 matches → INVALID_ARGUMENT
      (rim edges are cylinder+plane; both faces must be in the set).
    - Negatives: kind:"planar" → "edges.kind must be 'circular'";
      edges:"bogus" → "array of edge ids or a selector object";
      radius<=0 / missing edges → INVALID_ARGUMENT.
    - Re-filleting an already-filleted edge can return API_ERROR/E_FAIL from AddSimple
      due to a geometry conflict, rather than a selector miss.
    - Circle-profile regression: draw a circle, then `close_sketch`; expect a solid
      profile and a successful extrusion. When UpdateProfiles leaves no profiles,
      `close_sketch` uses `Profiles.AddForSolid()` to create one.

29. **inventor_probe_brep**:
    Create a plate 60×40×8 with a through hole d=10 at (30,20):
    - Full survey → exactly 2 ports: top mouth (normal [0,0,1], center z=8) and
      bottom mouth (normal [0,0,-1], center z=0). Check outward normal orientation
      rather than relying on face indices.
      Both report port_diameter_mm=10, inner_loop circle r=5 at (30,20),
      planar_faces_scanned=6, bodies_scanned=1.
    - min/max_diameter_mm [9,11] → 2 ports; min_diameter_mm=50 → 0 ports.
    - body:"plate_body" scopes the scan; body:"body:9" → INVALID_ARGUMENT
      "body index 9 out of range (1..1)".
    - min>max → INVALID_ARGUMENT "min_diameter_mm must be <= max_diameter_mm".
