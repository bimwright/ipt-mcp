# Smart toast verification

Public behavior, motion contract, and verification coverage for the single activity card.
The card is the one rvt-mcp and dwg-mcp show (same window, counters, thumbnail and wordmark code); only the host around it is Inventor-specific. Live checks below predate that switch and are limited evidence, not a claim about the currently deployed add-in or a release certification.

## Behavior covered

- One card. Tool results share it and roll **Success / Failed / Capture** counters. Capture counts a successful `capture_view` (including inline) and is not added again on top of Success. A failure sticks the accent red until the card closes. Read and write both use the blue accent. The card title is `ipt-mcp {year}`, the line under it names the latest tool.
- The card idles for 20 seconds by default after the latest result. Status offers 10, 20, 30 or 60 seconds; Apply saves `toastIdleSeconds` and the next result or pointer leave uses the new duration. A failed save keeps the previous duration active, shows an error and allows retry. A real pointer move pauses the deadline; leaving rearms the full interval. A card shown under a stationary cursor is not a hover.
- Clicking the card opens History and dismisses it. × only dismisses. A successful `capture_view` that saved a local image also shows it as a thumbnail, centred in one fixed frame whatever its shape; clicking the thumbnail opens the file. A path that is not a real local image never renders.
- **Agent connected** and the toast on/off confirmation are status cards (6 seconds and 3 seconds). They do not change counters and do not cover an open activity card or task report.
- `inventor_report_task_result` (`query`, read-only) takes `task_id` (1–80 characters), `outcome` (`completed` / `failed` / `cancelled`), and a single-line `summary` (1–120 characters, control/format characters rejected). It does not modify the model. The report replaces the shared slot, says **Agent reported**, keeps an 8-second lifetime, and does not increment counters. `failed` is an error card; `completed` and `cancelled` use the standard card. `toast_shown` is true when a card was retained, including while Inventor is minimized or behind a dialog.
- A minimized or modal-blocked frame holds the card hidden and shows it with the same counts once the frame is usable. A hidden or destroyed frame, or invisible Inventor, gets no card. Inventor has no Idling event, so a 100 ms tracker on the dedicated toast thread restores a parked card and follows the owner frame. The card remains unowned and never calls Inventor COM from that thread.
- Status offers independent Left/Right and Top/Bottom alignment, opt-in dragging by the title row and Reset position. Default: 16 DIP inside the owner's top-left corner. Saved DIP offsets are relative to that corner; monitor clamping does not rewrite them. Bottom cards grow upward, with opposite-side fallback when space is insufficient.
- Deliberate hover opens the activity timeline after 200 ms; leaving has 120 ms grace. It displays up to three virtualized rows, keeps earlier scroll position and follows new results at the bottom. Entries retain bounded/redacted summaries, completion time and measured duration. New results glide/fade/pop when motion is enabled; reduced motion updates directly. Click still opens History.
- `inventor_report_task_result` is STA-independent: it answers on the listener thread, so the report still lands while the STA is jammed behind a timed-out `send_code`.
- `inventor_health` does not toast.
- The card has one fixed light style, like rvt-mcp and dwg-mcp. There is no theme setting (`toastTheme` in the config file and `BIMWRIGHT_INVENTOR_TOAST_THEME` are ignored) and nothing samples the screen behind the card.

## Brand motion contract

Off by default. The ribbon choice persists as `showBranding` in `iptmcp.config.json` and is restored at the next start. `McpToastWindow` reveals `BrandAssets` on a real hover:

- Both wordmark layers use 10 DIP SemiBold text. With branding off the wordmark row is omitted; with it on the row is reserved and stays blank until a real hover. The title (`ipt-mcp {year}`) is visible either way.
- No wipe when the card appears. A real hover starts one after 100 ms. The mask and glint sweep together for 500 ms, quadratic ease-out, from relative offset -0.75 to +0.75. Settled letters are alpha 0.8; the glint crests at 1.0. Leaving fades the wordmark over 200 ms.
- Updates, reflow, and a further hover while the wordmark is already revealed do not replay the wipe.
- With Windows' **Animate controls and elements** setting disabled, the wordmark settles directly at alpha 0.8 without a sweep, and the card opens and closes without a fade.
- The **Toast Brand** ribbon button is disabled while toasts are off and keeps its pressed state.

## Verification

Automated test commands:

```bash
# The card, identical to the rvt-mcp / dwg-mcp harness: a console app, exit code 0 = every check passed
dotnet run --project tests/Bimwright.Ipt.Toast.Tests -c Release
# Interactive preview with simulated results (no Inventor, no model edits)
dotnet run --project tests/Bimwright.Ipt.Toast.Tests -c Release -- --demo
# The Inventor-specific host (dedicated thread, anchoring, parking) on net48, net8 and net10
dotnet test tests/Bimwright.Ipt.Toast.Wpf.Tests -c Debug
# Host-free model: card state machine, content builder, layout, config, status text, source policy
dotnet test tests/Bimwright.Ipt.Tests -c Debug --filter "FullyQualifiedName~Toast|FullyQualifiedName~StatusText|FullyQualifiedName~ToolActivity"
```

The harness is a Windows-only STA console app using the production card window and manager with real WPF and no Inventor
dependency; it needs the .NET 10 SDK and an interactive session, and briefly shows test cards. Exit code 0 requires all of:

- One merged body for outcome and context, keeping errors and capture hints and dropping blank or duplicate context.
- The five-row card: title names the gateway (`ipt-mcp`, or the instance identity such as `ipt-mcp 2027`), the line under it names the latest tool, Success · Failed · Capture counters are spread evenly with the first lined up with the tool line, the brand row is right-aligned, and the height never depends on the result text.
- Odometer counters: only changed digits roll, a new value continues the roll, a number that gains a digit widens gradually, a burst settles on the last value without resizing the card, and unchanged counters do not animate.
- Thumbnail: centred on both axes in one fixed frame, opens with the card growing around it, cross-fades to the next capture, fades out before the card closes, ends fully open after an interrupted close, and never animates when Windows animation effects are off. A path outside the local-image policy never renders.
- The × background stays transparent on hover; the two wordmark layers crossfade only inside the moving hover band. Reduced-motion preferences are honoured and every animation clock is released on close.
- `ActivityAggregator → McpToastManager` sets finite coordinates before `Show`, never animating from WPF's default `NaN` position (the failure that crashed rvt-mcp's card on Revit 2027).
- A stationary pointer baseline is filtered; 100 results land in one card that updates without replaying the enter animation; a card is replaced during its fade; closing is synchronous.
- A status card uses the shared slot, × dismisses it, and a card click reaches the host callback (History) and dismisses the card.

`--demo` opens the same window and manager with simulated results (no Inventor calls or model edits): ten successes play
automatically, two counted as captures (one wide, one tall), with controls to replay, burst, add an error, show a status
card, cycle the instance title, or reset. Hover pauses the 60-second preview idle timer.

Automated results (2026-09-29): the harness passes all 18 checks; `Bimwright.Ipt.Toast.Wpf.Tests` 5 passed on each of
net48, net8.0 and net10.0; `Bimwright.Ipt.Tests` 736 passed, 0 failed with `BIMWRIGHT_INVENTOR_MASK_LONG_TOKENS`
unset (three secret-masking tests in `ServerLoggerTests` / `ResponseSpillTests` assume it is unset).

Limited live checks on Inventor 2027, made before the card was replaced by the shared one, covered write, read,
error, and task-result card accents; the delayed wordmark wipe; and `inventor_report_task_result` responding while
ordinary commands queued behind a busy STA. They do not establish coverage for the current card or for every
supported Inventor year.

## Remaining coverage

- Run a 100-call `inventor_list_open_documents` burst: one window, counters that match the calls, and no further windows after the calls return.
- Minimize or a modal dialog parks the card; restoring shows the same counts and a fresh idle deadline. A stationary cursor on restore does not count as a hover.
- Live-check the new card in Inventor 2027 (and 2025, the other installed year): title `ipt-mcp {year}`, position over the graphics view versus the home page, the capture thumbnail and its click, success/error glyphs, History opening from the card click, and × dismissing without opening History. A `cancelled` task report now uses the standard blue card instead of the old neutral icon.
