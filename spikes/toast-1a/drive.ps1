# PROTOTYPE — throwaway toast compatibility spike driver (roadmap Phase 1a).
# Builds + deploys the spike add-in, restarts Inventor 2027, runs every probe, closes Inventor,
# removes the spike add-in. Evidence: %LOCALAPPDATA%\Bimwright\ipt-mcp\toast-spike\
param(
    [int]$Alc = 0,              # UseInventorAssemblyContext value written into the .addin
    [switch]$Quick,             # env + one toast + ribbon only (used for the ALC=1 pass)
    [string]$Tag = "pass1",
    [switch]$KeepTrust,         # keep the spike's AddInLoadRules entry (for a follow-up pass)
    [switch]$ThemeSurvey,       # toast palettes vs Inventor Light/Dark theme (switches theme, restores it)
    [switch]$BackdropSurvey     # palette chosen from the colour behind the toast (scheme / pixels / theme)
)
$ErrorActionPreference = 'Stop'
$here    = $PSScriptRoot
$root    = Join-Path $env:LOCALAPPDATA 'Bimwright\ipt-mcp\toast-spike'
$inbox   = Join-Path $root 'inbox'
$outbox  = Join-Path $root 'outbox'
$addins  = Join-Path $env:APPDATA 'Autodesk\Inventor 2027\Addins'
$deploy  = Join-Path $addins 'Bimwright.Ipt.ToastSpike'
$manifest = Join-Path $addins 'Bimwright.Ipt.ToastSpike.addin'
$invExe  = 'C:\Program Files\Autodesk\Inventor 2027\Bin\Inventor.exe'
$script:seq = 0

function Say($m) { Write-Host ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $m) }

function Close-Inventor {
    $p = Get-Process Inventor -ErrorAction SilentlyContinue
    if (-not $p) { Say 'Inventor not running'; return }
    $ready = Join-Path $root 'ready.json'
    if ((Test-Path $ready) -and ((Get-Content $ready -Raw | ConvertFrom-Json).pid -eq $p.Id)) {
        Say "closing Inventor pid $($p.Id) via spike 'quit' probe (SilentOperation + Quit, runs Deactivate)"
        P quit | Out-Null
    } else {
        Say "closing Inventor pid $($p.Id): spike not loaded -> Stop-Process"
        Stop-Process -Id $p.Id -Force
    }
    if (-not $p.WaitForExit(120000)) { Say '  still running after 120 s -> Stop-Process'; Stop-Process -Id $p.Id -Force; $p.WaitForExit(30000) | Out-Null }
    Say '  Inventor exited'
}

# Inventor blocks unsigned add-ins ("Add-in Manager Security Alert") and records the decision in
# %APPDATA%\Autodesk\Inventor 2027\Addins\AddInLoadRules as {zId&<GUID>} + 4-byte flag (0 = allowed,
# 1 = blocked). Unblock ONLY the spike GUID for the run (same as the Add-in Manager "unblock"), and remove
# the entry afterwards so the machine's trust state returns to what it was.
$rules = Join-Path $addins 'AddInLoadRules'
$spikeGuidBytes = [Text.Encoding]::Unicode.GetBytes('{5B1D7A0E-3C2F-4E8A-9D61-7A2C0F1E5B11}')
function Find-Bytes([byte[]]$hay, [byte[]]$needle) {
    for ($i = 0; $i -le $hay.Length - $needle.Length; $i++) {
        $ok = $true
        for ($j = 0; $j -lt $needle.Length; $j++) { if ($hay[$i + $j] -ne $needle[$j]) { $ok = $false; break } }
        if ($ok) { return $i }
    }
    return -1
}
function Set-SpikeTrust([bool]$allowed) {
    $b = [IO.File]::ReadAllBytes($rules)
    $i = Find-Bytes $b $spikeGuidBytes
    if ($i -lt 0) {
        # append an entry in the same layout Inventor writes: '{z' + u64 2 + "Id" + u64 38 + GUID (UTF-16) + u32 flag + '}|'
        $ms = New-Object IO.MemoryStream
        $ms.Write($b, 0, $b.Length)
        $w = New-Object IO.BinaryWriter($ms)
        $w.Write([byte[]](0x7B, 0x7A)); $w.Write([UInt64]2); $w.Write([Text.Encoding]::Unicode.GetBytes('Id'))
        $w.Write([UInt64]38); $w.Write($spikeGuidBytes); $w.Write([UInt32]($(if ($allowed) { 0 } else { 1 }))); $w.Write([byte[]](0x7D, 0x7C))
        $w.Flush()
        [IO.File]::WriteAllBytes($rules, $ms.ToArray())
        Say "AddInLoadRules: spike entry appended (allowed=$allowed, $($b.Length) -> $($ms.Length) bytes)"
        return
    }
    $flag = $i + $spikeGuidBytes.Length
    $b[$flag] = if ($allowed) { 0 } else { 1 }
    [IO.File]::WriteAllBytes($rules, $b)
    Say "AddInLoadRules: spike entry flag -> $($b[$flag])"
}
function Remove-SpikeTrustEntry {
    $b = [IO.File]::ReadAllBytes($rules)
    $i = Find-Bytes $b $spikeGuidBytes
    if ($i -lt 0) { return }
    # entry = '{z' + 8-byte len + 'Id' ... GUID + 4-byte flag + '}|'; find the '{z' that starts it
    $start = $i; while ($start -gt 0 -and -not ($b[$start] -eq 0x7B -and $b[$start + 1] -eq 0x7A)) { $start-- }
    $end = $i + $spikeGuidBytes.Length + 4 + 2
    $out = New-Object byte[] ($b.Length - ($end - $start))
    [Array]::Copy($b, 0, $out, 0, $start)
    [Array]::Copy($b, $end, $out, $start, $b.Length - $end)
    [IO.File]::WriteAllBytes($rules, $out)
    Say "AddInLoadRules: spike entry removed ($($b.Length) -> $($out.Length) bytes)"
}

function Deploy {
    $bin = Join-Path $here 'bin\Release\net10.0-windows7.0'
    if (Test-Path $deploy) { Remove-Item $deploy -Recurse -Force }
    New-Item -ItemType Directory $deploy | Out-Null
    Copy-Item "$bin\*" $deploy -Recurse
    @"
<?xml version="1.0" encoding="utf-8"?>
<!-- PROTOTYPE — throwaway toast spike add-in (roadmap Phase 1a). Safe to delete. -->
<Addin Type="Standard">
  <ClassId>{5B1D7A0E-3C2F-4E8A-9D61-7A2C0F1E5B11}</ClassId>
  <ClientId>{5B1D7A0E-3C2F-4E8A-9D61-7A2C0F1E5B11}</ClientId>
  <DisplayName>Bimwright Toast Spike (PROTOTYPE)</DisplayName>
  <Description>Throwaway toast compatibility spike.</Description>
  <Assembly>$deploy\Bimwright.Ipt.ToastSpike.dll</Assembly>
  <LoadOnStartUp>1</LoadOnStartUp>
  <LoadAutomatically>1</LoadAutomatically>
  <UserUnloadable>1</UserUnloadable>
  <Hidden>0</Hidden>
  <SupportedSoftwareVersionGreaterThan>30..</SupportedSoftwareVersionGreaterThan>
  <SupportedSoftwareVersionLessThan>32..</SupportedSoftwareVersionLessThan>
  <UseInventorAssemblyContext>$Alc</UseInventorAssemblyContext>
</Addin>
"@ | Set-Content $manifest -Encoding utf8
    Say "deployed spike add-in (UseInventorAssemblyContext=$Alc)"
}

function Undeploy {
    Remove-Item $manifest -Force -ErrorAction SilentlyContinue
    Remove-Item $deploy -Recurse -Force -ErrorAction SilentlyContinue
    Say 'spike add-in removed from Addins'
}

function Start-Inventor {
    Remove-Item (Join-Path $root 'ready.json') -ErrorAction SilentlyContinue
    Say 'starting Inventor 2027'
    Start-Process -FilePath $invExe | Out-Null
    $deadline = (Get-Date).AddSeconds(420)
    while (-not (Test-Path (Join-Path $root 'ready.json'))) {
        if ((Get-Date) -gt $deadline) { throw 'spike add-in never reported ready (7 min)' }
        Start-Sleep -Seconds 2
    }
    Say "spike ready: $(Get-Content (Join-Path $root 'ready.json'))"
    Start-Sleep -Seconds 15   # let the UI settle (My Home, ribbon)
}

function Wait-Out([string]$id, [int]$timeout = 90) {
    $f = Join-Path $outbox "$id.json"
    $deadline = (Get-Date).AddSeconds($timeout)
    while (-not (Test-Path $f)) {
        if ((Get-Date) -gt $deadline) { Say "  !! timeout waiting for $id"; return $null }
        Start-Sleep -Milliseconds 200
    }
    Start-Sleep -Milliseconds 50
    return Get-Content $f -Raw | ConvertFrom-Json
}

function P([string]$probe, [hashtable]$a = @{}, [int]$timeout = 90) {
    $id = '{0}-{1:D3}-{2}' -f $Tag, $script:seq++, $probe
    $json = @{ id = $id; probe = $probe; args = $a } | ConvertTo-Json -Depth 6 -Compress
    Set-Content (Join-Path $inbox "$id.tmp") $json -Encoding utf8
    Move-Item (Join-Path $inbox "$id.tmp") (Join-Path $inbox "$id.json")
    $r = Wait-Out $id $timeout
    $short = if ($r) { ($r.result | ConvertTo-Json -Depth 3 -Compress) } else { 'NO RESULT' }
    if ($short.Length -gt 220) { $short = $short.Substring(0, 220) + '…' }
    Say "$id ($($r.ms) ms): $short"
    return @{ id = $id; r = $r }
}

# ---------------------------------------------------------------- run
Close-Inventor
Deploy
Set-SpikeTrust $true
foreach ($d in @($inbox, $outbox)) { if (Test-Path $d) { Get-ChildItem $d | Remove-Item -Force } }
Start-Inventor

P env | Out-Null
P ribbon_state @{ exercise = $true } | Out-Null
P ribbon_activate @{ ribbon = 'ZeroDoc' } | Out-Null
Start-Sleep 2
P snap @{ what = 'main_top'; name = "$Tag-ribbon-zerodoc" } | Out-Null

if ($BackdropSurvey) {
    P theme_info | Out-Null
    P part_sketch | Out-Null
    P exit_sketch | Out-Null
    P scheme_info | Out-Null
    P host @{ mode = 'B' } | Out-Null
    $body = 'inventor_extrude · 124 ms · Part1.ipt'
    P show @{ mode = 'B'; owned = $false; anchor = 'view'; palette = 'auto-scheme';  title = 'auto-scheme (colour scheme)'; body = $body } | Out-Null
    P show @{ mode = 'B'; owned = $false; anchor = 'view'; palette = 'auto-sample';  title = 'auto-sample (screen pixels)'; body = $body } | Out-Null
    P show @{ mode = 'B'; owned = $false; anchor = 'view'; palette = 'auto-inverse'; title = 'auto-inverse (UI theme)'; body = $body } | Out-Null
    Start-Sleep 2
    P snap @{ what = 'main'; name = "$Tag-dark" } | Out-Null
    P theme_set @{ name = 'other' } 120 | Out-Null
    Start-Sleep 6
    P theme_info | Out-Null
    P scheme_info | Out-Null
    P snap @{ what = 'main'; name = "$Tag-light-after-switch" } | Out-Null
    P close | Out-Null
    P show @{ mode = 'B'; owned = $false; anchor = 'view'; palette = 'auto-scheme';  title = 'auto-scheme (colour scheme)'; body = $body } | Out-Null
    P show @{ mode = 'B'; owned = $false; anchor = 'view'; palette = 'auto-sample';  title = 'auto-sample (screen pixels)'; body = $body } | Out-Null
    P show @{ mode = 'B'; owned = $false; anchor = 'view'; palette = 'auto-inverse'; title = 'auto-inverse (UI theme)'; body = $body } | Out-Null
    P show @{ mode = 'B'; owned = $false; anchor = 'main'; palette = 'auto-scheme';  title = 'auto-scheme, main-frame anchor'; body = $body } | Out-Null
    Start-Sleep 2
    P snap @{ what = 'main'; name = "$Tag-light-fresh" } | Out-Null
    P theme_set @{ name = 'original' } 120 | Out-Null
    Start-Sleep 6
    P theme_info | Out-Null
    P snap @{ what = 'main'; name = "$Tag-restored" } | Out-Null
    P close | Out-Null
    P close_doc | Out-Null
    P show @{ mode = 'B'; owned = $false; anchor = 'view'; palette = 'auto-scheme'; title = 'auto-scheme on Home (no view)'; body = $body } | Out-Null
    Start-Sleep 2
    P snap @{ what = 'main'; name = "$Tag-home" } | Out-Null
    P close | Out-Null
} elseif ($ThemeSurvey) {
    $palettes = @('dark', 'dark-elevated', 'light', 'light-elevated', 'auto-inverse')
    P theme_info | Out-Null
    P part_sketch | Out-Null
    P exit_sketch | Out-Null
    P host @{ mode = 'B' } | Out-Null
    foreach ($p in $palettes) { P show @{ mode = 'B'; owned = $false; anchor = 'main'; palette = $p; title = "palette: $p"; body = 'inventor_extrude · 124 ms · Part1.ipt' } | Out-Null }
    Start-Sleep 2
    P snap @{ what = 'main'; name = "$Tag-theme1-5palettes" } | Out-Null
    P theme_set @{ name = 'other' } 120 | Out-Null
    Start-Sleep 6
    P theme_info | Out-Null
    P snap @{ what = 'main'; name = "$Tag-theme2-after-switch" } | Out-Null
    P close | Out-Null
    foreach ($p in $palettes) { P show @{ mode = 'B'; owned = $false; anchor = 'main'; palette = $p; title = "palette: $p"; body = 'inventor_extrude · 124 ms · Part1.ipt' } | Out-Null }
    Start-Sleep 2
    P snap @{ what = 'main'; name = "$Tag-theme2-5palettes" } | Out-Null
    P theme_set @{ name = 'original' } 120 | Out-Null
    Start-Sleep 6
    P theme_info | Out-Null
    P snap @{ what = 'main'; name = "$Tag-theme-restored" } | Out-Null
    P close | Out-Null
    P close_doc | Out-Null
} elseif ($Quick) {
    P host @{ mode = 'B' } | Out-Null
    P show @{ mode = 'B'; owned = $true; anchor = 'main'; body = "ALC=$Alc owned" } | Out-Null
    P show @{ mode = 'B'; owned = $false; anchor = 'main'; body = "ALC=$Alc unowned" } | Out-Null
    P host @{ mode = 'A' } | Out-Null
    P show @{ mode = 'A'; owned = $false; anchor = 'main'; body = "ALC=$Alc A unowned" } | Out-Null
    Start-Sleep 2
    P state | Out-Null
    P snap @{ name = "$Tag-toast" } | Out-Null
    P env | Out-Null
    P close | Out-Null
} else {
    # Q1 — hosts (B first so env for A is measured after a dedicated thread exists)
    P host @{ mode = 'B' } | Out-Null
    P host @{ mode = 'A' } | Out-Null
    P env | Out-Null

    # Q2/Q3 — baseline STA latency, then each rendering mode under load
    $b = P sta_ping @{ n = 40; every = 100; label = 'baseline-no-toasts' }; Wait-Out "$($b.id)-ping" | Out-Null
    foreach ($cfg in @(@{m='A';o=$true;tag='A'}, @{m='B';o=$true;tag='B-owned'}, @{m='B';o=$false;tag='B-unowned'})) {
        1..4 | ForEach-Object { P show @{ mode = $cfg.m; owned = $cfg.o; anchor = 'main'; animate = $true; title = "$($cfg.tag) #$_" } | Out-Null }
        Start-Sleep 2
        P snap @{ name = "$Tag-$($cfg.tag)-4toasts" } | Out-Null
        $p = P sta_ping @{ n = 40; every = 100; label = "$($cfg.tag)-4-animated" }; Wait-Out "$($p.id)-ping" | Out-Null
        $blk = P block @{ ms = 8000; mode = $cfg.m; newToastAtMs = 3000; newOwned = $cfg.o; snapAtMs = 5000 } 60
        Wait-Out "$($blk.id)-watch" 60 | Out-Null
        P state | Out-Null
        P close | Out-Null
    }

    # Q3 — owner semantics: minimize / restore
    foreach ($o in @($true, $false)) {
        P show @{ mode = 'B'; owned = $o; anchor = 'main'; title = "owned=$o" } | Out-Null
        Start-Sleep 1
        P minimize | Out-Null; Start-Sleep 2; P state | Out-Null
        P restore | Out-Null;  Start-Sleep 2; P state | Out-Null
        P close | Out-Null
    }

    # Q4 — DPI / monitors, both modes
    foreach ($m in @('B', 'A')) {
        P dpi_toasts @{ mode = $m; owned = $false } | Out-Null
        Start-Sleep 2
        P state | Out-Null
        P snap @{ what = 'virtual'; name = "$Tag-dpi-$m-virtual" } | Out-Null
        P close | Out-Null
    }

    # Q6 — focus: active sketch, in-canvas command, modal dialog
    P try_foreground | Out-Null
    P part_sketch | Out-Null
    Start-Sleep 2
    P fg | Out-Null
    P ribbon_activate @{ ribbon = 'Part' } | Out-Null
    Start-Sleep 1
    P snap @{ what = 'main_top'; name = "$Tag-ribbon-part" } | Out-Null
    foreach ($s in @(@{m='B';n=$true}, @{m='B';n=$false}, @{m='A';n=$true}, @{m='A';n=$false})) {
        P show @{ mode = $s.m; owned = $true; noActivate = $s.n; anchor = 'main'; title = "sketch $($s.m) noAct=$($s.n)" } | Out-Null
        Start-Sleep 1
        P fg | Out-Null
    }
    P close | Out-Null
    P exit_sketch | Out-Null
    P cmd_start @{ name = 'PartExtrudeCmd' } | Out-Null
    Start-Sleep 3
    P fg | Out-Null
    foreach ($n in @($true, $false)) {
        P show @{ mode = 'B'; owned = $true; noActivate = $n; anchor = 'main'; title = "extrude noAct=$n" } | Out-Null
        Start-Sleep 1
        P fg | Out-Null
    }
    P snap @{ what = 'main'; name = "$Tag-extrude-with-toasts" } | Out-Null
    P close | Out-Null
    P cmd_stop | Out-Null
    P cmd_start @{ name = 'AppApplicationOptionsCmd' } | Out-Null
    Start-Sleep 4
    P sta_windows | Out-Null
    foreach ($n in @($true, $false)) {
        P show @{ mode = 'B'; owned = $true; noActivate = $n; anchor = 'main'; title = "modal noAct=$n" } | Out-Null
        Start-Sleep 1
        P fg | Out-Null
    }
    P snap @{ what = 'virtual'; name = "$Tag-modal-with-toasts" } | Out-Null
    P close | Out-Null
    P modal_close | Out-Null
    Start-Sleep 2
    P fg | Out-Null
    P close_doc | Out-Null
    P ribbon_state @{ exercise = $false } | Out-Null
    P env | Out-Null
}

Close-Inventor
Undeploy
if (-not $KeepTrust) { Remove-SpikeTrustEntry }
Say "done — evidence in $root"
