param(
    [Parameter(Mandatory = $true)][string]$GameRoot,
    [string[]]$ForeignSave = @(),
    [string]$OriginalGameRoot,
    [string]$SimplifiedGameRoot,
    [string]$Pal98GameRoot,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$source = (Resolve-Path -LiteralPath $GameRoot).Path
$resolvedForeign = @($ForeignSave | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })
$matrix = @($OriginalGameRoot, $SimplifiedGameRoot, $Pal98GameRoot | Where-Object { $_ })
if ($matrix.Count -ne 0 -and $matrix.Count -ne 3) { throw 'Supply all three resource matrix roots together.' }
$resolvedMatrix = @($matrix | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })
$run = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $run) { throw 'Choose a new evidence directory.' }
$build = Join-Path $run 'build'
New-Item -ItemType Directory -Path $run | Out-Null
Push-Location $repo
try {
    & dotnet build tests/PalSaveEditor.Geshi.Tests/PalSaveEditor.Geshi.Tests.csproj -c Release -p:GeshiEdition=true "-p:PalArtifactRoot=$build" --nologo -v:minimal 2>&1 | Tee-Object -FilePath (Join-Path $run 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Dedicated test build failed.' }
    $exe = Join-Path $build 'PalSaveEditor.Geshi.Tests/bin/Release/net472/PalSaveEditor.Geshi.Tests.exe'
    & $exe $source (Join-Path $run 'fixtures') @resolvedForeign 2>&1 | Tee-Object -FilePath (Join-Path $run 'tests.log')
    if ($LASTEXITCODE -ne 0) { throw "Dedicated tests failed. Evidence: $run" }
    if ($resolvedMatrix.Count -eq 3) {
        & $exe --resource-matrix @resolvedMatrix (Join-Path $run 'resource-matrix') 2>&1 | Tee-Object -FilePath (Join-Path $run 'resource-matrix.log')
        if ($LASTEXITCODE -ne 0) { throw "Resource matrix failed. Evidence: $run" }
    }
    Write-Output "Evidence: $run"
} finally { Pop-Location }
