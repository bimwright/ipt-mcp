#Requires -Version 5.1
# Upgrade a sandboxed installation from a real release package to a new one:
# real payloads, the real installer control flow and the real server smoke
# check (ipt-mcp.exe --help). Profile env vars (USERPROFILE, APPDATA,
# LOCALAPPDATA) are redirected to a sandbox under $Sandbox\profile for the whole
# run, so a function that ignores its fixture path cannot reach real user data.
[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [Parameter(Mandatory=$true)][string]$OldPackage,
    [Parameter(Mandatory=$true)][string]$NewPackage,
    [Parameter(Mandatory=$true)][string]$Sandbox,
    [Parameter(Mandatory=$true)][string]$ResultPath
)
$ErrorActionPreference='Stop'
$OldPackage=(Resolve-Path -LiteralPath $OldPackage).Path
$NewPackage=(Resolve-Path -LiteralPath $NewPackage).Path
$Sandbox=[IO.Path]::GetFullPath($Sandbox)
if (Test-Path -LiteralPath $Sandbox) { throw 'Use a new sandbox directory.' }
$sandboxUserProfile=Join-Path $Sandbox 'profile\userprofile'
$sandboxAppData=Join-Path $Sandbox 'profile\appdata'
$sandboxLocalAppData=Join-Path $Sandbox 'profile\localappdata'
New-Item -ItemType Directory -Path $sandboxUserProfile,$sandboxAppData,$sandboxLocalAppData -Force | Out-Null
$savedUserProfile=$env:USERPROFILE
$savedAppData=$env:APPDATA
$savedLocalAppData=$env:LOCALAPPDATA
$env:USERPROFILE=$sandboxUserProfile
$env:APPDATA=$sandboxAppData
$env:LOCALAPPDATA=$sandboxLocalAppData
if ($env:USERPROFILE -ne $sandboxUserProfile -or $env:APPDATA -ne $sandboxAppData -or $env:LOCALAPPDATA -ne $sandboxLocalAppData) {
    throw 'Profile env redirection failed'
}
$installer=Join-Path $NewPackage 'install.ps1'
$tokens=$null; $parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($installer,[ref]$tokens,[ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
foreach ($fn in $ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst]},$true)) {
    . ([scriptblock]::Create($fn.Extent.Text))
}
$start=$ast.EndBlock.Statements | Where-Object { $_.Extent.Text.StartsWith('$agentsGuide =') } | Select-Object -First 1
$main=[scriptblock]::Create((Get-Content $installer -Raw).Substring($start.Extent.StartOffset))

# Only these OS boundaries differ from a real user installation. The real
# Inventor process and the machine-wide add-in folders are never touched.
$bundleTarget=Join-Path $Sandbox 'plugins-user\Bimwright.Ipt.bundle'
function Get-UserBundleRoot { $bundleTarget }
function Get-UserScanRoots { @(Join-Path $Sandbox 'plugins-user') | Where-Object { Test-Path -LiteralPath $_ -PathType Container } }
function Get-MachineScanRoots { @(Join-Path $Sandbox 'machine') | Where-Object { Test-Path -LiteralPath $_ -PathType Container } }
function Get-Process { param($Name,$ErrorAction) } # Isolated host has no Inventor process.
try {
$oldManifest=Get-Content (Join-Path $OldPackage 'manifest.json') -Raw | ConvertFrom-Json
Assert-SetupManifest -Root $OldPackage -Manifest $oldManifest
# Releases before the fixed-path installer placed the server in a versioned folder.
$oldServer=Join-Path $Sandbox "server/$($oldManifest.version)"
New-Item -ItemType Directory -Path (Split-Path -Parent $oldServer) | Out-Null
Copy-Item -LiteralPath (Join-Path $OldPackage 'server') -Destination $oldServer -Recurse
Copy-Item -LiteralPath (Join-Path $OldPackage 'bundle') -Destination $bundleTarget -Recurse
$oldServerHash=(Get-FileHash (Join-Path $oldServer 'ipt-mcp.exe')).Hash

$SourceDir=$NewPackage; $bundleSourceDir=Join-Path $NewPackage 'bundle'; $serverSourceDir=Join-Path $NewPackage 'server'
$manifest=Get-Content (Join-Path $NewPackage 'manifest.json') -Raw | ConvertFrom-Json
$setupVersion=[string]$manifest.version
$ServerInstallRoot=Join-Path $Sandbox 'server\current'
$Uninstall=$false; $PruneOldServers=$false
& $main

# Every installed bundle file must match the new package byte for byte.
$sourceFull=[IO.Path]::GetFullPath((Join-Path $NewPackage 'bundle')).TrimEnd('\')+'\'
$files=@(Get-ChildItem (Join-Path $NewPackage 'bundle') -File -Recurse)
foreach ($file in $files) {
    $relative=$file.FullName.Substring($sourceFull.Length)
    $target=Join-Path $bundleTarget $relative
    if ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw "Installed payload differs: $target" }
}
if ((Get-FileHash (Join-Path $oldServer 'ipt-mcp.exe')).Hash -ne $oldServerHash) { throw 'Legacy server version changed' }
if ((Get-FileHash (Join-Path $ServerInstallRoot 'ipt-mcp.exe')).Hash -ne (Get-FileHash (Join-Path $NewPackage 'server/ipt-mcp.exe')).Hash) { throw 'New server mismatch' }
$report=[ordered]@{testedAtUtc=(Get-Date).ToUniversalTime().ToString('o');fromVersion=$oldManifest.version;toVersion=$manifest.version;
    installerSha256=(Get-FileHash $installer).Hash;isolation='Real payloads, real installer control flow and real server smoke check (--help); sandbox paths and simulated closed host; no real deployment.';
    bundleFilesVerified=$files.Count;serverVerified=$true;legacyServerKept=$true;smokeCheck='passed';passed=$true}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $ResultPath -Encoding UTF8
$report | ConvertTo-Json -Depth 10
} finally {
    $env:USERPROFILE=$savedUserProfile
    $env:APPDATA=$savedAppData
    $env:LOCALAPPDATA=$savedLocalAppData
}
