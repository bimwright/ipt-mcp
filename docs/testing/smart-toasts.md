# Smart toast verification

Status **2026-09-24**. The READMEs link here for build and live-test status of the aggregated toast feed. This is not a release note.

## Behavior covered

- At most three retained cards (`ToastLayout.MaxToasts = 3`).
- The UI pulls a snapshot at most every 500 ms (`ToastFeed.RefreshIntervalMs`). One tool call does not enqueue one window.
- Routine successes share an activity card (count + latest tool). Identical errors share a card and show an occurrence count.
- Priority: error, then an explicit task summary, then snapshot/export, then routine activity. A full stack replaces an older card of equal or lower priority. A lower-priority arrival is dropped and is not replayed.
- `inventor_report_task_result` (`query`, read-only) takes `task_id` (1–80 characters), `outcome` (`completed` / `failed` / `cancelled`), and a single-line `summary` (1–120 characters). It does not modify the model. The card says **Agent reported**.
- `inventor_health` does not toast.

## Automated — passed 2026-09-24

```
dotnet test tests/Bimwright.Ipt.Toast.Tests -c Debug
dotnet test tests/Bimwright.Ipt.Tests -c Debug --artifacts-path bin/smart-toast-artifacts
```

| Project | Result |
|---|---|
| `Bimwright.Ipt.Toast.Tests` | 146 passed, 0 failed |
| `Bimwright.Ipt.Tests` | 436 passed, 0 failed |

The server test used a side output directory because a running server process held the default build output.

## Live — still open

Before this feed, a burst on Inventor 2027 queued one toast per tool: 100 `inventor_list_open_documents` calls returned in about 0.3 s and toast windows kept appearing for about 13 s afterwards (at most four visible). That measurement is the reason for the feed.

The follow-up has not been loaded into Inventor. The 2027 session was left open, and the add-in DLL stays locked until Inventor exits. Acceptance for the live check: a burst of 100 tool calls does not keep creating toasts for tens of seconds after the calls have returned. Close Inventor, rebuild the add-in, then repeat the burst.
