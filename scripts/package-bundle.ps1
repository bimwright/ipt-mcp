<#
.SYNOPSIS
    Builds the per-version Inventor add-ins and assembles a per-user, registry-free
    Autodesk ApplicationPlugins bundle for inventor-mcp.

.DESCRIPTION
    Lays out:

        %APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Inventor.bundle\
          PackageContents.xml              <- ApplicationPackage entry point (one <Components> per year)
          Contents\
            2022\  Bimwright.Inventor.Plugin.Inv22.dll  (+ deps)  Bimwright.Inventor.Inv22.addin
            2023\  ...
            ...
            2027\  Bimwright.Inventor.Plugin.Inv27.dll  (+ deps)  Bimwright.Inventor.Inv27.addin

    Inventor auto-discovers add-ins placed under %APPDATA%\Autodesk\ApplicationPlugins\*.bundle
    via PackageContents.xml, so NO COM registry registration is required (registry-free deploy).

    For each year the script runs `dotnet build` on the matching plugin project. Where the
    Inventor interop for that year is present on this machine it does a REAL build; where it is
    absent it falls back to `/p:SkipInventorReferenceCheck=true` so the project SHAPE still
    compiles (the resulting DLL is shape-only and must be rebuilt on a box with that year's SDK
    before shipping). Build failures are reported per-year and do not abort the other years.

    The PackageContents.xml and the .addin element schema generated here are BEST-EFFORT (see the
    FLAG emitted at the end and the comments inside the generated XML). They MUST be verified
    against the installed Inventor SDK / ObjectARX-style schema before release — this build box
    has no runnable Inventor.

.PARAMETER Years
    Which Inventor years to package. Default: 2022..2027.

.PARAMETER Configuration
    dotnet build configuration. Default: Release.

.PARAMETER BundleRoot
    Override the bundle destination. Default:
    %APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Inventor.bundle

.PARAMETER DryRun
    Plan only: print every build/copy/write action without touching the build outputs or the
    bundle directory. Safe to run anywhere.

.EXAMPLE
    pwsh scripts/package-bundle.ps1 -DryRun

.EXAMPLE
    pwsh scripts/package-bundle.ps1 -Years 2025,2026,2027 -Configuration Release
#>
[CmdletBinding()]
param(
    [int[]]   $Years = @(2022, 2023, 2024, 2025, 2026, 2027),
    [string]  $Configuration = 'Release',
    [string]  $BundleRoot,
    [switch]  $DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# --- Constants -------------------------------------------------------------------------------
$BundleName  = 'Bimwright.Inventor.bundle'
$VendorId    = 'Bimwright'
$ProductName = 'Bimwright Inventor MCP'

# repo root = parent of scripts/
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir
$SrcDir    = Join-Path $RepoRoot 'src'

if (-not $BundleRoot) {
    $BundleRoot = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\$BundleName"
}
$ContentsDir = Join-Path $BundleRoot 'Contents'

function Write-Step   { param([string]$m) Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Action { param([string]$m) Write-Host ("    {0}{1}" -f ($(if ($DryRun) { '[dry-run] ' } else { '' })), $m) }
function Write-Warn2  { param([string]$m) Write-Host "    !! $m" -ForegroundColor Yellow }

function InteropPathFor([int]$year) {
    Join-Path "C:\Program Files\Common Files\Autodesk Shared\Extensions $year\Framework\Interop" 'Autodesk.Inventor.Interop.dll'
}

# Internal major version = calendar year - 1996 (2022=26 ... 2027=31). Mirrors the .addin brackets.
function InternalMajorFor([int]$year) { return $year - 1996 }

# --- Plan / results --------------------------------------------------------------------------
$summary = [System.Collections.Generic.List[object]]::new()

Write-Step "inventor-mcp bundle packaging  (Configuration=$Configuration, DryRun=$DryRun)"
Write-Action "Repo root : $RepoRoot"
Write-Action "Bundle    : $BundleRoot"
Write-Action "Years     : $($Years -join ', ')"
Write-Host ""

# --- 1. Build each plugin --------------------------------------------------------------------
foreach ($year in $Years) {
    $nn          = $year - 2000
    $projDirName = "plugin-inv$nn"
    $projDir     = Join-Path $SrcDir $projDirName
    $proj        = Join-Path $projDir ("Bimwright.Inventor.Plugin.Inv{0:00}.csproj" -f $nn)
    $asmName     = "Bimwright.Inventor.Plugin.Inv{0:00}" -f $nn
    $addinName   = "Bimwright.Inventor.Inv{0:00}.addin" -f $nn
    $addinPath   = Join-Path $projDir $addinName

    $entry = [pscustomobject]@{
        Year       = $year
        Project    = $proj
        Assembly   = $asmName
        Addin      = $addinName
        RealBuild  = $false
        Built      = $false
        Copied     = $false
        Note       = ''
    }

    Write-Step "Inventor $year  ($projDirName, $asmName.dll)"

    if (-not (Test-Path $proj)) {
        $entry.Note = "project not found: $proj"
        Write-Warn2 $entry.Note
        $summary.Add($entry); continue
    }
    if (-not (Test-Path $addinPath)) {
        $entry.Note = "manifest not found: $addinPath"
        Write-Warn2 $entry.Note
        $summary.Add($entry); continue
    }

    $interop      = InteropPathFor $year
    $hasInterop   = Test-Path $interop
    $entry.RealBuild = $hasInterop

    $buildArgs = @('build', $proj, '-c', $Configuration, '--nologo')
    if (-not $hasInterop) {
        $buildArgs += '/p:SkipInventorReferenceCheck=true'
        Write-Warn2 "interop for $year not found ($interop) -> SHAPE-ONLY build (/p:SkipInventorReferenceCheck=true). Rebuild on a box with the $year SDK before shipping."
    } else {
        Write-Action "interop found: $interop -> real build"
    }

    Write-Action ("dotnet {0}" -f ($buildArgs -join ' '))
    if (-not $DryRun) {
        & dotnet @buildArgs
        if ($LASTEXITCODE -ne 0) {
            $entry.Note = "build FAILED (exit $LASTEXITCODE)"
            Write-Warn2 $entry.Note
            $summary.Add($entry); continue
        }
        $entry.Built = $true
    }

    # --- 2. Locate build output and copy into the bundle -------------------------------------
    # net48 outputs to bin\<cfg>\net48; net8/net10 to bin\<cfg>\<tfm>. Discover the newest dir
    # holding the assembly so we don't hard-code the TFM folder.
    $binRoot   = Join-Path $projDir "bin\$Configuration"
    $destDir   = Join-Path $ContentsDir "$year"

    if ($DryRun) {
        Write-Action "ensure dir : $destDir"
        Write-Action "copy       : $asmName.dll (+ runtime deps) -> $destDir"
        Write-Action "copy       : $addinName -> $destDir"
        $entry.Copied = $true   # planned
        $summary.Add($entry); Write-Host ""; continue
    }

    if (-not (Test-Path $binRoot)) {
        $entry.Note = "no build output under $binRoot"
        Write-Warn2 $entry.Note
        $summary.Add($entry); continue
    }

    $dll = Get-ChildItem -Path $binRoot -Recurse -Filter "$asmName.dll" -ErrorAction SilentlyContinue |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $dll) {
        $entry.Note = "built but $asmName.dll not found under $binRoot"
        Write-Warn2 $entry.Note
        $summary.Add($entry); continue
    }
    $outDir = $dll.Directory.FullName

    New-Item -ItemType Directory -Force -Path $destDir | Out-Null

    # Copy the primary DLL + its non-framework runtime deps (Newtonsoft, Roslyn, etc.).
    # Skip the Inventor interop itself (loaded from the Inventor install, <Private>false</Private>).
    $skip = @('Autodesk.Inventor.Interop.dll')
    Get-ChildItem -Path $outDir -Filter '*.dll' | Where-Object { $skip -notcontains $_.Name } |
        ForEach-Object { Copy-Item $_.FullName -Destination $destDir -Force }

    # Carry the deps manifest / config for net8/net10 isolated loading if present.
    foreach ($ext in @("$asmName.deps.json", "$asmName.runtimeconfig.json")) {
        $extra = Join-Path $outDir $ext
        if (Test-Path $extra) { Copy-Item $extra -Destination $destDir -Force }
    }

    Copy-Item $addinPath -Destination (Join-Path $destDir $addinName) -Force
    $entry.Copied = $true
    Write-Action "copied $asmName.dll (+ deps) and $addinName -> $destDir"
    $summary.Add($entry)
    Write-Host ""
}

# --- 3. Generate PackageContents.xml ---------------------------------------------------------
# Only emit <Components> for years we actually staged a DLL+addin for (or planned to, in dry-run).
$staged = $summary | Where-Object { $_.Copied }

$nl = [Environment]::NewLine
$sb = [System.Text.StringBuilder]::new()
[void]$sb.Append('<?xml version="1.0" encoding="utf-8"?>' + $nl)
[void]$sb.Append('<!--' + $nl)
[void]$sb.Append('  ============================================================================' + $nl)
[void]$sb.Append('  BEST-EFFORT PackageContents.xml for the Autodesk ApplicationPlugins bundle.' + $nl)
[void]$sb.Append('  FLAG: the EXACT ApplicationPackage / Components / ComponentEntry schema AND the' + $nl)
[void]$sb.Append('  per-year SeriesMin/SeriesMax product codes ("I" + internal major) MUST be verified' + $nl)
[void]$sb.Append('  against the installed Inventor SDK (Autodesk Developer docs / a known-good bundle)' + $nl)
[void]$sb.Append('  BEFORE release. This box has no runnable Inventor to confirm against.' + $nl)
[void]$sb.Append('  SeriesMin/Max use the Inventor product code where internal major = year - 1996.' + $nl)
[void]$sb.Append('  ============================================================================' + $nl)
[void]$sb.Append('-->' + $nl)
[void]$sb.Append(('<ApplicationPackage SchemaVersion="1.0" AutodeskProduct="Inventor" Name="{0}"' -f $ProductName) + $nl)
[void]$sb.Append('                    Description="Bimwright MCP gateway add-ins for Autodesk Inventor 2022-2027"' + $nl)
[void]$sb.Append('                    AppVersion="0.1.0" ProductType="Application" ProductCode="{B1MW0001-0000-0000-0000-000000000001}">' + $nl)
[void]$sb.Append('  <CompanyDetails Name="Bimwright" Url="https://github.com/bimwright/inventor-mcp" />' + $nl)
[void]$sb.Append('  <RuntimeRequirements OS="Win64" Platform="Inventor" />' + $nl)

foreach ($e in ($staged | Sort-Object Year)) {
    $year   = $e.Year
    $major  = InternalMajorFor $year       # e.g. 2025 -> 29
    $series = "I$major"                     # Inventor product code, e.g. I29. FLAG: confirm prefix/format.
    [void]$sb.Append(('  <Components Description="Inventor {0}">' -f $year) + $nl)
    [void]$sb.Append(('    <RuntimeRequirements OS="Win64" Platform="Inventor" SeriesMin="{0}" SeriesMax="{0}" />' -f $series) + $nl)
    # ModuleName points at the per-year .addin; that manifest's LoadAutomatically/SupportedSoftwareVersion
    # drives the actual load. FLAG: confirm Inventor honours a .addin as the ComponentEntry ModuleName
    # (vs. pointing directly at the DLL) against the SDK before release.
    [void]$sb.Append(('    <ComponentEntry AppName="{0} {1}" ModuleName="./Contents/{1}/{2}" />' -f $ProductName, $year, $e.Addin) + $nl)
    [void]$sb.Append('  </Components>' + $nl)
}

[void]$sb.Append('</ApplicationPackage>' + $nl)
$packageXml = $sb.ToString()

$pcPath = Join-Path $BundleRoot 'PackageContents.xml'
Write-Step "PackageContents.xml -> $pcPath"
if ($DryRun) {
    Write-Action "would write PackageContents.xml with $($staged.Count) <Components> block(s):"
    Write-Host ""
    Write-Host $packageXml
} else {
    New-Item -ItemType Directory -Force -Path $BundleRoot | Out-Null
    Set-Content -Path $pcPath -Value $packageXml -Encoding UTF8
    Write-Action "wrote PackageContents.xml ($($staged.Count) <Components> block(s))"
}

# --- 4. Summary + FLAG -----------------------------------------------------------------------
Write-Host ""
Write-Step "Summary"
$summary | Sort-Object Year | Format-Table `
    @{N='Year';E={$_.Year}}, `
    @{N='RealBuild';E={$_.RealBuild}}, `
    @{N='Built';E={if ($DryRun) {'(planned)'} else {$_.Built}}}, `
    @{N='Staged';E={if ($DryRun) {'(planned)'} else {$_.Copied}}}, `
    @{N='Note';E={$_.Note}} -AutoSize | Out-String | Write-Host

Write-Host "============================================================================" -ForegroundColor Yellow
Write-Host " FLAG — verify before release (no runnable Inventor on this build box):" -ForegroundColor Yellow
Write-Host "   1. PackageContents.xml: confirm the ApplicationPackage / Components /" -ForegroundColor Yellow
Write-Host "      ComponentEntry schema and the SeriesMin/SeriesMax product codes" -ForegroundColor Yellow
Write-Host "      ('I' + (year-1996)) against the installed Inventor SDK / a known-good bundle." -ForegroundColor Yellow
Write-Host "   2. .addin element names and the SupportedSoftwareVersion brackets: confirm" -ForegroundColor Yellow
Write-Host "      against the Inventor SDK 'Add-In Manifest' reference for each target year." -ForegroundColor Yellow
Write-Host "   3. Years whose interop was absent here built SHAPE-ONLY — rebuild each on a box" -ForegroundColor Yellow
Write-Host "      with that year's Inventor SDK installed (drop SkipInventorReferenceCheck)." -ForegroundColor Yellow
Write-Host "   4. Inventor must be CLOSED when deploying/replacing the bundle DLLs." -ForegroundColor Yellow
Write-Host "============================================================================" -ForegroundColor Yellow

if ($DryRun) { Write-Host "" ; Write-Host "Dry-run complete — nothing was built or written." -ForegroundColor Green }
