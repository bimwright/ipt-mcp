#Requires -Version 5.1
# No Pester dependency. Execute production functions against isolated temporary files.
# Profile env vars (USERPROFILE, APPDATA, LOCALAPPDATA) are redirected to a
# sandbox under the test root for the whole run, so a function that ignores its
# fixture path cannot reach real user data.
[CmdletBinding()]
param([string]$ResultPath, [string]$TestRootParent = [IO.Path]::GetTempPath())
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path $TestRootParent ('ipt-installer-tests-' + [guid]::NewGuid().ToString('N'))
$sandboxUserProfile = Join-Path $testRoot 'profile\userprofile'
$sandboxAppData = Join-Path $testRoot 'profile\appdata'
$sandboxLocalAppData = Join-Path $testRoot 'profile\localappdata'
New-Item -ItemType Directory -Path $sandboxUserProfile, $sandboxAppData, $sandboxLocalAppData -Force | Out-Null
$savedUserProfile = $env:USERPROFILE
$savedAppData = $env:APPDATA
$savedLocalAppData = $env:LOCALAPPDATA
$env:USERPROFILE = $sandboxUserProfile
$env:APPDATA = $sandboxAppData
$env:LOCALAPPDATA = $sandboxLocalAppData
if ($env:USERPROFILE -ne $sandboxUserProfile -or $env:APPDATA -ne $sandboxAppData -or $env:LOCALAPPDATA -ne $sandboxLocalAppData) {
    throw 'Profile env redirection failed'
}
$installer = Join-Path $PSScriptRoot '../../scripts/install.ps1'
$tokens = $null; $parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path $installer), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
foreach ($fn in $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)) {
    . ([scriptblock]::Create($fn.Extent.Text))
}
$results = New-Object System.Collections.Generic.List[object]
function Assert($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Test([string]$Name, [scriptblock]$Body) {
    try { & $Body; $results.Add([pscustomobject]@{name=$Name;passed=$true}); Write-Host "PASS $Name" }
    catch { $results.Add([pscustomobject]@{name=$Name;passed=$false;error=$_.Exception.Message}); Write-Host "FAIL $Name : $_" }
}
function Assert-Throws([scriptblock]$Body, [string]$Pattern) {
    $message = $null
    try { & $Body | Out-Null } catch { $message = $_.Exception.Message }
    Assert ($null -ne $message -and $message -match $Pattern) "Expected error matching '$Pattern', got '$message'"
}
$mainStatement = $ast.EndBlock.Statements | Where-Object { $_.Extent.Text.StartsWith('$agentsGuide =') } | Select-Object -First 1
$mainBody = [scriptblock]::Create((Get-Content -LiteralPath $installer -Raw).Substring($mainStatement.Extent.StartOffset))
function New-AddinXml([int]$Year, [string]$Marker, [string]$Assembly = "Bimwright.Ipt.Plugin.Inv$($Year - 2000).dll", [string]$Id) {
    if (-not $Id) { $Id = (Get-IptMcpClientIds)[$Year - 2022] }
    return @"
<?xml version="1.0" encoding="utf-8"?>
<Addin Type="Standard">
  <ClassId>{$Id}</ClassId>
  <ClientId>{$Id}</ClientId>
  <DisplayName>$Marker</DisplayName>
  <Assembly>$Assembly</Assembly>
</Addin>
"@
}
function New-PackageContentsXml {
    return '<?xml version="1.0" encoding="utf-8"?><ApplicationPackage SchemaVersion="1.0" AutodeskProduct="Inventor" Name="Bimwright Inventor MCP" AppVersion="0.2.0" ProductType="Application" ProductCode="{B1MW0001-0000-0000-0000-000000000001}"><RuntimeRequirements OS="Win64" Platform="Inventor" /></ApplicationPackage>'
}
function New-SetupFixture([string]$Parent = $testRoot) {
    $root = Join-Path $Parent ([guid]::NewGuid().ToString('N'))
    $source = Join-Path $root 'source'
    New-Item -ItemType Directory -Path "$source/bundle/Contents/2025", "$source/bundle/Contents/2027", "$source/server",
        "$root/server/current", "$root/server/0.1.0", "$root/server/dev",
        "$root/plugins-user/Bimwright.Ipt.bundle/Contents/2025", "$root/plugins-user/Bimwright.Ipt.bundle/Contents/2027",
        "$root/machine/ApplicationPlugins", "$root/machine/InventorAddins", "$root/user-addins" -Force | Out-Null
    Set-Content -LiteralPath "$source/server/ipt-mcp.exe" 'new-server'
    Set-Content -LiteralPath "$source/bundle/PackageContents.xml" (New-PackageContentsXml)
    Set-Content -LiteralPath "$root/server/current/ipt-mcp.exe" 'old-server-current'
    Set-Content -LiteralPath "$root/server/0.1.0/ipt-mcp.exe" 'old-server-010'
    Set-Content -LiteralPath "$root/server/dev/ipt-mcp.exe" 'dev-build'
    $installed = Join-Path $root 'plugins-user/Bimwright.Ipt.bundle'
    Set-Content -LiteralPath "$installed/PackageContents.xml" (New-PackageContentsXml)
    foreach ($year in @(2025,2027)) {
        $nn = '{0:00}' -f ($year - 2000)
        Set-Content -LiteralPath "$source/bundle/Contents/$year/Bimwright.Ipt.Plugin.Inv$nn.dll" "new-plugin-$year"
        Set-Content -LiteralPath "$source/bundle/Contents/$year/Bimwright.Ipt.Inv$nn.addin" (New-AddinXml $year "new-addin-$year")
        Set-Content -LiteralPath "$installed/Contents/$year/Bimwright.Ipt.Plugin.Inv$nn.dll" "old-plugin-$year"
        Set-Content -LiteralPath "$installed/Contents/$year/Bimwright.Ipt.Inv$nn.addin" (New-AddinXml $year "old-addin-$year")
    }
    Set-Content -LiteralPath "$installed/Contents/2025/old-only.dll" 'old dependency'
    $manifest = [pscustomobject]@{files=@(Get-ChildItem -LiteralPath $source -File -Recurse | ForEach-Object {
        [pscustomobject]@{path=$_.FullName.Substring($source.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
    })}
    return [pscustomobject]@{Root=$root;Source=$source;Running=$false;SmokeFails=$false;Manifest=$manifest}
}
function Invoke-FixtureSetup {
    [CmdletBinding(SupportsShouldProcess=$true)]
    param($Fixture, [switch]$PruneOldServers, [switch]$Uninstall)
    # Only OS boundaries are redirected. Main control flow, staging,
    # directory replacement and rollback are production code.
    function Get-UserBundleRoot { Join-Path $Fixture.Root 'plugins-user\Bimwright.Ipt.bundle' }
    function Get-UserScanRoots { @((Join-Path $Fixture.Root 'plugins-user'), (Join-Path $Fixture.Root 'user-addins')) | Where-Object { Test-Path -LiteralPath $_ -PathType Container } }
    function Get-MachineScanRoots { @((Join-Path $Fixture.Root 'machine\ApplicationPlugins'), (Join-Path $Fixture.Root 'machine\InventorAddins')) | Where-Object { Test-Path -LiteralPath $_ -PathType Container } }
    function Get-Process { param($Name, $ErrorAction) if ($Fixture.Running) { [pscustomobject]@{Name='Inventor';Id=1234} } }
    function Test-ServerExecutable { param([string]$Path, [int]$TimeoutSeconds, [string]$Arguments) if ($Fixture.SmokeFails) { throw 'Server executable could not start (stub). Antivirus or policy may have blocked it.' } }
    $SourceDir=$Fixture.Source; $bundleSourceDir=Join-Path $SourceDir 'bundle'; $serverSourceDir=Join-Path $SourceDir 'server'
    $ServerInstallRoot=Join-Path $Fixture.Root 'server\current'; $manifest=$Fixture.Manifest; $setupVersion='0.2.0'
    & $mainBody
}
function Assert-OldInstall($Fixture) {
    $installed = "$($Fixture.Root)/plugins-user/Bimwright.Ipt.bundle"
    foreach ($year in @(2025,2027)) {
        $nn = '{0:00}' -f ($year - 2000)
        Assert ((Get-Content -LiteralPath "$installed/Contents/$year/Bimwright.Ipt.Plugin.Inv$nn.dll" -Raw).Trim() -eq "old-plugin-$year") "Old $year plugin not restored"
        Assert ((Get-Content -LiteralPath "$installed/Contents/$year/Bimwright.Ipt.Inv$nn.addin" -Raw).Contains("old-addin-$year")) "Old $year manifest not restored"
    }
    Assert (Test-Path -LiteralPath "$installed/Contents/2025/old-only.dll") 'Old dependency not restored'
    Assert ((Get-Content -LiteralPath "$($Fixture.Root)/server/0.1.0/ipt-mcp.exe" -Raw).Trim() -eq 'old-server-010') 'Legacy server version changed'
    Assert ((Get-Content -LiteralPath "$($Fixture.Root)/server/current/ipt-mcp.exe" -Raw).Trim() -eq 'old-server-current') 'Previous current server not restored'
}
try {
    Test 'Upgrade replaces bundle and current server, keeps legacy versions, reports next steps' {
        $fixture = New-SetupFixture
        $output = Invoke-FixtureSetup $fixture 3>&1 6>&1 | Out-String -Width 4096
        $installed = "$($fixture.Root)/plugins-user/Bimwright.Ipt.bundle"
        foreach ($year in @(2025,2027)) {
            $nn = '{0:00}' -f ($year - 2000)
            Assert ((Get-Content -LiteralPath "$installed/Contents/$year/Bimwright.Ipt.Plugin.Inv$nn.dll" -Raw).Trim() -eq "new-plugin-$year") 'Plugin not upgraded'
            Assert ((Get-Content -LiteralPath "$installed/Contents/$year/Bimwright.Ipt.Inv$nn.addin" -Raw).Contains("new-addin-$year")) 'Manifest not upgraded'
        }
        Assert (-not (Test-Path -LiteralPath "$installed/Contents/2025/old-only.dll")) 'Stale dependency survived upgrade'
        Assert ((Get-Content -LiteralPath "$($fixture.Root)/server/current/ipt-mcp.exe" -Raw).Trim() -eq 'new-server') 'Server not upgraded'
        Assert ((Get-Content -LiteralPath "$($fixture.Root)/server/0.1.0/ipt-mcp.exe" -Raw).Trim() -eq 'old-server-010') 'Legacy version removed without -PruneOldServers'
        Assert ((Get-Content -LiteralPath "$($fixture.Root)/server/dev/ipt-mcp.exe" -Raw).Trim() -eq 'dev-build') 'dev copy touched'
        Assert ($output -match 'Server\s*:.*current\\ipt-mcp\.exe') 'Server path missing from summary'
        Assert ($output -match 'Legacy\s*:\s*0\.1\.0') 'Legacy version not reported'
        Assert ($output -match 'PruneOldServers') 'Prune guidance missing'
        Assert ($output -match 'Verified:\s*bundle') 'Verification not reported'
        Assert (@(Get-ChildItem -LiteralPath $fixture.Root -Recurse -Filter '*.iptmcp-rollback-*').Count -eq 0) 'Transaction backups leaked after success'
    }
    Test 'Install works when paths contain spaces' {
        $parent = Join-Path $testRoot 'with space'
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
        $fixture = New-SetupFixture $parent
        Invoke-FixtureSetup $fixture | Out-Null
        Assert ((Get-Content -LiteralPath "$($fixture.Root)/server/current/ipt-mcp.exe" -Raw).Trim() -eq 'new-server') 'Server not installed under a path with spaces'
        Assert ((Get-Content -LiteralPath "$($fixture.Root)/plugins-user/Bimwright.Ipt.bundle/Contents/2027/Bimwright.Ipt.Plugin.Inv27.dll" -Raw).Trim() -eq 'new-plugin-2027') 'Bundle not installed under a path with spaces'
    }
    Test 'PruneOldServers removes older versions and keeps dev' {
        $fixture = New-SetupFixture
        Invoke-FixtureSetup $fixture -PruneOldServers | Out-Null
        Assert (-not (Test-Path -LiteralPath "$($fixture.Root)/server/0.1.0")) 'Previous version not pruned'
        Assert ((Get-Content -LiteralPath "$($fixture.Root)/server/current/ipt-mcp.exe" -Raw).Trim() -eq 'new-server') 'Server not upgraded'
        Assert ((Get-Content -LiteralPath "$($fixture.Root)/server/dev/ipt-mcp.exe" -Raw).Trim() -eq 'dev-build') 'Non-version server dir was cleaned'
        Assert (@(Get-ChildItem -LiteralPath $fixture.Root -Recurse -Filter '*.iptmcp-rollback-*').Count -eq 0) 'Transaction backups leaked after success'
    }
    Test 'Server copy still running is kept whole and swept once free' {
        $fixture = New-SetupFixture
        Copy-Item -LiteralPath "$env:WINDIR\System32\PING.EXE" -Destination "$($fixture.Root)/server/current/ipt-mcp.exe" -Force
        $proc = Start-Process -FilePath "$($fixture.Root)\server\current\ipt-mcp.exe" -ArgumentList '-n','30','127.0.0.1' -WindowStyle Hidden -PassThru
        try {
            Start-Sleep -Milliseconds 500
            $output = Invoke-FixtureSetup $fixture 3>&1 6>&1 | Out-String -Width 4096
            Assert ((Get-Content -LiteralPath "$($fixture.Root)/server/current/ipt-mcp.exe" -Raw).Trim() -eq 'new-server') 'Server not upgraded'
            $kept = @(Get-ChildItem -LiteralPath "$($fixture.Root)/server" -Directory | Where-Object Name -like 'current.iptmcp-rollback-*')
            Assert ($kept.Count -eq 1 -and (Test-Path -LiteralPath (Join-Path $kept[0].FullName 'ipt-mcp.exe'))) 'Running copy was not kept whole'
            Assert ($output -match 'In use\s*:') 'In-use copy not reported'
        } finally { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue; $null = $proc.WaitForExit(5000) }
        Start-Sleep -Milliseconds 300
        Invoke-FixtureSetup $fixture | Out-Null
        Assert (@(Get-ChildItem -LiteralPath "$($fixture.Root)/server" -Directory | Where-Object Name -like 'current.iptmcp-rollback-*').Count -eq 0) 'Leftover not swept after the process exited'
    }
    Test 'Leftover sweep only touches exact rollback names' {
        $fixture = New-SetupFixture
        $s = "$($fixture.Root)/server"
        $leftover = "$s/current.iptmcp-rollback-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $leftover, "$s/current.backup", "$s/0.1.0.iptmcp-rollback-xyz" -Force | Out-Null
        Set-Content -LiteralPath "$leftover/ipt-mcp.exe" 'old'
        Set-Content -LiteralPath "$s/current.backup/ipt-mcp.exe" 'keep'
        Set-Content -LiteralPath "$s/0.1.0.iptmcp-rollback-xyz/ipt-mcp.exe" 'keep'
        Invoke-FixtureSetup $fixture | Out-Null
        Assert (-not (Test-Path -LiteralPath $leftover)) 'Exact leftover was not swept'
        Assert ((Test-Path -LiteralPath "$s/current.backup/ipt-mcp.exe") -and (Test-Path -LiteralPath "$s/0.1.0.iptmcp-rollback-xyz/ipt-mcp.exe")) 'Sweep touched a non-matching name'
    }
    Test 'Inventor running blocks the upgrade before any replacement' {
        $fixture = New-SetupFixture; $fixture.Running=$true
        Assert-Throws { Invoke-FixtureSetup $fixture } 'Inventor running'
        Assert-OldInstall $fixture
    }
    Test 'WhatIf preserves installation, stages nothing and runs no smoke check' {
        $fixture = New-SetupFixture; $fixture.SmokeFails = $true
        Invoke-FixtureSetup $fixture -WhatIf -PruneOldServers 3>&1 6>&1 | Out-Null
        Assert-OldInstall $fixture
        Assert ($null -eq $script:installStage) 'WhatIf staged payload'
    }
    Test 'Smoke-check failure restores bundle, previous server, pruned versions and stray manifests' {
        $fixture = New-SetupFixture; $fixture.SmokeFails = $true
        $legacy = "$($fixture.Root)/user-addins/Bimwright.Ipt.Inv25.addin"
        Set-Content -LiteralPath $legacy (New-AddinXml 2025 'legacy' 'Bimwright.Ipt.Plugin.Inv25.dll')
        Assert-Throws { Invoke-FixtureSetup $fixture -PruneOldServers } 'could not start'
        Assert-OldInstall $fixture
        Assert (Test-Path -LiteralPath $legacy) 'Legacy manifest not restored'
        Assert (@(Get-ChildItem -LiteralPath $fixture.Root -Recurse -Filter '*.iptmcp-rollback-*').Count -eq 0) 'Rollback left backups behind'
    }
    Test 'Machine-wide manifest with our ClientId blocks the install before any change' {
        $fixture = New-SetupFixture
        $machineDir = "$($fixture.Root)/machine/ApplicationPlugins/Other.bundle/Contents/2027"
        New-Item -ItemType Directory -Path $machineDir -Force | Out-Null
        Set-Content -LiteralPath "$machineDir/Bimwright.Ipt.Inv27.addin" (New-AddinXml 2027 'machine-wide')
        Assert-Throws { Invoke-FixtureSetup $fixture } 'machine-wide'
        Assert-OldInstall $fixture
    }
    Test 'Stray add-in manifests with our ClientIds are removed; others untouched' {
        $fixture = New-SetupFixture
        $sibling = "$($fixture.Root)/plugins-user/Other.bundle/Contents/2025"
        New-Item -ItemType Directory -Path $sibling -Force | Out-Null
        Set-Content -LiteralPath "$sibling/Bimwright.Ipt.Inv25.addin" (New-AddinXml 2025 'stray' 'Bimwright.Ipt.Plugin.Inv25.dll')
        Set-Content -LiteralPath "$sibling/Bimwright.Ipt.Plugin.Inv25.dll" 'stray-dll'
        Set-Content -LiteralPath "$($fixture.Root)/user-addins/Bimwright.Ipt.Inv27.addin" (New-AddinXml 2027 'legacy')
        Set-Content -LiteralPath "$($fixture.Root)/user-addins/Other.addin" (New-AddinXml 2025 'other' 'Other.dll' '11111111-2222-3333-4444-555555555555')
        Set-Content -LiteralPath "$($fixture.Root)/user-addins/Broken.addin" 'not xml'
        $output = Invoke-FixtureSetup $fixture 3>&1 6>&1 | Out-String -Width 4096
        Assert (-not (Test-Path -LiteralPath "$sibling/Bimwright.Ipt.Inv25.addin")) 'Stray bundle manifest survived'
        Assert (Test-Path -LiteralPath "$sibling/Bimwright.Ipt.Plugin.Inv25.dll") 'Foreign bundle DLL was removed'
        Assert (-not (Test-Path -LiteralPath "$($fixture.Root)/user-addins/Bimwright.Ipt.Inv27.addin")) 'Legacy manifest survived'
        foreach ($keep in 'Other.addin', 'Broken.addin') { Assert (Test-Path -LiteralPath "$($fixture.Root)/user-addins/$keep") "Unrelated $keep touched" }
        Assert ($output -match 'Removed\s*:') 'Removed duplicates not reported'
        Assert (@(Get-ChildItem -LiteralPath $fixture.Root -Recurse -Filter '*.iptmcp-rollback-*').Count -eq 0) 'Backups leaked'
    }
    Test 'WhatIf previews duplicate removal without removing' {
        $fixture = New-SetupFixture
        $stray = "$($fixture.Root)/user-addins/Bimwright.Ipt.Inv25.addin"
        Set-Content -LiteralPath $stray (New-AddinXml 2025 'legacy')
        $output = Invoke-FixtureSetup $fixture -WhatIf 3>&1 6>&1 | Out-String -Width 4096
        Assert (Test-Path -LiteralPath $stray) 'WhatIf removed a duplicate'
        Assert ($output -match 'preview') 'WhatIf did not preview'
    }
    Test 'Installed server files are unblocked' {
        $fixture = New-SetupFixture
        Set-Content -LiteralPath "$($fixture.Source)/server/ipt-mcp.exe" -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3"
        Invoke-FixtureSetup $fixture | Out-Null
        $streams = @(Get-Item -LiteralPath "$($fixture.Root)/server/current/ipt-mcp.exe" -Stream * | Where-Object Stream -eq 'Zone.Identifier')
        Assert ($streams.Count -eq 0) 'Server exe still carries Mark-of-the-Web'
    }
    Test 'Test-ServerExecutable accepts exit 0 and rejects non-zero exit and missing files' {
        Test-ServerExecutable -Path "$env:WINDIR\System32\cmd.exe" -Arguments '/c exit 0'
        Assert-Throws { Test-ServerExecutable -Path "$env:WINDIR\System32\cmd.exe" -Arguments '/c exit 3' } 'exit code 3'
        Assert-Throws { Test-ServerExecutable -Path (Join-Path $testRoot 'missing.exe') } 'could not start'
    }
    foreach ($kind in @('empty-contents','bad-packagecontents','foreign-clientid','missing-assembly')) {
        Test "$kind payload blocks all replacements" {
            $fixture = New-SetupFixture
            $fixture.Manifest = $null # Exercise bundle validation without checksum coverage.
            $bundleDir = Join-Path $fixture.Source 'bundle'
            if ($kind -eq 'empty-contents') { Remove-Item -LiteralPath "$bundleDir/Contents" -Recurse -Force }
            elseif ($kind -eq 'bad-packagecontents') { Set-Content -LiteralPath "$bundleDir/PackageContents.xml" 'not xml' }
            elseif ($kind -eq 'foreign-clientid') { Set-Content -LiteralPath "$bundleDir/Contents/2027/Bimwright.Ipt.Inv27.addin" (New-AddinXml 2027 'bad' 'Bimwright.Ipt.Plugin.Inv27.dll' '11111111-2222-3333-4444-555555555555') }
            else { Remove-Item -LiteralPath "$bundleDir/Contents/2027/Bimwright.Ipt.Plugin.Inv27.dll" }
            Assert-Throws { Invoke-FixtureSetup $fixture } '.'
            Assert-OldInstall $fixture
        }
    }
    Test 'Verification rejects a modified file or an extra file at the target' {
        $fixture = New-SetupFixture
        $source = Join-Path $fixture.Source 'bundle'
        $target = Join-Path $fixture.Root 'bundle-verify'
        Copy-Item -LiteralPath $source -Destination $target -Recurse
        $verify = { Assert-InstalledBundle -SourceDir $source -TargetDir $target }
        & $verify
        Set-Content -LiteralPath "$target/Contents/2025/Bimwright.Ipt.Plugin.Inv25.dll" 'tampered'
        Assert-Throws $verify 'differs from the package'
        Set-Content -LiteralPath "$target/Contents/2025/Bimwright.Ipt.Plugin.Inv25.dll" 'new-plugin-2025'
        Set-Content -LiteralPath "$target/extra.txt" 'x'
        Assert-Throws $verify 'extra file'
    }
    Test 'Uninstall removes bundle and stray manifests, keeps server and other add-ins' {
        $fixture = New-SetupFixture
        Set-Content -LiteralPath "$($fixture.Root)/user-addins/Bimwright.Ipt.Inv27.addin" (New-AddinXml 2027 'legacy')
        Set-Content -LiteralPath "$($fixture.Root)/user-addins/Other.addin" (New-AddinXml 2025 'other' 'Other.dll' '11111111-2222-3333-4444-555555555555')
        Invoke-FixtureSetup $fixture -Uninstall 3>&1 6>&1 | Out-Null
        $installed = "$($fixture.Root)/plugins-user/Bimwright.Ipt.bundle"
        Assert (-not (Test-Path -LiteralPath $installed)) 'Bundle not removed'
        Assert (-not (Test-Path -LiteralPath "$($fixture.Root)/user-addins/Bimwright.Ipt.Inv27.addin")) 'Stray manifest not removed'
        Assert (Test-Path -LiteralPath "$($fixture.Root)/user-addins/Other.addin") 'Unrelated add-in touched'
        Assert ((Get-Content -LiteralPath "$($fixture.Root)/server/current/ipt-mcp.exe" -Raw).Trim() -eq 'old-server-current') 'Uninstall touched the server'
    }
    Test 'Manifest checksum failure is detected before installation' {
        $fixture = New-SetupFixture
        $manifest = [pscustomobject]@{files=@([pscustomobject]@{path='server/ipt-mcp.exe';sha256=('0' * 64)})}
        Assert-Throws { Assert-SetupManifest -Root $fixture.Source -Manifest $manifest } 'checksum failed'
        Assert-OldInstall $fixture
    }
    Test 'Rollback removes a newly created server' {
        $fixture = New-SetupFixture
        $newServer = Join-Path $fixture.Root 'server/new-version'
        $script:installChanges = New-Object System.Collections.Generic.List[object]
        Set-InstallPath -Source (Join-Path $fixture.Source 'server') -Destination $newServer
        Undo-InstallChanges
        Assert (-not (Test-Path -LiteralPath $newServer)) 'New server survived rollback'
        $script:installChanges=$null
    }
    Test 'Incomplete rollback reports retained backup and continues restoring other paths' {
        $fixture = New-SetupFixture
        $dir = Join-Path $fixture.Root 'rollback'
        New-Item -ItemType Directory -Path "$dir/new" -Force | Out-Null
        $path1 = Join-Path $dir 'one.txt'; $path2 = Join-Path $dir 'two.txt'
        Set-Content -LiteralPath $path1 'old-one'; Set-Content -LiteralPath $path2 'old-two'
        Set-Content -LiteralPath "$dir/new/one.txt" 'new-one'; Set-Content -LiteralPath "$dir/new/two.txt" 'new-two'
        $script:installChanges = New-Object System.Collections.Generic.List[object]
        Set-InstallPath -Source "$dir/new/one.txt" -Destination $path1
        Set-InstallPath -Source "$dir/new/two.txt" -Destination $path2
        $locked = [IO.File]::Open($path2, 'Open', 'Read', 'None')
        try { Assert-Throws { Undo-InstallChanges } 'Rollback incomplete' }
        finally { $locked.Dispose() }
        Assert ((Get-Content -LiteralPath $path1 -Raw).Trim() -eq 'old-one') 'Rollback stopped before earlier path'
        Assert (Test-Path -LiteralPath $script:installChanges[1].Backup) 'Failed rollback deleted its recovery backup'
        $script:installChanges=$null
    }
    Test 'Bundle-level rollback leftover is swept; unrelated names kept' {
        $fixture = New-SetupFixture
        $parent = "$($fixture.Root)/plugins-user"
        $ours = "$parent/Bimwright.Ipt.bundle.iptmcp-rollback-0123456789abcdef0123456789abcdef"
        $other = "$parent/Other.bundle.iptmcp-rollback-0123456789abcdef0123456789abcdef"
        $nonhex = "$parent/Bimwright.Ipt.bundle.iptmcp-rollback-xyz"
        New-Item -ItemType Directory -Path $ours, $other, $nonhex -Force | Out-Null
        Invoke-FixtureSetup $fixture | Out-Null
        Assert (-not (Test-Path -LiteralPath $ours)) 'Our leftover was not swept'
        Assert (Test-Path -LiteralPath $other) 'Foreign bundle leftover touched'
        Assert (Test-Path -LiteralPath $nonhex) 'Non-hex name touched'
    }
    Test 'Locked file inside bundle blocks the swap; previous install intact, no leftovers' {
        # The bundle backup is a full copy: a locked child makes Copy-Item throw
        # before the destination is touched, so rollback restores everything.
        $fixture = New-SetupFixture
        $installed = "$($fixture.Root)/plugins-user/Bimwright.Ipt.bundle"
        $parent = Split-Path -Parent $installed
        $locked = [IO.File]::Open("$installed/Contents/2027/Bimwright.Ipt.Inv27.addin", 'Open', 'Read', 'None')
        try { Assert-Throws { Invoke-FixtureSetup $fixture } 'being used|used by another process|access.*denied' }
        finally { $locked.Dispose() }
        Assert-OldInstall $fixture
        Assert (@(Get-ChildItem -LiteralPath $parent -Directory | Where-Object Name -like 'Bimwright.Ipt.bundle.iptmcp-rollback-*').Count -eq 0) 'Partial backup left behind'
    }
} finally {
    $env:USERPROFILE = $savedUserProfile
    $env:APPDATA = $savedAppData
    $env:LOCALAPPDATA = $savedLocalAppData
    $report = [ordered]@{powershell=$PSVersionTable.PSVersion.ToString();installerSha256=(Get-FileHash $installer -Algorithm SHA256).Hash;results=@($results.ToArray())}
    if ($ResultPath) { $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $ResultPath -Encoding UTF8 }
    # Only remove the uniquely created test directory under the resolved temp root.
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempRoot = [IO.Path]::GetFullPath($TestRootParent).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
if (@($results | Where-Object { -not $_.passed }).Count) { throw 'Installer regression tests failed' }
