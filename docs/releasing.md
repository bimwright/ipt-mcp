# Releasing ipt-mcp

Ordered pipeline for cutting a release. Each step lists its gate; do not reorder —
the MCP registry validates that the NuGet package exists, and the setup ZIP embeds
the plugin builds.

Current pending release: **v0.2.0** — version fields already pinned
(`server.json`, `src/server/Bimwright.Ipt.Server.csproj`), CHANGELOG section cut,
`mcps/ipt-mcp/tools/` schemas regenerated (73 tools, `--toolsets all --enable-send-code`).

## Pipeline

1. **Tests + builds** — all green before anything ships:
   ```bash
   dotnet test tests/Bimwright.Ipt.Tests -c Release
   dotnet build src/server -c Release
   ```
   Plugin builds happen inside step 3 per detected interop.

2. **NuGet server package** — `server.json` points the MCP registry at
   `nuget.org/Bimwright.Ipt.Server` (`dnx` tool). Pack and push:
   ```bash
   dotnet pack src/server -c Release -o build/nuget
   dotnet nuget push build/nuget/Bimwright.Ipt.Server.<ver>.nupkg -k <NUGET_API_KEY> -s https://api.nuget.org/v3/index.json
   ```
   NuGet push is irreversible for a given version — bump `<Version>` first if the
   version was already pushed.

3. **Client setup ZIP** (Inventor add-in bundle + server):
   ```powershell
   pwsh scripts/package-client-setup.ps1 -Config Release
   ```
   Output: `build/client-setup/IptMcp.Setup-v<ver>-win-x64.zip`.
   The script **refuses a dirty working tree** — commit first so the package
   matches a commit (`manifest.json` records `commit` + `dirty`); `-AllowDirty`
   is for throwaway test packages only. The ZIP ships `install.ps1`,
   `uninstall.ps1`/`uninstall-all.ps1` (the same full-sweep script), `AGENTS.md`
   and `README.md` when present, `bundle/` (PackageContents.xml + per-year
   `Contents\<year>\`), `server/ipt-mcp.exe`, and a checksummed `manifest.json`.
   The script only ships Inventor years with a **real interop DLL** on the build
   machine — shape-only fallback builds are never packed. Check `manifest.json`
   inside the ZIP (`packedInventorYears`) against the intended support matrix.
   Missing years need a build box with that year's interop.
   Installer semantics: server goes to the fixed `server\current\` path with
   rollback on failure, byte-verified bundle install, `--help` smoke check,
   and `-PruneOldServers` for legacy versioned copies — see `AGENTS.md` for the
   client-facing contract.

4. **Regen registry tool schemas** (after any tool-surface change):
   ```bash
   python scripts/regen-mcps-schemas.py
   ```
   Writes `<workspace>/mcps/ipt-mcp/tools/*.json` (73 files at v0.2.0). Local state,
   not git — consumed by registry publishing.

5. **GitHub release** — tag `v<ver>` on the release commit, attach the ZIP from
   step 3:
   ```bash
   git tag v<ver> && git push origin v<ver>
   gh release create v<ver> build/client-setup/IptMcp.Setup-v<ver>-win-x64.zip
   ```

6. **MCP registry publish** — requires the NuGet package from step 2 to be live
   (the registry validates it). Uses the `mcp-publisher` CLI (binary kept at
   `.mcp-publisher/mcp-publisher.exe`, gitignored — same layout as rvt-mcp;
   a copy exists at the workspace root `.mcp-publisher/`):
   ```powershell
   .mcp-publisher/mcp-publisher.exe login github   # device flow, first time / on 401
   .mcp-publisher/mcp-publisher.exe publish        # from repo root; reads server.json
   ```
   Registry name: `io.github.bimwright/ipt-mcp`. First publish for this project —
   `Bimwright.Ipt.Server` has no NuGet versions yet, so step 2 is a hard
   prerequisite, not just an ordering nicety.

## Gates / known gaps

- **NuGet key**: not stored in the repo. Last publish in the family (rvt-mcp)
  the key was pasted into chat and told to be revoked — confirm with the owner
  whether that happened; get a fresh key at release time.
- **NuGet is registry plumbing, not the client install.** README directs users
  to the GitHub setup ZIP; `dotnet tool install -g Bimwright.Ipt.Server` is not
  the supported client path. The nupkg exists so the registry can point `dnx`
  at a real package.
- **Interop coverage**: packaging only ships years whose
  `Autodesk.Inventor.Interop.dll` is found (`package-client-setup.ps1` →
  `Find-InventorInteropDir`). On a box with only e.g. 2025+2027 interops the ZIP
  covers those years only — verify `packedInventorYears` before publishing.
- **PackageContents.xml / .addin schema** is best-effort generated — verify the
  bundle installs on a real Inventor (see `docs/testing/manual-smoke.md`) before
  announcing a release.
- **Version pins**: `server.json` `version` and `packages[0].version` must equal
  the csproj `<Version>` — the setup script reads `server.json`, the nupkg name
  comes from the csproj.
- **Registry auth**: `mcp-publisher` uses a GitHub device-flow token; on `401`
  re-run `login github`. No credentials are committed.
