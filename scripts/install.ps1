#Requires -Version 5.1
<#
.SYNOPSIS
  Install, update or uninstall the ipt-mcp Inventor add-in bundle and MCP server.

.DESCRIPTION
  In a client setup ZIP, this script installs:
    - the Inventor add-in bundle from bundle/ to
      %APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Ipt.bundle
      (one bundle covers every installed Inventor 2022-2027 via PackageContents.xml)
    - the self-contained MCP server from server/ at the fixed path
      %LOCALAPPDATA%\Bimwright\ipt-mcp\server\current\ipt-mcp.exe

  It never reads or writes MCP client configs: point your MCP client at the
  server path above. Updating keeps that path, so clients only need a restart.

  Every replacement is recorded and rolled back on error. Other Inventor add-in
  manifests carrying an ipt-mcp ClientId (stray copies in sibling bundles or the
  legacy per-user Addins folder) are removed; a machine-wide copy under
  %ProgramData% blocks the install. The installed bundle is verified against the
  package and the server is started once with --help.

.PARAMETER SourceDir
  Setup root. Defaults to the current setup root when bundle/ or server/ exists
  beside this script; otherwise defaults to build\client-setup\stage relative to
  the repo root.

.PARAMETER Uninstall
  Remove the per-user ipt-mcp bundle and stray manifests carrying our ClientIds.
  The server and user data stay; use uninstall-all.ps1 for a full removal.

.PARAMETER PruneOldServers
  Remove legacy version-named server copies (for example 0.2.0\) beside
  current\ after a successful install. Repoint clients that still use them
  first.

.EXAMPLE
  pwsh .\install.ps1 -WhatIf
  pwsh .\install.ps1
  pwsh .\install.ps1 -PruneOldServers
  pwsh .\install.ps1 -Uninstall
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$SourceDir,
    [switch]$Uninstall,
    [string]$ServerInstallRoot,
    [switch]$PruneOldServers
)

$ErrorActionPreference = 'Stop'

if (-not $SourceDir) {
    $hasSetupLayout = (Test-Path (Join-Path $PSScriptRoot 'server')) -or (Test-Path (Join-Path $PSScriptRoot 'bundle'))
    if ($hasSetupLayout) {
        $SourceDir = $PSScriptRoot
    } else {
        $repoRoot = Split-Path -Parent $PSScriptRoot
        $SourceDir = Join-Path $repoRoot 'build\client-setup\stage'
    }
}

if (Test-Path $SourceDir) {
    $SourceDir = (Resolve-Path $SourceDir).Path
}

$bundleSourceDir = if (Test-Path (Join-Path $SourceDir 'bundle')) {
    Join-Path $SourceDir 'bundle'
} else {
    $null
}

$serverSourceDir = if (Test-Path (Join-Path $SourceDir 'server')) {
    Join-Path $SourceDir 'server'
} else {
    $null
}

$manifestPath = Join-Path $SourceDir 'manifest.json'
$manifest = $null
$setupVersion = 'dev'
if (Test-Path $manifestPath) {
    try {
        $manifest = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json
        if ($manifest.version) { $setupVersion = [string]$manifest.version }
    } catch {
        throw ("[setup] could not parse manifest.json: {0}" -f $_.Exception.Message)
    }
}
if ($setupVersion -notmatch '^v?\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?$' -and $setupVersion -ne 'dev') {
    throw '[setup] Invalid version in manifest.json.'
}

# Fixed path for every version: MCP clients are configured once and an update
# only replaces the files behind it.
if (-not $ServerInstallRoot) {
    $ServerInstallRoot = Join-Path $env:LOCALAPPDATA 'Bimwright\ipt-mcp\server\current'
}

# --- OS boundaries (functions so tests can redirect them to fixtures) ---------

function Get-UserBundleRoot {
    return Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\Bimwright.Ipt.bundle'
}

# Roots scanned for stray manifests carrying an ipt-mcp ClientId: sibling
# *.bundle folders beside our bundle, plus the legacy per-user add-in folder.
function Get-UserScanRoots {
    $parent = Split-Path -Parent (Get-UserBundleRoot)
    $roots = @()
    if ($parent) { $roots += $parent }
    $roots += (Join-Path $env:APPDATA 'Autodesk\Inventor\Addins')
    return @($roots | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Container) })
}

# Machine-wide copies shadow or double-load the per-user add-in, and a per-user
# installer cannot remove them - their presence blocks the install.
function Get-MachineScanRoots {
    return @(
        (Join-Path $env:ProgramData 'Autodesk\ApplicationPlugins'),
        (Join-Path $env:ProgramData 'Autodesk\Inventor\Addins')
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Container }
}

# Product identity, unchanged since first release: the per-year ClientId each
# Bimwright.Ipt.InvNN.addin manifest carries (matches the [Guid] on the add-in).
function Get-IptMcpClientIds {
    return @(
        '2f4f08c6-e88b-4b75-92a8-b9c52244c169',  # 2022
        'e6e68fdf-601c-4f25-98c9-a814a3fc6f01',  # 2023
        'b562fc6e-f594-44e2-b1b8-561abe81bf2c',  # 2024
        'b1d25025-0000-4a25-9b25-bf1e2025c0de',  # 2025
        'b1d26026-0000-4a26-9b26-bf1e2026c0de',  # 2026
        'b1d27027-0000-4a27-9b27-bf1e2027c0de'   # 2027
    )
}

# Unreadable or malformed manifests belong to someone else: return $null, never throw.
function Read-AddinManifest([string]$Path) {
    try { [xml]$xml = [IO.File]::ReadAllText($Path) } catch { return $null }
    try {
        $addin = @($xml.SelectNodes('//Addin'))[0]
        if (-not $addin) { return $null }
        $clientId = $addin.SelectSingleNode('ClientId')
        $assembly = $addin.SelectSingleNode('Assembly')
        return [pscustomobject]@{
            ClientId = if ($clientId -and $clientId.InnerText) { $clientId.InnerText.Trim().Trim('{', '}').ToLowerInvariant() } else { $null }
            Assembly = if ($assembly -and $assembly.InnerText) { $assembly.InnerText.Trim() } else { $null }
        }
    } catch { return $null }
}

# Every *.addin file under $Folder whose ClientId is one of ours - loose files,
# bundle Contents\<year>\ folders, anything. Callers exclude our own target.
function Find-IptMcpAddinManifests([string]$Folder) {
    if (-not $Folder -or -not (Test-Path -LiteralPath $Folder -PathType Container)) { return @() }
    $ids = Get-IptMcpClientIds
    $found = @()
    foreach ($file in Get-ChildItem -LiteralPath $Folder -Filter '*.addin' -File -Recurse -ErrorAction SilentlyContinue) {
        $manifest = Read-AddinManifest $file.FullName
        if ($manifest -and $manifest.ClientId -and ($ids -contains $manifest.ClientId)) { $found += $file.FullName }
    }
    return $found
}

function Find-ServerSourceExe {
    param([string]$ServerDir)
    if (-not $ServerDir) { return $null }
    $preferred = Join-Path $ServerDir 'ipt-mcp.exe'
    if (Test-Path $preferred) { return $preferred }
    $fallback = Join-Path $ServerDir 'Bimwright.Ipt.Server.exe'
    if (Test-Path $fallback) { return $fallback }
    return $null
}

function Get-AgentsGuidePath([string]$ScriptRoot) {
    if ($ScriptRoot) {
        foreach ($candidate in @((Join-Path $ScriptRoot 'AGENTS.md'), (Join-Path $ScriptRoot 'README.md'))) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
        }
    }
    return 'https://github.com/bimwright/ipt-mcp'
}

# Every backup is beside its target. Never delete a caller-supplied directory
# unless it is a recorded target of this transaction or our unique staging area.
function Remove-InstallPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $allowed = @($script:installStage)
    foreach ($change in $script:installChanges) { $allowed += $change.Path; $allowed += $change.Backup }
    if ($full -notin $allowed -or $full.TrimEnd('\') -eq [IO.Path]::GetPathRoot($full).TrimEnd('\')) {
        throw "Refusing cleanup outside this install transaction: $full"
    }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}

# Mode 'copy' (default): back the old directory up as a full copy before
# touching it. A locked child makes Copy-Item throw before anything changes,
# and a mid-install failure restores from a complete backup.
# Mode 'rename': move the old directory aside instead. A recursive directory
# move can stop halfway on a locked child, so the change is recorded before
# the move and undo merges the backup back over whatever remains. Needed for
# server\current: a running server image does not block the rename, so the
# swap still succeeds and the in-use copy is kept whole afterwards.
function Set-InstallPath([string]$Source, [string]$Destination, [string]$Mode = 'copy') {
    $full = [IO.Path]::GetFullPath($Destination)
    if ($full.TrimEnd('\') -eq [IO.Path]::GetPathRoot($full).TrimEnd('\')) { throw 'Cannot install into a drive root.' }
    $parent = Split-Path -Parent $full
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    if (Test-Path -LiteralPath $full) {
        $backup = $full + '.iptmcp-rollback-' + [guid]::NewGuid().ToString('N')
        if ($Mode -eq 'rename') {
            # Record before moving: a recursive move can stop halfway on a
            # locked child and must still be restorable from the backup.
            $change = [pscustomobject]@{Path=$full;Backup=$backup;Mode='rename';MovedOld=$false}
            $script:installChanges.Add($change)
            Move-Item -LiteralPath $full -Destination $backup
            $change.MovedOld = $true
        } else {
            # A failed copy leaves the destination untouched, so nothing is
            # recorded; a recorded copy-mode backup is always complete.
            try { Copy-Item -LiteralPath $full -Destination $backup -Recurse }
            catch { Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue; throw }
            $script:installChanges.Add([pscustomobject]@{Path=$full;Backup=$backup;Mode='copy';MovedOld=$true})
            Remove-InstallPath $full
        }
    } else {
        $script:installChanges.Add([pscustomobject]@{Path=$full;Backup=$null;Mode=$Mode;MovedOld=$false})
    }
    Move-Item -LiteralPath $Source -Destination $full
}

function Move-ToRollback([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $backup = $full + '.iptmcp-rollback-' + [guid]::NewGuid().ToString('N')
    Move-Item -LiteralPath $full -Destination $backup
    $script:installChanges.Add([pscustomobject]@{Path=$full;Backup=$backup;Mode='rename';MovedOld=$true})
}

function Undo-InstallChanges {
    $failures = @()
    for ($i = $script:installChanges.Count - 1; $i -ge 0; $i--) {
        $change = $script:installChanges[$i]
        try {
            if (-not $change.Backup) {
                Remove-InstallPath $change.Path
            } elseif (Test-Path -LiteralPath $change.Backup) {
                if ($change.Mode -eq 'rename' -and -not $change.MovedOld -and (Test-Path -LiteralPath $change.Path)) {
                    # The first move stopped halfway: moved children sit in the
                    # backup, the rest are still at the target. Copy the backup
                    # back over the target to reassemble the old state.
                    Copy-Item -LiteralPath (Join-Path $change.Backup '*') -Destination $change.Path -Recurse -Force
                    Remove-Item -LiteralPath $change.Backup -Recurse -Force
                } else {
                    Remove-InstallPath $change.Path
                    Move-Item -LiteralPath $change.Backup -Destination $change.Path
                }
            }
            # else: the rename failed before the backup existed; the original
            # is still untouched at Path.
        } catch { $failures += "$($change.Path): $_ (backup: $($change.Backup))" }
    }
    if ($failures.Count) { throw ("Rollback incomplete; retain backup files and restore manually:`n" + ($failures -join "`n")) }
}

function Assert-InventorClosed {
    if (@(Get-Process -Name Inventor -ErrorAction SilentlyContinue).Count) {
        throw 'Inventor running. Close every Inventor window before installing or uninstalling the add-in bundle; no files have been replaced.'
    }
}

# The package bundle must be a real ApplicationPlugins payload: a parseable
# PackageContents.xml and, under Contents\, only .addin manifests that carry our
# ClientIds and point at assembly files that ship beside them.
function Assert-BundleSource([string]$BundleDir) {
    $prefix = "Invalid plugin bundle at ${BundleDir}:"
    $pcPath = Join-Path $BundleDir 'PackageContents.xml'
    if (-not (Test-Path -LiteralPath $pcPath -PathType Leaf)) { throw "$prefix missing PackageContents.xml" }
    try { [xml]$pc = [IO.File]::ReadAllText($pcPath) } catch { throw "$prefix PackageContents.xml does not parse" }
    # .Name collides with the Name attribute in PowerShell's XML adapter; use LocalName.
    if (-not $pc.DocumentElement -or $pc.DocumentElement.LocalName -ne 'ApplicationPackage' -or
        $pc.DocumentElement.GetAttribute('AutodeskProduct') -ne 'Inventor') {
        throw "$prefix PackageContents.xml is not an Inventor ApplicationPackage"
    }
    $contents = Join-Path $BundleDir 'Contents'
    $yearDirs = @(Get-ChildItem -LiteralPath $contents -Directory -ErrorAction SilentlyContinue)
    if ($yearDirs.Count -eq 0) { throw "$prefix Contents\ holds no per-year folders" }
    $ids = Get-IptMcpClientIds
    $addinCount = 0
    foreach ($dir in $yearDirs) {
        $addins = @(Get-ChildItem -LiteralPath $dir.FullName -Filter '*.addin' -File -ErrorAction SilentlyContinue)
        if ($addins.Count -eq 0) { throw "$prefix Contents\$($dir.Name) has no .addin manifest" }
        foreach ($addinFile in $addins) {
            $parsed = Read-AddinManifest $addinFile.FullName
            if (-not $parsed -or -not $parsed.ClientId -or ($ids -notcontains $parsed.ClientId)) {
                throw "$prefix $($addinFile.Name) carries an unexpected ClientId"
            }
            if (-not $parsed.Assembly -or -not (Test-Path -LiteralPath (Join-Path $dir.FullName $parsed.Assembly) -PathType Leaf)) {
                throw "$prefix $($addinFile.Name) points at a missing assembly"
            }
            $addinCount++
        }
    }
    if ($addinCount -eq 0) { throw "$prefix no ipt-mcp .addin manifests found" }
}

function Assert-SetupManifest([string]$Root, $Manifest) {
    if (-not $Manifest) { return } # Packages without a manifest skip checksum validation.
    if (-not $Manifest.files) { throw 'Setup manifest has no file checksums.' }
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    foreach ($file in $Manifest.files) {
        $path = [IO.Path]::GetFullPath((Join-Path $Root $file.path))
        if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Invalid manifest path: $($file.path)" }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Setup file missing: $($file.path)" }
        # Get-FileHash's provider reads honor inherited WhatIf in Windows
        # PowerShell 5.1. Hash directly so previews still validate actual bytes.
        $stream = [IO.File]::OpenRead($path)
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($hash -ne $file.sha256) {
            throw "Setup checksum failed: $($file.path). Download and extract the setup ZIP again."
        }
    }
}

function Get-StreamSha256($Stream) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($Stream)).Replace('-', '') } finally { $sha.Dispose() }
}

function Get-FileSha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try { return Get-StreamSha256 $stream } finally { $stream.Dispose() }
}

# The installed bundle must be exactly what the package holds: every source file
# present with the same bytes, and no extra files left behind at the target.
function Assert-InstalledBundle([string]$SourceDir, [string]$TargetDir) {
    $prefix = "Bundle verification failed:"
    $sourceFull = [IO.Path]::GetFullPath($SourceDir).TrimEnd('\') + '\'
    $targetFull = [IO.Path]::GetFullPath($TargetDir).TrimEnd('\') + '\'
    $sourceFiles = @(Get-ChildItem -LiteralPath $SourceDir -File -Recurse)
    foreach ($file in $sourceFiles) {
        $relative = $file.FullName.Substring($sourceFull.Length)
        $target = Join-Path $targetFull $relative
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "$prefix missing file $relative" }
        if ((Get-FileSha256 $file.FullName) -ne (Get-FileSha256 $target)) { throw "$prefix $relative differs from the package" }
    }
    foreach ($file in @(Get-ChildItem -LiteralPath $TargetDir -File -Recurse)) {
        $relative = $file.FullName.Substring($targetFull.Length)
        if (-not (Test-Path -LiteralPath (Join-Path $sourceFull $relative) -PathType Leaf)) { throw "$prefix extra file $relative" }
    }
}

function Install-IptMcpServer {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [string]$ServerDir,
        [string]$InstallRoot
    )
    $sourceExe = Find-ServerSourceExe -ServerDir $ServerDir
    if (-not $sourceExe) { return $null }

    $plannedExe = Join-Path $InstallRoot (Split-Path -Leaf $sourceExe)
    if ($PSCmdlet.ShouldProcess($InstallRoot, 'Install self-contained ipt-mcp server')) {
        # Rename so a server exe an MCP client still runs does not block the
        # swap; the kept copy is reported under 'In use' and swept once free.
        Set-InstallPath -Source $ServerDir -Destination $InstallRoot -Mode rename
        Write-Host ("[server] installed -> {0}" -f $plannedExe)
    } else {
        Write-Host ("[server] preview install -> {0}" -f $plannedExe)
    }
    return $plannedExe
}

# --help returns before any side effect in src/server/Program.cs, so this only
# proves the installed executable can start (antivirus, policy, corruption).
function Test-ServerExecutable([string]$Path, [int]$TimeoutSeconds = 30, [string]$Arguments = '--help') {
    $hint = 'Antivirus or policy may have blocked it. Previous installation restored.'
    $psi = New-Object Diagnostics.ProcessStartInfo $Path
    $psi.Arguments = $Arguments
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    try { $process = [Diagnostics.Process]::Start($psi) }
    catch { throw ("Server executable could not start ({0}). {1}" -f $_.Exception.Message, $hint) }
    try {
        $null = $process.StandardOutput.ReadToEndAsync()
        $null = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try { $process.Kill() } catch { }
            throw ("Server executable could not start (no exit after {0} s). {1}" -f $TimeoutSeconds, $hint)
        }
        if ($process.ExitCode -ne 0) { throw ("Server executable could not start (exit code {0}). {1}" -f $process.ExitCode, $hint) }
    } finally { $process.Dispose() }
}

function Get-OtherServerVersions([string]$InstallRoot) {
    $parent = Split-Path -Parent $InstallRoot
    if (-not $parent -or -not (Test-Path -LiteralPath $parent)) { return @() }
    $current = Split-Path -Leaf $InstallRoot
    return @(Get-ChildItem -LiteralPath $parent -Directory | Where-Object {
        $_.Name -ne $current -and $_.Name -match '^v?\d+\.\d+\.\d+([-+][A-Za-z0-9.-]+)?$'
    })
}

function Test-ServerCopy([string]$Dir) {
    return (Test-Path -LiteralPath (Join-Path $Dir 'ipt-mcp.exe')) -or (Test-Path -LiteralPath (Join-Path $Dir 'Bimwright.Ipt.Server.exe'))
}

# A running server's exe cannot be deleted. Probe it first so a copy that an
# MCP client still runs is kept whole instead of half-deleted.
function Remove-ServerCopy([string]$Dir) {
    foreach ($name in 'ipt-mcp.exe', 'Bimwright.Ipt.Server.exe') {
        $exe = Join-Path $Dir $name
        if (Test-Path -LiteralPath $exe) {
            try { Remove-Item -LiteralPath $exe -Force -ErrorAction Stop } catch { return $false }
        }
    }
    try { Remove-Item -LiteralPath $Dir -Recurse -Force -ErrorAction Stop; return $true }
    catch { Write-Warning ("Could not fully remove {0}: {1}" -f $Dir, $_.Exception.Message); return $false }
}

# Only names this installer creates: <name>.iptmcp-rollback-<32 hex>.
function Get-LeftoverServerCopies([string]$ServerParent) {
    if (-not (Test-Path -LiteralPath $ServerParent -PathType Container)) { return @() }
    return @(Get-ChildItem -LiteralPath $ServerParent -Directory | Where-Object { $_.Name -match '^.+\.iptmcp-rollback-[0-9a-f]{32}$' })
}

# Legacy version directories (from installers before server\current) are kept
# by default because clients may still point at them; -PruneOldServers opts
# into removal. Only version-shaped directories are touched (current\, dev\ and
# arbitrary names are preserved). Each move is recorded in the transaction:
# rollback restores them, and the post-install sweep deletes the backups with
# the exe-first rule. A locked directory is skipped, not fatal.
function Remove-StaleServerVersions {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([string]$InstallRoot)
    foreach ($dir in Get-OtherServerVersions -InstallRoot $InstallRoot) {
        if ($PSCmdlet.ShouldProcess($dir.FullName, 'Remove stale server version')) {
            $backup = "$($dir.FullName).iptmcp-rollback-$([guid]::NewGuid().ToString('N'))"
            try {
                Move-Item -LiteralPath $dir.FullName -Destination $backup
                $script:installChanges.Add([pscustomobject]@{Path=$dir.FullName;Backup=$backup})
                Write-Host ("[server] removed stale version -> {0}" -f $dir.FullName)
            } catch {
                Write-Warning ("[server] could not remove stale version {0}: {1}" -f $dir.FullName, $_.Exception.Message)
            }
        } else {
            Write-Host ("[server] preview remove stale version -> {0}" -f $dir.FullName)
        }
    }
}

# --- Main ---------------------------------------------------------------------
$agentsGuide = Get-AgentsGuidePath $PSScriptRoot

$handled = @()
$skipped = @()
$previewed = @()
$removedDuplicates = @()
$verified = @()
$inUse = @()
$legacyServers = @()
$serverCommand = $null
$serverCheck = 'not run'
# A caller-supplied -ServerInstallRoot's parent is not ours to prune or sweep.
$ownServerRoot = -not $PSBoundParameters.ContainsKey('ServerInstallRoot')
$script:installChanges = New-Object System.Collections.Generic.List[object]
$script:installStage = $null

$bundleTarget = Get-UserBundleRoot
$bundleTargetFull = [IO.Path]::GetFullPath($bundleTarget).TrimEnd('\')

try {
    # Validate every selected payload before replacing any installed file.
    if (-not $WhatIfPreference) { Assert-InventorClosed }
    if (-not $Uninstall) {
        if (-not $bundleSourceDir -and -not $serverSourceDir) {
            throw "[setup] no payload found at $SourceDir (expected bundle\ and/or server\)."
        }
        Assert-SetupManifest -Root $SourceDir -Manifest $manifest
        if ($bundleSourceDir) { Assert-BundleSource $bundleSourceDir }
        if ($serverSourceDir -and -not (Find-ServerSourceExe $serverSourceDir)) { throw 'Setup server executable is missing.' }
    }
    # A machine-wide manifest with one of our ClientIds would shadow or
    # double-load the per-user add-in, and a per-user installer cannot remove it.
    $machineDupes = @()
    foreach ($root in Get-MachineScanRoots) {
        $machineDupes += @(Find-IptMcpAddinManifests -Folder $root)
    }
    if ($machineDupes.Count) {
        $message = "A machine-wide ipt-mcp add-in manifest exists at $($machineDupes -join ', ') (same ClientId). Remove it with administrator rights"
        if ($Uninstall) { Write-Warning "$message; it was left in place." }
        else { throw "$message, then run the installer again. Nothing was changed." }
    }

    # Stray per-user manifests carrying our ClientIds (Bimwright-era copies,
    # leftovers in sibling bundles, the legacy Addins folder) are removed:
    # Inventor loading two manifests with the same ClientId misbehaves.
    $userDupes = @()
    foreach ($root in Get-UserScanRoots) {
        $userDupes += @(Find-IptMcpAddinManifests -Folder $root | Where-Object {
            -not ([IO.Path]::GetFullPath($_)).StartsWith($bundleTargetFull + '\', [StringComparison]::OrdinalIgnoreCase)
        })
    }

    # Post-install verification compares the target against the pristine package;
    # the staged copies are consumed by the move, so keep the package paths.
    $packageBundleDir = $bundleSourceDir

    if (-not $Uninstall -and -not $WhatIfPreference) {
        $script:installStage = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('iptmcp-install-' + [guid]::NewGuid().ToString('N'))))
        New-Item -ItemType Directory -Path $script:installStage | Out-Null
        if ($bundleSourceDir) {
            Copy-Item -LiteralPath $bundleSourceDir -Destination (Join-Path $script:installStage 'bundle') -Recurse
            $bundleSourceDir = Join-Path $script:installStage 'bundle'
        }
        if ($serverSourceDir) {
            Copy-Item -LiteralPath $serverSourceDir -Destination (Join-Path $script:installStage 'server') -Recurse
            $serverSourceDir = Join-Path $script:installStage 'server'
        }
        # Recheck after staging; a user may have launched Inventor meanwhile.
        Assert-InventorClosed
    }

    if ($Uninstall) {
        $targets = @(@($bundleTarget) + $userDupes | Where-Object { Test-Path -LiteralPath $_ })
        foreach ($item in $targets) {
            if ($PSCmdlet.ShouldProcess($item, 'Remove ipt-mcp add-in payload')) { Remove-Item -LiteralPath $item -Recurse -Force }
        }
        foreach ($item in $userDupes) { $removedDuplicates += (Split-Path -Leaf $item) }
        if ($targets.Count) {
            if ($WhatIfPreference) {
                Write-Host ("[bundle] preview uninstall from {0}" -f $bundleTarget)
                $previewed += 'bundle'
            } else {
                Write-Host ("[bundle] uninstalled from {0}" -f $bundleTarget)
                $handled += 'bundle'
            }
        } else {
            Write-Host ("[bundle] nothing to remove at {0}" -f $bundleTarget)
            $skipped += 'bundle'
        }
    } else {
        foreach ($item in $userDupes) {
            if ($PSCmdlet.ShouldProcess($item, 'Remove duplicate ipt-mcp add-in manifest (same ClientId)')) { Move-ToRollback $item }
            $removedDuplicates += (Split-Path -Leaf $item)
        }

        if ($bundleSourceDir) {
            if ($PSCmdlet.ShouldProcess($bundleTarget, 'Install staged add-in bundle with rollback')) {
                Set-InstallPath -Source $bundleSourceDir -Destination $bundleTarget
            }
            if ($WhatIfPreference) {
                Write-Host ("[bundle] preview install -> {0}" -f $bundleTarget)
                $previewed += 'bundle'
            } else {
                Write-Host ("[bundle] installed -> {0}" -f $bundleTarget)
                $handled += 'bundle'
            }
        }

        $serverCommand = Install-IptMcpServer -ServerDir $serverSourceDir -InstallRoot $ServerInstallRoot
        if ($serverCommand -and $ownServerRoot) {
            if ($PruneOldServers) {
                Remove-StaleServerVersions -InstallRoot $ServerInstallRoot
            } else {
                $legacyServers = @(Get-OtherServerVersions -InstallRoot $ServerInstallRoot)
            }
        }
        if ($serverCommand -and -not $WhatIfPreference) {
            # A browser-downloaded ZIP extracted by Explorer marks every file as
            # coming from the Internet; Copy-Item/Move-Item keep that mark.
            Get-ChildItem -LiteralPath $ServerInstallRoot -Recurse -File | Unblock-File
            Test-ServerExecutable -Path $serverCommand
            $serverCheck = 'OK'
        } elseif ($serverCommand) {
            $serverCheck = 'skipped (WhatIf)'
        }
        if ($packageBundleDir -and -not $WhatIfPreference) {
            Assert-InstalledBundle -SourceDir $packageBundleDir -TargetDir $bundleTarget
            $verified += 'bundle'
        }
    }
} catch {
    $installFailure = $_
    Undo-InstallChanges
    throw $installFailure
} finally {
    if ($script:installStage) {
        try { Remove-InstallPath $script:installStage } catch { Write-Warning "Staging cleanup failed: $_" }
    }
}
# Keep backups until every add-in and server change has succeeded.
foreach ($change in $script:installChanges) {
    if (-not $change.Backup -or -not (Test-Path -LiteralPath $change.Backup)) { continue }
    if (Test-ServerCopy $change.Backup) {
        if (-not (Remove-ServerCopy $change.Backup)) { $inUse += $change.Backup }
    } else {
        try { Remove-InstallPath $change.Backup } catch { Write-Warning "Backup retained at $($change.Backup): $_" }
    }
}
# Previous runs may have left copies that a client was still running.
if (-not $Uninstall -and -not $WhatIfPreference -and $serverCommand -and $ownServerRoot) {
    foreach ($dir in Get-LeftoverServerCopies (Split-Path -Parent ([IO.Path]::GetFullPath($ServerInstallRoot)))) {
        if ($inUse -contains $dir.FullName) { continue }
        if (-not (Remove-ServerCopy $dir.FullName)) { $inUse += $dir.FullName }
    }
}
# Bundle-level rollback leftovers (retained when a file inside was still
# locked) are swept once free. Exact-name match only; Inventor ignores the
# folder because its name does not end in .bundle.
if (-not $Uninstall -and -not $WhatIfPreference) {
    $bundleParent = Split-Path -Parent ([IO.Path]::GetFullPath($bundleTarget))
    foreach ($leftover in Get-LeftoverServerCopies $bundleParent) {
        if ($leftover.Name -notlike 'Bimwright.Ipt.bundle.iptmcp-rollback-*') { continue }
        # Not Remove-InstallPath: leftovers from earlier runs are not in this
        # transaction's record, so the cleanup guard would refuse them.
        try { Remove-Item -LiteralPath $leftover.FullName -Recurse -Force } catch { $inUse += $leftover.FullName }
    }
}
$script:installChanges = $null

Write-Host ""
Write-Host "=== install.ps1 summary ==="
Write-Host ("Mode    : {0}" -f $(if ($Uninstall) { 'Uninstall' } else { 'Install' }))
if (-not $Uninstall) { Write-Host ("Version : {0}" -f $setupVersion) }
Write-Host ("Source  : {0}" -f $SourceDir)
Write-Host ("Bundle  : {0}" -f $bundleTarget)
Write-Host ("Handled : {0}" -f $(if ($handled.Count) { $handled -join ', ' } else { 'none' }))
if ($previewed.Count) { Write-Host ("Previewed: {0}" -f ($previewed -join ', ')) }
if ($skipped.Count) { Write-Host ("Skipped : {0}" -f ($skipped -join ', ')) }
if ($removedDuplicates.Count) { Write-Host ("Removed : {0}{1}" -f ($removedDuplicates -join ', '), $(if ($WhatIfPreference) { ' (preview)' } else { '' })) }
if ($verified.Count) { Write-Host ("Verified: {0} match the package" -f ($verified -join ', ')) }
if (-not $Uninstall) {
    if ($serverCommand) { Write-Host ("Server  : {0} (check: {1})" -f $serverCommand, $serverCheck) }
    else { Write-Host 'Server  : not in this package' }
}
if ($inUse.Count) { Write-Host ("In use  : {0} - restart MCP clients; removed at next install" -f ($inUse -join ', ')) }
if ($legacyServers.Count) { Write-Host ("Legacy  : {0} - repoint clients to the Server path, then run install.ps1 -PruneOldServers" -f (($legacyServers | ForEach-Object { $_.Name }) -join ', ')) }
if (-not $Uninstall) { Write-Host ("Next    : connect MCP clients - see {0}" -f $agentsGuide) }
