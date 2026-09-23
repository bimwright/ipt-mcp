# Spike verdict — ipt-mcp toast Phase 1a (2026-09-23, Inventor 2027 live)

Question: can a toast like rvt-mcp's run inside Inventor, and on what platform terms?

| Question | Answer |
|---|---|
| WPF host in Inventor? | Yes: `Application.Current` = `Autodesk.UIModel.App` on the Inventor STA |
| Render on the Inventor STA (A) or on a dedicated thread (B)? | **B.** A freezes for the whole time a command occupies the STA |
| Owner = Inventor main frame? | **No.** Creating an owned toast while the STA is busy stalls the toast thread. Use unowned windows and track minimize/foreground ourselves |
| Focus | Without `ShowActivated=false` + `WS_EX_NOACTIVATE` the toast steals foreground/focus (sketch, extrude, modal dialog). With them, nothing changes |
| DPI | Process is system-aware; 100% and 200% size/placement are correct; mixed DPI untested |
| Ribbon + icons | Tabs in ZeroDoc/Part/Assembly/Drawing work; `Pressed` + `OnExecute` work; AxHost and OLE `IPictureDisp` both render. Pick OLE |
| Cost | STA round-trip p95 ≤ 6 ms even with 4 animating toasts |
| Packaging | Unsigned new add-ins are blocked on first load, so ship the toast inside the existing, already trusted add-in |

Full decisions: `ipt-mcp/docs/superpowers/specs/2026-09-23-ipt-toast-design.md` (local, gitignored).
