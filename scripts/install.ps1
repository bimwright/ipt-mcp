#Requires -Version 5.1
<#
.SYNOPSIS
    Install or uninstall ipt-mcp from a client setup ZIP.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$bundleName = 'Bimwright.Ipt.bundle'
$targetRoot = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\$bundleName"

$setupVersion = 'dev'
if (Test-Path (Join-Path $PSScriptRoot 'manifest.json')) {
    $setupVersion = ((Get-Content -Raw (Join-Path $PSScriptRoot 'manifest.json')) | ConvertFrom-Json).version
}
$serverInstallRoot = Join-Path $env:LOCALAPPDATA "Bimwright\ipt-mcp\server\$setupVersion"

if ($Uninstall) {
    if ($PSCmdlet.ShouldProcess($targetRoot, 'Remove Inventor bundle')) {
        if (Test-Path $targetRoot) { Remove-Item $targetRoot -Recurse -Force }
        Write-Host "Removed plugin bundle (if present): $targetRoot"
    }
    $serverParent = Join-Path $env:LOCALAPPDATA 'Bimwright\ipt-mcp\server'
    if ($PSCmdlet.ShouldProcess($serverParent, 'Remove installed servers')) {
        if (Test-Path $serverParent) { Remove-Item $serverParent -Recurse -Force }
        Write-Host "Removed server installs (if present): $serverParent"
    }
    return
}

if (-not (Test-Path (Join-Path $PSScriptRoot 'bundle'))) {
    Write-Error 'This install.ps1 expects a client setup ZIP (bundle/ + server/). For a local SDK build use scripts/package-bundle.ps1.'
    return
}

if ($PSCmdlet.ShouldProcess($targetRoot, 'Install ipt-mcp plugin bundle')) {
    if (Test-Path $targetRoot) { Remove-Item $targetRoot -Recurse -Force }
    Copy-Item (Join-Path $PSScriptRoot 'bundle') $targetRoot -Recurse -Force
    Write-Host "Installed plugin bundle: $targetRoot"
}

$exeSrc = Join-Path $PSScriptRoot 'server\ipt-mcp.exe'
if (Test-Path $exeSrc) {
    if ($PSCmdlet.ShouldProcess($serverInstallRoot, 'Install ipt-mcp.exe')) {
        New-Item -ItemType Directory -Path $serverInstallRoot -Force | Out-Null
        Copy-Item (Join-Path $PSScriptRoot 'server\*') $serverInstallRoot -Force
        $exe = Join-Path $serverInstallRoot 'ipt-mcp.exe'
        Write-Host "Installed server: $exe"
        Write-Host "MCP command: $exe"
    }
}

Write-Host 'Restart Inventor to load the add-in. Packed years are listed in manifest.json.'
