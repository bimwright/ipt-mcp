# Smart toast verification

Public behavior, motion contract, and verification coverage for the aggregated toast feed.
Historical checks below are limited evidence, not a claim about the currently deployed add-in or a release certification.

## Behavior covered

- At most three retained cards (`ToastLayout.MaxToasts = 3`).
- Feed changes wake the toast thread on demand; the 500 ms throttle (`ToastFeed.RefreshIntervalMs`) only bridges bursts. One tool call does not enqueue one window, and nothing ticks while the feed is idle.
- Routine successes share an activity card (count + latest tool). Identical errors share a card and show an occurrence count.
- Priority: failures (including `failed` task reports), then explicit task summaries (`completed` / `cancelled`), then snapshot/export, then routine activity. A full stack replaces the lowest-priority oldest card — a task report may evict even an error, because a report exists to be seen; only lower-priority routine arrivals are dropped, never replayed.
- `inventor_report_task_result` (`query`, read-only) takes `task_id` (1–80 characters), `outcome` (`completed` / `failed` / `cancelled`), and a single-line `summary` (1–120 characters, control/format characters rejected). It does not modify the model. The card says **Agent reported**, the card colour follows the outcome (`failed` is an error card; `cancelled` is not and shows a neutral icon), and the response carries `toast_shown` so the agent knows whether a card was actually retained for display.
- A minimized or modal-blocked Inventor still gets its card: it is held hidden and shown once the frame is usable (`toast_shown` is true). Only a hidden or destroyed frame, or invisible Inventor, gets no card.
- `inventor_report_task_result` is STA-independent: it answers on the listener thread, so the report still lands while the STA is jammed behind a timed-out `send_code`.
- `inventor_health` does not toast.
- Backdrop samples are always taken beside the stack while a card is on screen — a painted card is never its own backdrop.

## Brand motion contract

`toast-brand-v1` is defined by `BrandMotion` and consumed by `ToastWindow`:

- Both wordmark layers use 10 DIP SemiBold text.
- The initial left-to-right wipe starts 1300 ms after the card appears. Hover replays it after 150 ms; repeated hover replaces the pending pass rather than queuing passes.
- The wordmark mask and glint sweep together for 800 ms, with quadratic ease-in/out, from relative offset -0.75 to +0.75.
- Wordmark alpha is 0.3 ahead of the wipe, 1.0 at the crest, and 0.8 after it settles.
- Model updates, retheming, and reflow do not replay the wipe or entrance animation.
- Suppression while Inventor is minimized or modal-blocked pauses the visible lifetime and motion; restoring the frame resumes them. A card created while suppressed starts its initial pass only when first shown. Hover keeps its lifetime paused.
- With Windows' **Animate controls and elements** setting disabled, the wordmark settles directly at alpha 0.8 without a sweep.

## Verification

Automated test commands:

```bash
dotnet test tests/Bimwright.Ipt.Toast.Tests -c Debug
dotnet test tests/Bimwright.Ipt.Tests -c Debug
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

- Run a 100-call `inventor_list_open_documents` burst: the aggregated feed must not continue creating a backlog of new toast windows after the calls return.
- Verify hover across modal suppression: a card under the pointer must keep its lifetime paused until the pointer leaves after the frame is restored.
- Live-check success/error glyphs, the neutral `cancelled` icon, and cards retained while minimized or modal-blocked and shown after restore. These behaviors were not covered by the historical live checks above.
