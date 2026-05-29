# Security Policy

## Supported Versions

Security updates are provided for the latest minor release series only.

| Version | Supported |
|---------|-----------|
| 0.1.x   | ✓         |

## Threat Model

`ipt-mcp` runs locally only. The MCP server talks to each in-process Inventor add-in over an
authenticated localhost transport — **TCP on `127.0.0.1` for Inventor 2022–2024 (net48)** and a
**Named Pipe for Inventor 2025–2027 (net8/net10)**. The attack surface is:

- Local processes that can read the discovery files
  (`%LOCALAPPDATA%\Bimwright\ipt-mcp\inventor-2022-*.json` .. `inventor-2027-*.json`).
- Local processes that can connect to the per-target TCP port or Named Pipe.
- Code executed via `inventor_send_code` or materialized by the ToolBaker engine.

## Mitigations in place

### Per-session token authentication
- Each Inventor add-in session generates a cryptographic random token.
- The token is persisted alongside the transport endpoint in the discovery file.
- Every request must carry the valid token in the command envelope — otherwise it is rejected with
  `UNAUTHORIZED`.

### Input validation
- `--target` is validated against the 4-digit calendar years `2022`–`2027`; legacy version codes
  are rejected.
- Handler parameters are validated before dispatch; invalid input returns `INVALID_ARGUMENT`.
- The transport enforces a 1 MiB line-size limit per NDJSON message.
- Responses exceeding the configured limit (`--max-response-bytes`, default 5 MB) return
  `RESPONSE_TOO_LARGE`.

### Secret masking
- `SecretMasker` redacts API keys, Bearer tokens, passwords, and auth tokens in log output.
- `ErrorSanitizer` strips Windows/UNC absolute paths from errors returned to the model; only
  document/output paths the user explicitly requested are surfaced.

### Network binding
- TCP listener (2022–2024): `127.0.0.1` only, never `0.0.0.0`.
- Named Pipe (2025–2027): local pipe only; chosen specifically to avoid the Windows
  loopback-firewall prompt.

### Dynamic code paths (`inventor_send_code`, ToolBaker)
- `inventor_send_code` is **disabled by default**. It requires both a server-side opt-in
  (`--enable-send-code` or `BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE=1`) **and** an add-in environment
  variable (`BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1`). Missing either returns
  `SEND_CODE_DISABLED`.
- Run with `--read-only` or `--disable-toolbaker` for host profiles that must not expose
  dynamic-code execution.
- Both `send_code` and baked-tool source pass the `BakeCompilerPolicy` banned-API gate
  (no file/process/network/environment/reflection APIs) before compilation.
- Baked-tool execution is restricted by `BakedToolDispatchAuthorizer` to read-only query commands
  and may never re-enter the platform layer.
- See [`docs/toolbaker.md`](docs/toolbaker.md) for the full model.
- ToolBaker and `send_code` run under the host Inventor process trust boundary. Treat them as
  trusted-operator-only.

## Reporting a vulnerability

**Please do not open a public GitHub issue for security-sensitive reports.**

Use one of these private channels:

1. **GitHub private vulnerability report** — open the repository's Security tab and submit a new
   advisory draft. This is the preferred path.
2. **Email the maintainer** — contact via the address on the commit history.

Include:
- Version (server + add-in) and Inventor year.
- Reproduction steps.
- Impact assessment (local vs remote, auth required, user interaction).

Do not publish proof-of-concept exploits in public channels until a fix has shipped.

## Disclosure timeline

- Acknowledgement within 72 hours of report.
- Assessment + fix target within 14 days for high-severity issues (auth bypass, RCE).
- Coordinated disclosure via GitHub Security Advisory with CVE assignment where applicable.
