#Requires -Version 5.1
<#
.SYNOPSIS
  Build the end-user ipt-mcp setup ZIP. Ships only Inventor years with a real interop (no shape-only DLLs).
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Config = 'Release',
    [string]$RepoRoot,
    [string]$Version,
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
$RepoRoot = (Resolve-Path $RepoRoot).Path
if (-not $OutputDir) { $OutputDir = Join-Path $RepoRoot 'build\client-setup' }

if (-not $Version) {
    $serverJsonPath = Join-Path $RepoRoot 'server.json'
    $Version = ((Get-Content -Raw -Path $serverJsonPath) | ConvertFrom-Json).version
}
if (-not $Version) { throw 'Pass -Version or set server.json version.' }

$displayVersion = if ($Version.StartsWith('v')) { $Version } else { "v$Version" }
$stageRoot = Join-Path $OutputDir 'stage'
$serverStage = Join-Path $stageRoot 'server'
$bundleStage = Join-Path $stageRoot 'bundle'
$contentsStage = Join-Path $bundleStage 'Contents'

if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Path $serverStage, $contentsStage -Force | Out-Null

function Find-InventorInteropDir([int]$year) {
    $candidates = @(
        "C:\Program Files\Common Files\Autodesk Shared\Extensions $year\Framework\Interop",
        "C:\Program Files\Common Files\Autodesk Shared\Inventor Interoperability $year\Bin",
        "C:\Program Files\Autodesk\Inventor $year\Bin\Public Assemblies",
        "C:\Program Files\Autodesk\Inventor $year\Bin"
    )
    foreach ($dir in $candidates) {
        if (Test-Path (Join-Path $dir 'Autodesk.Inventor.Interop.dll')) { return $dir }
    }
    return $null
}

function TfmFor([int]$year) {
    if ($year -le 2024) { return 'net48' }
    if ($year -le 2026) { return 'net8.0-windows7.0' }
    return 'net10.0-windows7.0'
}

Write-Host "=== ipt-mcp package-client-setup ($displayVersion) ==="

$serverProject = Join-Path $RepoRoot 'src\server\Bimwright.Ipt.Server.csproj'
& dotnet publish $serverProject -c $Config -r win-x64 --self-contained true /p:PublishSingleFile=true -o $serverStage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

$published = Join-Path $serverStage 'Bimwright.Ipt.Server.exe'
$friendly = Join-Path $serverStage 'ipt-mcp.exe'
if (-not (Test-Path $published)) { throw "Missing $published" }
Move-Item $published $friendly -Force

$packed = @()
foreach ($year in 2022..2027) {
    $nn = '{0:00}' -f ($year - 2000)
    $interopDir = Find-InventorInteropDir $year
    if (-not $interopDir) {
        Write-Warning "Skipping Inventor $year (no Autodesk.Inventor.Interop.dll)."
        continue
    }
    $csproj = Join-Path $RepoRoot "src\plugin-inv$nn\Bimwright.Ipt.Plugin.Inv$nn.csproj"
    $addin = Join-Path $RepoRoot "src\plugin-inv$nn\Bimwright.Ipt.Inv$nn.addin"
    Write-Host "[plugin] Inventor $year ($interopDir)"
    & dotnet build $csproj -c $Config --nologo -v q /p:InventorInteropDir="$interopDir"
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Skipping Inventor $year (build failed)."
        continue
    }
    $tfm = TfmFor $year
    $outDir = Join-Path $RepoRoot "src\plugin-inv$nn\bin\$Config\$tfm"
    $asm = "Bimwright.Ipt.Plugin.Inv$nn"
    $dll = Join-Path $outDir "$asm.dll"
    if (-not (Test-Path $dll)) { throw "Built but missing $dll" }
    $dest = Join-Path $contentsStage "$year"
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Get-ChildItem $outDir -Filter '*.dll' | Where-Object { $_.Name -ne 'Autodesk.Inventor.Interop.dll' } |
        Copy-Item -Destination $dest -Force
    foreach ($extra in @("$asm.deps.json", "$asm.runtimeconfig.json")) {
        $p = Join-Path $outDir $extra
        if (Test-Path $p) { Copy-Item $p $dest -Force }
    }
    Copy-Item $addin (Join-Path $dest "Bimwright.Ipt.Inv$nn.addin") -Force
    $packed += [pscustomobject]@{ Year = $year; Addin = "Bimwright.Ipt.Inv$nn.addin" }
}

if ($packed.Count -eq 0) { throw 'No Inventor plugin years compiled against real interop. Cannot ship an empty ZIP.' }

$nl = [Environment]::NewLine
$sb = [System.Text.StringBuilder]::new()
[void]$sb.Append('<?xml version="1.0" encoding="utf-8"?>' + $nl)
[void]$sb.Append('<ApplicationPackage SchemaVersion="1.0" AutodeskProduct="Inventor" Name="Bimwright Inventor MCP"' + $nl)
[void]$sb.Append("                    Description=`"Bimwright MCP gateway add-ins for Autodesk Inventor`"" + $nl)
[void]$sb.Append("                    AppVersion=`"$Version`" ProductType=`"Application`" ProductCode=`"{B1MW0001-0000-0000-0000-000000000001}`">" + $nl)
[void]$sb.Append('  <CompanyDetails Name="Bimwright" Url="https://github.com/bimwright/ipt-mcp" />' + $nl)
[void]$sb.Append('  <RuntimeRequirements OS="Win64" Platform="Inventor" />' + $nl)
foreach ($e in ($packed | Sort-Object Year)) {
    $series = 'I' + ($e.Year - 1996)
    [void]$sb.Append(('  <Components Description="Inventor {0}">' -f $e.Year) + $nl)
    [void]$sb.Append(('    <RuntimeRequirements OS="Win64" Platform="Inventor" SeriesMin="{0}" SeriesMax="{0}" />' -f $series) + $nl)
    [void]$sb.Append(('    <ComponentEntry AppName="Bimwright Inventor MCP {0}" ModuleName="./Contents/{0}/{1}" />' -f $e.Year, $e.Addin) + $nl)
    [void]$sb.Append('  </Components>' + $nl)
}
[void]$sb.Append('</ApplicationPackage>' + $nl)
Set-Content -Path (Join-Path $bundleStage 'PackageContents.xml') -Value $sb.ToString() -Encoding UTF8

Copy-Item (Join-Path $RepoRoot 'scripts\install.ps1') (Join-Path $stageRoot 'install.ps1') -Force
Copy-Item (Join-Path $RepoRoot 'scripts\uninstall.ps1') (Join-Path $stageRoot 'uninstall.ps1') -Force

function Get-Rel([string]$Root, [string]$Path) {
    return $Path.Substring($Root.Length).TrimStart('\', '/') -replace '\\', '/'
}
function Get-Sha256Lower([string]$Path) {
    return ((Get-FileHash -Algorithm SHA256 -Path $Path).Hash).ToLowerInvariant()
}

$commit = ''
try { $commit = (& git -C $RepoRoot rev-parse HEAD).Trim() } catch { }

$files = @()
foreach ($f in @(Get-ChildItem $stageRoot -File -Recurse | Sort-Object FullName)) {
    $files += [ordered]@{ path = Get-Rel $stageRoot $f.FullName; sha256 = Get-Sha256Lower $f.FullName; bytes = $f.Length }
}

$years = @($packed | ForEach-Object { $_.Year })
$manifest = [ordered]@{
    name = 'IptMcp.Setup'
    version = $Version
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    commit = $commit
    platform = 'win-x64'
    packedInventorYears = $years
    supportedInventorYears = @(2022, 2023, 2024, 2025, 2026, 2027)
    server = [ordered]@{ command = 'server/ipt-mcp.exe'; selfContained = $true; requiresDotnet = $false }
    files = $files
}
$manifest | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $stageRoot 'manifest.json') -Encoding UTF8

$setupZip = Join-Path $OutputDir ("IptMcp.Setup-{0}-win-x64.zip" -f $displayVersion)
if (Test-Path $setupZip) { Remove-Item $setupZip -Force }
Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $setupZip -Force
Write-Host "Output : $setupZip"
Write-Host "Years  : $($years -join ', ')"
