#Requires -Version 5.1
<#
.SYNOPSIS
    Builds one release: tests, structure fixtures, publish, archive, verify.

.DESCRIPTION
    A release used to be a list of commands typed from memory. This script makes it one command,
    with the tests and the fixture runner as gates: if either fails, nothing is published and no
    archive is produced.

    The publish is single-file: the archive holds one Witcher3ModManager.exe, the Resources folder
    and the two documents. Nobody has to scroll past 250 runtime DLLs to find the program.

    The version shown inside the app comes from Witcher3ModManager\Core\AppInfo.cs. The csproj
    carries the same number for the exe metadata, and a unit test fails if the two drift apart.

.PARAMETER Version
    Overrides the version read from AppInfo.cs. Used for the archive name only.

.PARAMETER FrameworkDependent
    Publishes the small build that needs the .NET desktop runtime instead of the self-contained one.

.EXAMPLE
    .\tools\release.ps1
    .\tools\release.ps1 -FrameworkDependent
#>
param(
    [string]$Version,
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$appDir = Join-Path $repo 'Witcher3ModManager'
$appProject = Join-Path $appDir 'Witcher3ModManager.csproj'
$testProject = Join-Path $repo 'Witcher3ModManager.Tests\Witcher3ModManager.Tests.csproj'
$fixtureProject = Join-Path $repo 'Witcher3ModManager.FixtureRunner\Witcher3ModManager.FixtureRunner.csproj'

function Write-Step([string]$Text) { Write-Host "==> $Text" -ForegroundColor Cyan }

if (-not $Version) {
    $appInfoPath = Join-Path $appDir 'Core\AppInfo.cs'
    $appInfo = Get-Content -LiteralPath $appInfoPath -Raw
    if ($appInfo -notmatch 'Version\s*=\s*"(\d+\.\d+\.\d+)"') {
        throw "Could not read AppInfo.Version from $appInfoPath"
    }
    $Version = $Matches[1]
}
Write-Step "version $Version"

# 1. Unit tests. These include the translation guards, the fixture theories and the packaging
#    metadata check, so a broken release usually stops here.
Write-Step 'dotnet test'
& dotnet test $testProject --nologo
if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed; nothing was published.' }

# 2. Structure fixtures through the zero-dependency runner, as a second, independent gate.
Write-Step 'structure fixtures'
$fixtureOutput = & dotnet run --project $fixtureProject -c Debug
if ($LASTEXITCODE -ne 0 -or (($fixtureOutput -join "`n") -notmatch 'ALL OK')) {
    $fixtureOutput | Select-Object -Last 20 | ForEach-Object { Write-Host $_ }
    throw 'Structure fixtures failed; nothing was published.'
}

# 3. Publish into a clean folder. The containment check is deliberate: this step clears a folder.
$flavour = if ($FrameworkDependent) { 'framework' } else { 'selfcontained' }
$publishDir = Join-Path $appDir "publish_$flavour"
$repoFull = [System.IO.Path]::GetFullPath($repo).TrimEnd('\') + '\'
$publishFull = [System.IO.Path]::GetFullPath($publishDir)
if (-not $publishFull.StartsWith($repoFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clear a folder outside the repository: $publishFull"
}

Write-Step "publish ($flavour)"
if (Test-Path -LiteralPath $publishFull) {
    Get-ChildItem -LiteralPath $publishFull -Force | Remove-Item -Recurse -Force
}

$publishArgs = @(
    'publish', $appProject, '-c', 'Release', '-o', $publishDir, '--nologo',
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None'
)
if (-not $FrameworkDependent) { $publishArgs += @('-r', 'win-x64', '--self-contained', 'true') }
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

# 4. Archive. 7-Zip is looked up the same way the app looks it up, with PATH as the last resort.
$sevenZip = @(
    'D:\7-Zip\7z.exe',
    'C:\Program Files\7-Zip\7z.exe',
    'C:\Program Files (x86)\7-Zip\7z.exe'
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $sevenZip) {
    $command = Get-Command 7z -ErrorAction SilentlyContinue
    if ($command) { $sevenZip = $command.Source }
}
if (-not $sevenZip) {
    throw "7-Zip not found. The published files are ready in $publishDir; archive them by hand or configure the path."
}

$archive = Join-Path $repo "Witcher3ModManager_v${Version}_$flavour.7z"
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }

Write-Step "archive -> $archive"
& $sevenZip a -t7z -mx=9 -y $archive (Join-Path $publishDir '*')
if ($LASTEXITCODE -ne 0) { throw 'Archiving failed.' }

# 5. Verify the artifact instead of trusting the commands above.
$exe = Join-Path $publishDir 'Witcher3ModManager.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Published exe is missing: $exe" }

# Single-file is the point of this layout; loose DLLs mean it silently fell back to the old shape.
$looseDlls = @(Get-ChildItem -LiteralPath $publishFull -Filter *.dll -File)
if ($looseDlls.Count -gt 0) {
    throw "Publish produced $($looseDlls.Count) loose DLL(s); single-file publishing did not take effect."
}

$fileVersion = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
if (-not $fileVersion.StartsWith("$Version.", [System.StringComparison]::Ordinal)) {
    throw "The exe reports version $fileVersion, which does not belong to $Version."
}

$sizeMiB = [math]::Round((Get-Item -LiteralPath $archive).Length / 1MB, 1)
Write-Step "done: $archive"
Write-Host "    exe FileVersion : $fileVersion"
Write-Host "    archive size    : $sizeMiB MiB"
Write-Host "    next            : git tag v$Version   (after the manual checklist in docs\regression-matrix.md)"
