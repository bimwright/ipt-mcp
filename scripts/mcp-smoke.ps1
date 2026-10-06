<#
.SYNOPSIS
  Minimal stdio MCP client for live smoke runs against Bimwright.Ipt.Server.exe.
  Sends initialize + a sequence of tools/call requests, prints each result, then tails the
  call journal (finish lines) so the evidence can be pasted into a review.

.EXAMPLE
  # F1 call-journal smoke (Inventor running with the add-in; add-in started with
  # BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1):
  pwsh -File .\scripts\mcp-smoke.ps1 -EnableSendCode

.EXAMPLE
  # Custom sequence against a freshly built exe in a temp dir. NOTE: `pwsh -File` cannot bind a
  # hashtable[] from the command line, so pass -ToolCalls via -Command (or dot-source the script):
  pwsh -NoProfile -Command '& .\scripts\mcp-smoke.ps1 -ServerExe "$env:TEMP\ipt_f1_out\Bimwright.Ipt.Server.exe" -ToolCalls @(@{ name = "inventor_list_available_targets"; arguments = @{} })'
#>
param(
    [string]$ServerExe = (Join-Path $PSScriptRoot '..\src\server\bin\Debug\net8.0\Bimwright.Ipt.Server.exe'),
    [string]$LogPath = (Join-Path $env:TEMP 'ipt-mcp-smoke-calls.jsonl'),
    [switch]$EnableSendCode,
    [int]$TimeoutMs = 60000,
    [hashtable[]]$ToolCalls = @(
        @{ name = 'inventor_get_current_target'; arguments = @{} },
        @{ name = 'inventor_get_document_info';  arguments = @{} },
        @{ name = 'inventor_send_code';          arguments = @{ code = 'var x = ;' } },
        @{ name = 'inventor_send_code';          arguments = @{ code = 'Console.WriteLine(app.ActiveDocument.DisplayName);' } }
    )
)

$ErrorActionPreference = 'Stop'
$ServerExe = (Resolve-Path $ServerExe).Path
if (Test-Path $LogPath) { Remove-Item $LogPath -Force }

$psi = [System.Diagnostics.ProcessStartInfo]::new($ServerExe)
if ($EnableSendCode) { $psi.ArgumentList.Add('--enable-send-code') }
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.StandardInputEncoding = [System.Text.UTF8Encoding]::new($false)
$psi.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
$psi.Environment['BIMWRIGHT_INVENTOR_CALL_LOG'] = $LogPath

$proc = [System.Diagnostics.Process]::Start($psi)
$stdin = $proc.StandardInput
$stdin.AutoFlush = $true
$stdout = $proc.StandardOutput

function Send-Request([int]$id, [string]$method, $params) {
    $msg = @{ jsonrpc = '2.0'; id = $id; method = $method; params = $params }
    $stdin.WriteLine(($msg | ConvertTo-Json -Depth 10 -Compress))
    while ($true) {
        $task = $stdout.ReadLineAsync()
        if (-not $task.Wait($TimeoutMs)) { throw "timeout waiting for response to id=$id ($method)" }
        $line = $task.Result
        if ($null -eq $line) { throw "server closed stdout before answering id=$id" }
        if ($line.Trim().Length -eq 0) { continue }
        $obj = $line | ConvertFrom-Json
        if ($obj.id -eq $id) { return $obj }
    }
}

try {
    $init = Send-Request 1 'initialize' @{
        protocolVersion = '2024-11-05'; capabilities = @{}
        clientInfo = @{ name = 'ipt-mcp-smoke'; version = '0.1' }
    }
    "server: $($init.result.serverInfo.name) $($init.result.serverInfo.version)"
    $stdin.WriteLine((@{ jsonrpc = '2.0'; method = 'notifications/initialized' } | ConvertTo-Json -Compress))

    $id = 2
    foreach ($call in $ToolCalls) {
        $resp = Send-Request $id 'tools/call' @{ name = $call.name; arguments = $call.arguments }
        $text = if ($resp.error) { "JSON-RPC error: $($resp.error | ConvertTo-Json -Compress)" }
                else { ($resp.result.content | ForEach-Object { $_.text }) -join "`n" }
        "--- [$id] $($call.name) ---"
        if ($text.Length -gt 600) { $text.Substring(0, 600) + " ...(+$($text.Length - 600) chars)" } else { $text }
        $id++
    }
}
finally {
    try { $stdin.Close() } catch {}
    if (-not $proc.WaitForExit(5000)) { $proc.Kill() }
}

""
"===== call journal finish lines ($LogPath) ====="
if (Test-Path $LogPath) {
    Get-Content $LogPath | Where-Object { $_ -match '"phase":"finish"' }
} else {
    "!! journal file was not created"
}
