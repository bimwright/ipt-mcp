# Benchmark records

Records are scoped to the exact build and execution path stated in each file.
Host-free error-path tests do not demonstrate successful Inventor operations or
Claude Desktop acceptance.

- [Drawing Phase 1 raw fixture record](drawing-phase1-smoke.json): earlier Inventor
  2027 handler fixtures; see the build, coverage and limitations inside the record.
- [Drawing Phase 2 raw fixture record](drawing-phase2-smoke.json): earlier Inventor
  2027 handler fixtures; not an installed add-in/client lifecycle test.
- [v0.2.1 host-free record](v0.2.1-host-free.md): all 111 MCP wrappers on the pinned build, with durations, sizes and expected failure states.
- Current runtime contracts: `tests/stdio/release-contract.py` calls every registered
  tool against a new disposable data root with no CAD target. It records duration,
  result bytes, estimated tokens (UTF-8 bytes / 4, rounded up) and MCP error state.

A live release benchmark must name the commit, server version, host year, driver,
an anonymized machine specification, and every tool's duration, result bytes,
estimated tokens, Success/Failed and output-size policy outcome. Use generated
fixtures only; omit project names, paths, IDs and model contents. New or changed
tools need fresh live results. v0.2.1 still needs this live benchmark; earlier
drawing records do not cover its changed send_code, logging or permissions.
