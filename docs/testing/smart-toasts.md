# Smart toast verification

Status **2026-09-24**. The READMEs link here for build and live-test status of the aggregated toast feed. This is not a release note.

## Behavior covered

- At most three retained cards (`ToastLayout.MaxToasts = 3`).
- Feed changes wake the toast thread on demand; the 500 ms throttle (`ToastFeed.RefreshIntervalMs`) only bridges bursts. One tool call does not enqueue one window, and nothing ticks while the feed is idle.
- Routine successes share an activity card (count + latest tool). Identical errors share a card and show an occurrence count.
- Priority: failures (including `failed` task reports), then explicit task summaries (`completed` / `cancelled`), then snapshot/export, then routine activity. A full stack replaces the lowest-priority oldest card — a task report may evict even an error, because a report exists to be seen; only lower-priority routine arrivals are dropped, never replayed.
- `inventor_report_task_result` (`query`, read-only) takes `task_id` (1–80 characters), `outcome` (`completed` / `failed` / `cancelled`), and a single-line `summary` (1–120 characters, control/format characters rejected). It does not modify the model. The card says **Agent reported**, the card colour follows the outcome (`failed` is an error card; `cancelled` is not), and the response carries `toast_shown` so the agent knows whether a card was actually retained for display.
- `inventor_report_task_result` is STA-independent: it answers on the listener thread, so the report still lands while the STA is jammed behind a timed-out `send_code`.
- `inventor_health` does not toast.
- Backdrop samples are always taken beside the stack while a card is on screen — a painted card is never its own backdrop.

## Automated — passed 2026-09-24

```
dotnet test tests/Bimwright.Ipt.Toast.Tests -c Debug
dotnet test tests/Bimwright.Ipt.Tests -c Debug --artifacts-path .artifacts-test
```

| Project | Result |
|---|---|
| `Bimwright.Ipt.Toast.Tests` | 149 passed, 0 failed |
| `Bimwright.Ipt.Tests` | 437 passed, 0 failed |

The server test used a side output directory because running server processes held the default build output.

## Live

Verified on Inventor 2027 (2026-09-24): write / thumbnail / read / error / task-result cards all display with the right accents, the BIMwright wordmark rests dimmed then a lit front wipes left→right once ~1.3 s after the card appears, and `report_task_result` answers while ordinary commands queue (send_code sleeping 25 s on the STA: report returned in 7 ms, a queued read took 25.5 s). Still open: the 100-call burst check — before this feed, 100 `inventor_list_open_documents` calls returned in ~0.3 s and toast windows kept appearing for ~13 s afterwards; a post-feed burst should not keep creating toasts after the calls have returned — and the hover edge: a card the mouse rests on while a modal covers the frame stays paused until the pointer moves off it.
