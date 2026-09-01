param(
    [Parameter(Mandatory = $true)][string]$GameDirectory,
    [switch]$GeneralScanOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

if ([IntPtr]::Size -ne 4) {
    $powerShell32 = Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path -LiteralPath $powerShell32)) {
        throw '32-bit Windows PowerShell is required to load the deployed x86 checker assemblies'
    }
    $checkerArguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $PSCommandPath,
        '-GameDirectory', $GameDirectory)
    if ($GeneralScanOnly) {
        $checkerArguments += '-GeneralScanOnly'
    }
    & $powerShell32 @checkerArguments
    exit $LASTEXITCODE
}

$root = (Get-Item -LiteralPath ([IO.Path]::GetFullPath($GameDirectory))).FullName
$tools = Join-Path $root 'Tools'
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $assemblyName = [Reflection.AssemblyName]::new($eventArgs.Name).Name + '.dll'
    $candidate = Join-Path $tools $assemblyName
    if (Test-Path -LiteralPath $candidate) {
        return [Reflection.Assembly]::LoadFrom($candidate)
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$editorCoreAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $tools 'PalSaveEditor.Core.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $tools 'PalSaveChecker.Core.dll'))
$editorAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $tools 'PalSaveEditor.exe'))
$editorCoreReference = @($editorAssembly.GetReferencedAssemblies() |
    Where-Object Name -eq 'PalSaveEditor.Core') | Select-Object -First 1
if ($null -eq $editorCoreReference -or
    $editorCoreReference.Version -ne $editorCoreAssembly.GetName().Version) {
    throw 'Deployed PalSaveEditor.exe and shared PalSaveEditor.Core.dll assembly versions do not match'
}

$service = New-Object PalSaveChecker.Core.SaveCompatibilityService
$report = $service.Check($root)
if (@($report.Saves).Count -ne 5) { throw 'Deployed checker did not return five save slots' }
Write-Output "reference=$($report.ReferenceDescription); reference-error=$($report.ReferenceError)"

foreach ($item in @($report.Saves)) {
    Write-Output (
        '{0}: status={1}; repairable={2}; definitions={3}; scripts={4}; empty-contact={5}; error={6}' -f
        $item.FileName, $item.Status, $item.Repairable, $item.DefinitionMismatchCount,
        $item.InvalidScriptCount, $item.EmptyContactTriggerCount, $item.Error)
}

if (-not $GeneralScanOnly) {
    $slot4 = @($report.Saves | Where-Object FileName -eq '4.RPG') | Select-Object -First 1
    $slot5 = @($report.Saves | Where-Object FileName -eq '5.RPG') | Select-Object -First 1
    if ($null -eq $slot4 -or $null -eq $slot5 -or
        $slot4.EmptyContactTriggerCount -ne 1 -or $slot5.EmptyContactTriggerCount -ne 1) {
        throw 'Deployed checker did not detect the expected read-only 4.RPG/5.RPG empty-contact state'
    }
}

Write-Output (
    'PASS: deployed x86 save checker loaded with the existing shared editor core and performed the expected read-only scan' +
    $(if ($GeneralScanOnly) { ' (general profile mode)' } else { '' }))
