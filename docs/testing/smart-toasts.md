# Smart toast verification

Public behavior, motion contract, and verification coverage for the single activity card.
Historical checks below are limited evidence from the earlier three-card feed, not a claim about the currently deployed add-in or a release certification.

## Behavior covered

- One card (`ToastLayout.MaxToasts = 1`). Tool results share it and roll **Success / Failed / Capture** counters. Capture counts a successful `capture_view` (including inline) and is not added again on top of Success. A failure sticks the accent red until the card closes. Read and write both use the blue accent.
- The card idles for 20 seconds after the latest result. A real pointer move pauses that deadline; leaving rearms the full interval. A card shown under a stationary cursor is not a hover.
- Clicking the card opens History and dismisses it. × only dismisses. There is no thumbnail on the card.
- **Agent connected** and the toast on/off confirmation are status cards (6 seconds and 3 seconds). They do not change counters and do not cover an open activity card or task report.
- `inventor_report_task_result` (`query`, read-only) takes `task_id` (1–80 characters), `outcome` (`completed` / `failed` / `cancelled`), and a single-line `summary` (1–120 characters, control/format characters rejected). It does not modify the model. The report replaces the shared slot, says **Agent reported**, keeps an 8-second lifetime, and does not increment counters. `failed` is an error card; `cancelled` uses a neutral icon. `toast_shown` is true when a card was retained, including while Inventor is minimized or behind a dialog.
- A minimized or modal-blocked frame holds the card hidden and shows it with the same counts once the frame is usable. A hidden or destroyed frame, or invisible Inventor, gets no card. A card already fading closes immediately instead of being parked.
- `inventor_report_task_result` is STA-independent: it answers on the listener thread, so the report still lands while the STA is jammed behind a timed-out `send_code`.
- `inventor_health` does not toast.
- Auto palette follows `ToastSample`: no sample while the frame is unusable, the anchor only when no card is painted, beside the card when a retheme happens on screen, and a committed palette is kept across park/restore. A painted card is never its own backdrop. Samples stay in memory.

## Brand motion contract

Session-only, off by default, not written to config. `ToastWindow` reveals `BrandAssets` on a real hover:

- Both wordmark layers use 10 DIP SemiBold text. The footer label `Inventor {year}` stays visible with branding on or off.
- No wipe when the card appears. A real hover starts one after 100 ms. The mask and glint sweep together for 500 ms, quadratic ease-out, from relative offset -0.75 to +0.75. Settled letters are alpha 0.8; the glint crests at 1.0. Leaving fades the wordmark over 200 ms.
- Updates, retheme, reflow, and a further hover while the wordmark is already revealed do not replay the wipe.
- With Windows' **Animate controls and elements** setting disabled, the wordmark settles directly at alpha 0.8 without a sweep, and the card opens and closes without a fade.
- The **Toast Brand** ribbon button is disabled while toasts are off and keeps its pressed state.

## Verification

Automated test commands:

```bash
dotnet test tests/Bimwright.Ipt.Toast.Tests -c Debug
dotnet test tests/Bimwright.Ipt.Toast.Wpf.Tests -c Debug
dotnet test tests/Bimwright.Ipt.Tests -c Debug --filter FullyQualifiedName~ToastSourcePolicyTests
```

Historical automated results (2026-09-24):

| Project | Historical result |
|---|---|
| `Bimwright.Ipt.Toast.Tests` | 161 passed, 0 failed |
| `Bimwright.Ipt.Tests` | 437 passed, 0 failed |

Limited historical live checks on Inventor 2027 covered write, thumbnail, read,
error, and task-result card accents; the delayed wordmark wipe; and
`inventor_report_task_result` responding while ordinary commands queued behind
a busy STA. These checks do not establish coverage for every supported Inventor year.

## Remaining coverage

- Run a 100-call `inventor_list_open_documents` burst: one window, counters that match the calls, and no further windows after the calls return.
- Minimize or a modal dialog parks the card; restoring shows the same counts and a fresh idle deadline. A stationary cursor on restore does not count as a hover.
- Live-check success/error glyphs, the neutral `cancelled` icon, History opening from the card click, and × dismissing without opening History. These behaviors were not covered by the historical live checks above.
