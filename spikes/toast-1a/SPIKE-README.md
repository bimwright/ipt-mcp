# PROTOTYPE — throwaway (roadmap Phase 1a)

This folder is a **throwaway compatibility spike**, not product code. It lives only on branch
`spike/ipt-toast-1a` and must never be merged to `main`. It answers the six platform questions in
`docs/roadmap.md` → Phase 1a before any toast code is written for ipt-mcp.

- `ToastSpike.csproj` — standalone add-in `Bimwright.Ipt.ToastSpike` (own ClassId, no reference to
  `src/shared` or to rvt-mcp). Builds for net10 (2027, deployed live), net8 (2025/26) and net48
  (2022–24; compile-only against the 2024 interop — no 2024/2025 install on the dev machine).
- Probes run **inside** Inventor on its STA thread. `drive.ps1` talks to them through a file queue
  (`%LOCALAPPDATA%\Bimwright\ipt-mcp\toast-spike\inbox|outbox`), so no COM exposure is needed.
- Evidence (JSON + PNG) goes to `%LOCALAPPDATA%\Bimwright\ipt-mcp\toast-spike\evidence\`.

Run: `pwsh spikes/toast-1a/drive.ps1` (builds, deploys, restarts Inventor 2027, runs all probes,
closes Inventor, removes the spike add-in).
