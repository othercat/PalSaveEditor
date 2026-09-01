#Requires -Version 7.0

param(
    [Parameter(Mandatory = $true)]
    [string]$GameRoot,

    [ValidateRange(1, 5)]
    [int]$SaveSlot = 2,

    [string]$AssemblyRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$sourceRoot = [IO.Path]::GetFullPath($GameRoot)
$saveName = "$SaveSlot.RPG"
$sourceSave = Join-Path $sourceRoot $saveName
$sidecarSuffix = '.pal98-ext-magics.json'
$sourceSidecar = $sourceSave + $sidecarSuffix
if (-not (Test-Path -LiteralPath $sourceSave) -or
    -not (Test-Path -LiteralPath $sourceSidecar)) {
    throw "Source save or sidecar does not exist: $sourceSave"
}
$sourceSaveHash = (Get-FileHash -LiteralPath $sourceSave -Algorithm SHA256).Hash
$sourceSidecarHash = (Get-FileHash -LiteralPath $sourceSidecar -Algorithm SHA256).Hash

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = [IO.Path]::GetFullPath((Join-Path $tempBase (
    'palsaveeditor-native-sidecar-' + [Guid]::NewGuid().ToString('N'))))
if (-not $tempRoot.StartsWith(
        $tempBase,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe temporary path: $tempRoot"
}

New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
    $testSave = Join-Path $tempRoot $saveName
    $testSidecar = $testSave + $sidecarSuffix
    Copy-Item -LiteralPath $sourceSave -Destination $testSave
    Copy-Item -LiteralPath $sourceSidecar -Destination $testSidecar
    $testSidecarHash = (Get-FileHash -LiteralPath $testSidecar -Algorithm SHA256).Hash

    $output = if ([string]::IsNullOrWhiteSpace($AssemblyRoot)) {
        Resolve-Path (
            Join-Path $repoRoot 'tests\PalSaveEditor.Core.Tests\bin\Release\net472')
    } else {
        Resolve-Path $AssemblyRoot
    }
    [Reflection.Assembly]::LoadFrom(
        (Join-Path $output 'PalSaveEditor.Core.dll')) | Out-Null
    $document = [PalSaveEditor.Core.PalSaveDocument]::Load(
        $testSave,
        [PalSaveEditor.Core.SaveFormat]::Auto,
        $sourceRoot)
    if ($document.HasExtendedMagicSidecar -or
        -not [string]::IsNullOrWhiteSpace($document.ExtendedMagicSidecarWarning)) {
        throw "Native-only stale sidecar was not ignored silently."
    }

    $document.Save($testSave, $false) | Out-Null
    if ((Get-FileHash -LiteralPath $testSidecar -Algorithm SHA256).Hash -ne
        $testSidecarHash) {
        throw "Editor rewrote the native-only stale sidecar."
    }
    $reloaded = [PalSaveEditor.Core.PalSaveDocument]::Load(
        $testSave,
        [PalSaveEditor.Core.SaveFormat]::Auto,
        $sourceRoot)
    if (-not [string]::IsNullOrWhiteSpace(
            $reloaded.ExtendedMagicSidecarWarning)) {
        throw "Native-only stale sidecar warned after the isolated save."
    }

    if ((Get-FileHash -LiteralPath $sourceSave -Algorithm SHA256).Hash -ne
            $sourceSaveHash -or
        (Get-FileHash -LiteralPath $sourceSidecar -Algorithm SHA256).Hash -ne
            $sourceSidecarHash) {
        throw "Source save or sidecar changed during isolated validation."
    }

    [pscustomobject]@{
        PowerShellEdition = $PSVersionTable.PSEdition
        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
        SourceSave = $sourceSave
        WarningBeforeSave = $document.ExtendedMagicSidecarWarning
        WarningAfterSave = $reloaded.ExtendedMagicSidecarWarning
        SidecarRewritten = $false
        SourceUnchanged = $true
    }
}
finally {
    $cleanupTarget = [IO.Path]::GetFullPath($tempRoot)
    if ($cleanupTarget.StartsWith(
            $tempBase,
            [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $cleanupTarget)) {
        Remove-Item -LiteralPath $cleanupTarget -Recurse -Force
    }
}
