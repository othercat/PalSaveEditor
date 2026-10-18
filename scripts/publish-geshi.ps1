param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repo ('artifacts/publish-geshi-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
if (Test-Path -LiteralPath $output) { throw 'Publish output already exists.' }
Push-Location $repo
try {
    & dotnet publish src/PalSaveEditor.WinForms/PalSaveEditor.WinForms.csproj -c Release -p:GeshiEdition=true -p:DebugType=None -p:DebugSymbols=false -o $output --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Dedicated publish failed.' }
    $exe = Join-Path $output '隔世追凶存档编辑器.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw 'Dedicated executable is missing.' }
    Copy-Item -LiteralPath (Join-Path $repo 'docs/GESHI_SAVE_EDITOR.md') -Destination (Join-Path $output '使用说明.md')
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $output

    # Use this publish's restored package graph, not a separately maintained list.
    # Verify each copied NuGet DLL against its exact package asset before attaching notices.
    $assetsPath = Join-Path $repo 'src/PalSaveEditor.WinForms/obj/Geshi/project.assets.json'
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
    $targetName = if ($assets.targets.ContainsKey('net472/win-x86')) { 'net472/win-x86' } else { 'net472' }
    if (-not $assets.targets.ContainsKey($targetName)) { throw 'The restored publish target is missing.' }
    $licenses = Join-Path $output 'Licenses'
    New-Item -ItemType Directory -Path $licenses | Out-Null
    $licensedDlls = @{}
    $packages = @()
    foreach ($entry in ($assets.targets[$targetName].GetEnumerator() | Sort-Object Key)) {
        if ($entry.Value.type -ne 'package') { continue }
        $library = $assets.libraries[$entry.Key]
        $packageDirectory = $null
        foreach ($cache in $assets.packageFolders.Keys) {
            $candidate = Join-Path $cache $library.path
            if (Test-Path -LiteralPath $candidate -PathType Container) { $packageDirectory = $candidate; break }
        }
        if (-not $packageDirectory) { throw "Restored package cache is missing: $($entry.Key)" }
        $runtimeAssets = @()
        $assetPaths = @($entry.Value.runtime.Keys) + @($entry.Value.runtimeTargets.Keys)
        foreach ($asset in ($assetPaths | Where-Object { $_ -and $_.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase) } | Sort-Object -Unique)) {
            $name = [IO.Path]::GetFileName($asset)
            $publishedDll = Join-Path $output $name
            if (-not (Test-Path -LiteralPath $publishedDll -PathType Leaf)) { continue }
            $packageDll = Join-Path $packageDirectory $asset
            $hash = (Get-FileHash -LiteralPath $packageDll -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($hash -ne (Get-FileHash -LiteralPath $publishedDll -Algorithm SHA256).Hash.ToLowerInvariant()) {
                throw "Published DLL differs from its restored NuGet asset: $name ($($entry.Key))"
            }
            if ($licensedDlls.ContainsKey($name)) { throw "Ambiguous NuGet ownership: $name" }
            $licensedDlls[$name] = $entry.Key
            $runtimeAssets += [ordered]@{ File = $name; PackagePath = $asset; Sha256 = $hash }
        }
        if ($runtimeAssets.Count -eq 0) { continue }
        $identity = $entry.Key.Split('/')
        if ($identity.Count -ne 2 -or $library.path -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.+-]+$') {
            throw "Unexpected restored package identity: $($entry.Key)"
        }
        $noticePaths = @($library.files | Where-Object { [IO.Path]::GetFileName($_) -match '^(LICENSE|THIRD-PARTY-NOTICES)' } | Sort-Object)
        if (-not ($noticePaths | Where-Object { [IO.Path]::GetFileName($_) -match '^LICENSE' })) {
            throw "No license file in restored package: $($entry.Key)"
        }
        $notices = @()
        foreach ($notice in $noticePaths) {
            $sourceNotice = Join-Path $packageDirectory $notice
            $relativeNotice = $library.path + '/' + $notice
            $destinationNotice = [IO.Path]::GetFullPath((Join-Path $licenses $relativeNotice))
            if (-not $destinationNotice.StartsWith($licenses + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Package notice path escapes the license directory: $($entry.Key)"
            }
            New-Item -ItemType Directory -Path (Split-Path -Parent $destinationNotice) -Force | Out-Null
            Copy-Item -LiteralPath $sourceNotice -Destination $destinationNotice
            $sourceHash = (Get-FileHash -LiteralPath $sourceNotice -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($sourceHash -ne (Get-FileHash -LiteralPath $destinationNotice -Algorithm SHA256).Hash.ToLowerInvariant()) {
                throw "License copy verification failed: $relativeNotice"
            }
            $notices += [ordered]@{ PackagePath = $notice; PublishedPath = $relativeNotice; Sha256 = $sourceHash }
        }
        $packages += [ordered]@{
            Package = $identity[0]
            Version = $identity[1]
            Source = 'https://www.nuget.org/packages/' + $entry.Key
            PackageSha512 = $library.sha512
            RuntimeAssets = $runtimeAssets
            Notices = $notices
        }
    }
    foreach ($dll in (Get-ChildItem -LiteralPath $output -Filter '*.dll' -File)) {
        if ($dll.Name -ne 'PalSaveEditor.Core.dll' -and -not $licensedDlls.ContainsKey($dll.Name)) {
            throw "Published dependency has no NuGet attribution: $($dll.Name)"
        }
    }
    $packages | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $licenses 'packages.json') -Encoding utf8
    $noticeText = @(
        'Third-party NuGet components shipped with the Geshi Easy save editor.',
        'Original LICENSE* and THIRD-PARTY-NOTICES* files are copied verbatim from the packages restored for this publish.',
        'packages.json records package versions, public NuGet sources, package integrity hashes and exact shipped DLL/notice hashes.',
        ''
    )
    foreach ($package in $packages) {
        $noticeText += $package.Package + ' ' + $package.Version
        $noticeText += 'Source: ' + $package.Source
        $noticeText += 'DLL: ' + (($package.RuntimeAssets | ForEach-Object { $_.File }) -join ', ')
        $noticeText += 'Notices: ' + (($package.Notices | ForEach-Object { $_.PublishedPath }) -join ', ')
        $noticeText += ''
    }
    $noticeText | Set-Content -LiteralPath (Join-Path $licenses 'README.txt') -Encoding utf8
    Write-Output "Third-party notices: $($packages.Count) packages / $($licensedDlls.Count) DLLs"
    Write-Output "Publish directory: $output"
    Get-FileHash -LiteralPath $exe -Algorithm SHA256
} finally { Pop-Location }
