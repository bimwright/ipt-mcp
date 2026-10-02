#Requires -Version 7.0
param([string]$RepoRoot = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$publicDocs = @(
    'AGENTS.md', 'ARCHITECTURE.md', 'CHANGELOG.md', 'CLAUDE.md',
    'CODE_OF_CONDUCT.md', 'CONTRIBUTING.md', 'SECURITY.md',
    'README.md', 'README.vi.md', 'README.ja.md', 'README.zh-CN.md',
    'docs/releasing.md', 'docs/toolbaker.md',
    'docs/testing/manual-smoke.md', 'docs/testing/smart-toasts.md',
    'docs/testing/drawing-phase1.md', 'docs/testing/readonly-tools.json',
    'docs/benchmarks/drawing-phase1-smoke.json'
)
$privatePath = '(^|/)(\.(artifacts[^/]*|worktrees|claude|cursor|kilo|agents|openclaude)|internal-docs|analysis|artifacts|runs|spikes)(/|$)|^docs/(superpowers|design|reviews|benchmarks|research-archive|archive)/|(^|/)codemap\.md$'
$paths = @(& git -c core.quotepath=false -C $RepoRoot ls-files --cached --others --exclude-standard --deduplicate)
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate the public Git tree.' }
$violations = @(
    foreach ($path in $paths) {
        # Pending deletions remain in the index until staged; CI checks a fresh checkout.
        if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $path))) { continue }
        if (($path -match $privatePath -and $path -notin $publicDocs) -or
            ($path -match '\.(md|mdx|markdown|rst|txt|canvas|docx?|pdf)$' -and $path -notin $publicDocs) -or
            ($path.StartsWith('docs/', [StringComparison]::OrdinalIgnoreCase) -and $path -notin $publicDocs)) {
            $path
        }
    }
)
if ($violations.Count) {
    throw "Files need public-content review or relocation to private documentation:`n$($violations -join "`n")"
}
'Public tree check passed. This path check is not a secret scanner or a history audit.'
