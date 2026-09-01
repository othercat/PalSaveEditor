#Requires -Version 7.0

param(
    [Parameter(Mandatory = $true)]
    [string]$GameRoot,

    [ValidateRange(1, 5)]
    [int]$SaveSlot = 2,

    [switch]$AllowOtherRepairableIssue,

    [string]$AssemblyRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$sourceRoot = [IO.Path]::GetFullPath($GameRoot)
$saveName = "$SaveSlot.RPG"
$sourceSave = Join-Path $sourceRoot $saveName
if (-not (Test-Path -LiteralPath $sourceSave)) {
    throw "Source save does not exist: $sourceSave"
}
$sourceHashBefore = (Get-FileHash -LiteralPath $sourceSave -Algorithm SHA256).Hash
$sourceSidecar = $sourceSave + '.pal98-ext-magics.json'
$sourceSidecarHashBefore = if (Test-Path -LiteralPath $sourceSidecar) {
    (Get-FileHash -LiteralPath $sourceSidecar -Algorithm SHA256).Hash
} else {
    $null
}

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = Join-Path $tempBase (
    'pal98-save-migration-' + [Guid]::NewGuid().ToString('N'))
$resolvedTemp = [IO.Path]::GetFullPath($tempRoot)
if (-not $resolvedTemp.StartsWith(
        $tempBase,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe temporary path: $resolvedTemp"
}

New-Item -ItemType Directory -Force -Path $resolvedTemp | Out-Null
try {
    $targetSave = Join-Path $resolvedTemp $saveName
    Copy-Item -LiteralPath $sourceSave -Destination $targetSave
    $sidecarSuffix = '.pal98-ext-magics.json'
    if (Test-Path -LiteralPath $sourceSidecar) {
        Copy-Item -LiteralPath $sourceSidecar -Destination (
            $targetSave + $sidecarSuffix)
    }

    $sourceProfiles = Join-Path $sourceRoot 'palmod\Profiles'
    $targetProfiles = Join-Path $resolvedTemp 'palmod\Profiles'
    New-Item -ItemType Directory -Force -Path $targetProfiles | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceProfiles 'current.json') `
        -Destination (Join-Path $targetProfiles 'current.json')
    $pointer = Get-Content -Raw -LiteralPath (
        Join-Path $sourceProfiles 'current.json') | ConvertFrom-Json
    $profileId = [string]$pointer.profile_id
    $profileVersion = [string]$pointer.profile_version
    $sourceProfile = Join-Path $sourceProfiles $profileId
    $targetProfile = Join-Path $targetProfiles $profileId
    New-Item -ItemType Directory -Force -Path $targetProfile | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceProfile $profileVersion) `
        -Destination (Join-Path $targetProfile $profileVersion) -Recurse

    Get-ChildItem -LiteralPath $sourceProfile -Directory |
        Where-Object Name -ne $profileVersion |
        ForEach-Object {
            $sourceCatalog = Join-Path $_.FullName `
                'palmod\profile\content-catalog.json'
            if (Test-Path -LiteralPath $sourceCatalog) {
                $targetCatalog = Join-Path $targetProfile (
                    $_.Name + '\palmod\profile\content-catalog.json')
                New-Item -ItemType Directory -Force -Path (
                    Split-Path $targetCatalog) | Out-Null
                Copy-Item -LiteralPath $sourceCatalog -Destination $targetCatalog
                (Get-Item -LiteralPath $targetCatalog).LastWriteTimeUtc =
                    (Get-Item -LiteralPath $sourceCatalog).LastWriteTimeUtc
            }
        }
    (Get-Item -LiteralPath $targetSave).LastWriteTimeUtc =
        (Get-Item -LiteralPath $sourceSave).LastWriteTimeUtc

    $output = if ([string]::IsNullOrWhiteSpace($AssemblyRoot)) {
        Resolve-Path (
            Join-Path $repoRoot 'tests\PalSaveChecker.Core.Tests\bin\Release\net472')
    } else {
        Resolve-Path $AssemblyRoot
    }
    [Reflection.Assembly]::LoadFrom(
        (Join-Path $output 'PalSaveEditor.Core.dll')) | Out-Null
    [Reflection.Assembly]::LoadFrom(
        (Join-Path $output 'PalSaveChecker.Core.dll')) | Out-Null
    $service = [PalSaveChecker.Core.SaveCompatibilityService]::new()
    $before = $service.Check($resolvedTemp).Saves |
        Where-Object FileName -eq $saveName
    if (-not $AllowOtherRepairableIssue -and
        -not $before.LearnedMagicProfileIssue) {
        throw "Isolated source save did not expose a learned-magic Profile issue."
    }

    $repair = $service.Repair($resolvedTemp, $true)
    $result = $repair.Results | Where-Object FileName -eq $saveName
    $after = $repair.After.Saves | Where-Object FileName -eq $saveName
    if (-not $result.Success -or $after.Status -ne 'Clean' -or
        -not (Test-Path -LiteralPath $result.BackupPath)) {
        throw "Isolated repair or post-write verification failed: $($result.Message)"
    }

    $sourceHashAfter = (Get-FileHash -LiteralPath $sourceSave -Algorithm SHA256).Hash
    if ($sourceHashAfter -ne $sourceHashBefore) {
        throw "Source save changed during isolated repair validation."
    }
    $sourceSidecarHashAfter = if (Test-Path -LiteralPath $sourceSidecar) {
        (Get-FileHash -LiteralPath $sourceSidecar -Algorithm SHA256).Hash
    } else {
        $null
    }
    if ($sourceSidecarHashAfter -ne $sourceSidecarHashBefore) {
        throw "Source sidecar changed during isolated repair validation."
    }
    [pscustomobject]@{
        SourceSave = $sourceSave
        SourceHash = $sourceHashAfter
        BeforeStatus = $before.Status
        BeforeIssue = $before.LearnedMagicProfileError
        RepairMessage = $result.Message
        BackupCreated = $true
        AfterStatus = $after.Status
        SourceUnchanged = $true
        SourceSidecarUnchanged = $true
    }
}
finally {
    $cleanupTarget = [IO.Path]::GetFullPath($resolvedTemp)
    if ($cleanupTarget.StartsWith(
            $tempBase,
            [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $cleanupTarget)) {
        Remove-Item -LiteralPath $cleanupTarget -Recurse -Force
    }
}
