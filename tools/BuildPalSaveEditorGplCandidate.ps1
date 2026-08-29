[CmdletBinding()]
param(
    [string]$CandidateName = 'extended-role-magics-0.1.5-gpl-candidate-20260829',
    [string]$RuntimeRoot = 'artifacts\v159-extended-role-magics'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$candidateRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot $CandidateName))
$runtimeRootFull = [IO.Path]::GetFullPath((Join-Path $repoRoot $RuntimeRoot))

if (-not $candidateRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Candidate path escapes the repository artifacts directory: $candidateRoot"
}
if (Test-Path -LiteralPath $candidateRoot) {
    throw "Candidate already exists; refusing to overwrite it: $candidateRoot"
}
if (-not $runtimeRootFull.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Runtime root escapes the repository artifacts directory: $runtimeRootFull"
}

$runtimeDirectories = [ordered]@{
    'PalSaveEditor' = 'PalSaveEditor-win7-net472'
    'PalSaveChecker' = 'PalSaveChecker-win7-net472'
}
foreach ($entry in $runtimeDirectories.GetEnumerator()) {
    $source = Join-Path $runtimeRootFull $entry.Value
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Published runtime directory is missing: $source"
    }
}

$editorExe = Join-Path $runtimeRootFull 'PalSaveEditor-win7-net472\PalSaveEditor.exe'
$checkerExe = Join-Path $runtimeRootFull 'PalSaveChecker-win7-net472\仙剑98存档检查工具.exe'
if ((Get-Item -LiteralPath $editorExe).VersionInfo.FileVersion -ne '0.1.5.0') {
    throw 'PalSaveEditor.exe is not version 0.1.5.0'
}
if (-not (Test-Path -LiteralPath $checkerExe -PathType Leaf)) {
    throw "PalSaveChecker executable is missing: $checkerExe"
}

New-Item -ItemType Directory -Path $candidateRoot | Out-Null
foreach ($entry in $runtimeDirectories.GetEnumerator()) {
    Copy-Item -LiteralPath (Join-Path $runtimeRootFull $entry.Value) `
        -Destination (Join-Path $candidateRoot $entry.Key) -Recurse
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $candidateRoot 'LICENSE')
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination (Join-Path $candidateRoot 'README.md')
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\DREAM220_VISIBLE_PUBLIC_PROFILE.md') `
    -Destination (Join-Path $candidateRoot 'DREAM220_VISIBLE_PUBLIC_PROFILE.md')

$sourceStage = Join-Path $candidateRoot '_source-stage'
New-Item -ItemType Directory -Path $sourceStage | Out-Null

$sourceFiles = @(& git -C $repoRoot -c core.quotepath=false ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) {
    throw "git source inventory failed: $LASTEXITCODE"
}

$excludedPrefixes = @('.agents/', '.claude/', '.codegraph/', 'artifacts/')
$excludedFiles = @('AGENTS.md', 'CLAUDE.md')
foreach ($relative in $sourceFiles) {
    $normalized = $relative.Replace('\', '/')
    if ($excludedFiles -contains $normalized) { continue }
    if ($excludedPrefixes | Where-Object {
            $normalized.StartsWith($_, [StringComparison]::OrdinalIgnoreCase)
        }) { continue }
    if ($normalized -match '(^|/)(bin|obj)/' -or $normalized -match '\.(pfx|snk|suo|user)$') {
        continue
    }

    $source = Join-Path $repoRoot $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
    $destination = Join-Path $sourceStage $relative
    $destinationDirectory = Split-Path -Parent $destination
    if (-not (Test-Path -LiteralPath $destinationDirectory)) {
        New-Item -ItemType Directory -Path $destinationDirectory | Out-Null
    }
    Copy-Item -LiteralPath $source -Destination $destination
}

$head = (& git -C $repoRoot rev-parse HEAD).Trim()
$metadata = @(
    'PalSaveEditor and PalSaveChecker complete corresponding source snapshot'
    'SPDX-License-Identifier: GPL-2.0-only'
    'Repository: https://github.com/othercat/PalSaveEditor'
    "Source revision: $head"
    'Snapshot policy: current tracked and untracked source, excluding generated outputs, local Goal overlays, private-agent notes and signing-key file types.'
    'Build target: Windows 7 compatible net472 only.'
) -join [Environment]::NewLine
[IO.File]::WriteAllText(
    (Join-Path $sourceStage 'SOURCE_SNAPSHOT_METADATA.txt'),
    $metadata + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

$sourceZip = Join-Path $candidateRoot 'PalSaveEditor-0.1.5-source.zip'
Compress-Archive -Path (Join-Path $sourceStage '*') -DestinationPath $sourceZip -CompressionLevel Optimal

$resolvedStage = [IO.Path]::GetFullPath($sourceStage)
if (-not $resolvedStage.StartsWith($candidateRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Source staging path escaped candidate root: $resolvedStage"
}
Remove-Item -LiteralPath $resolvedStage -Recurse -Force

$payload = Get-ChildItem -LiteralPath $candidateRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{
        path = $_.FullName.Substring($candidateRoot.Length + 1).Replace('\', '/')
        size = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
}
$manifest = [ordered]@{
    schema = 'pal98.local-public-tool-release.v1'
    product = 'PalSaveEditor and PalSaveChecker'
    version = '0.1.5'
    license = 'GPL-2.0-only'
    repository_owner = 'othercat'
    repository = 'https://github.com/othercat/PalSaveEditor'
    build_configuration = 'Release; net472 x86 only'
    source_revision = $head
    source_snapshot_includes_uncommitted_changes = $true
    payload = $payload
}
[IO.File]::WriteAllText(
    (Join-Path $candidateRoot 'release-manifest.json'),
    ($manifest | ConvertTo-Json -Depth 6) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

$sumLines = Get-ChildItem -LiteralPath $candidateRoot -File -Recurse |
    Where-Object Name -ne 'SHA256SUMS.txt' |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($candidateRoot.Length + 1).Replace('\', '/')
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, $relative
    }
[IO.File]::WriteAllText(
    (Join-Path $candidateRoot 'SHA256SUMS.txt'),
    ($sumLines -join [Environment]::NewLine) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

Write-Output "PalSaveEditor GPL candidate: $candidateRoot"
Get-Content -LiteralPath (Join-Path $candidateRoot 'SHA256SUMS.txt')
