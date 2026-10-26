#!/usr/bin/env pwsh
# Packs Maniacal into artifacts/packages and checks the packages the way a consumer would:
#   - a console app restores Maniacal.Scumm from the local feed and plays the game headless, then
#     each of -MoreGames
#   - the Maniacal tool is installed from the feed, lists the game and takes a screenshot
# Usage: tools/PackageSmoke/run.ps1 -Game <folder> [-MoreGames <folder>,...] [-SkipPack] [-VersionSuffix alpha.2]
param(
    [Parameter(Mandatory)] [string] $Game,
    [string[]] $MoreGames = @(),
    [string] $VersionSuffix,
    [switch] $SkipPack
)

$ErrorActionPreference = 'Stop'
$smoke = $PSScriptRoot
$root = (Resolve-Path (Join-Path $smoke '../..')).Path
$artifacts = Join-Path $root 'artifacts'
$feed = Join-Path $artifacts 'packages'
$cache = Join-Path $artifacts 'nuget-cache'
$toolDir = Join-Path $artifacts 'tool'

function Step($name) { Write-Host "`n=== $name" -ForegroundColor Cyan }
function Run($file, [string[]] $arguments) {
    Write-Host "> $file $($arguments -join ' ')" -ForegroundColor DarkGray
    & $file @arguments
    if ($LASTEXITCODE -ne 0) { throw "$file exited with $LASTEXITCODE" }
}

foreach ($dir in $cache, $toolDir) {
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
}
if (-not $SkipPack) {
    Step 'Pack'
    if (Test-Path $feed) { Remove-Item -Recurse -Force $feed }
    $packArgs = @((Join-Path $root 'Maniacal.slnx'), '-c', 'Release', '-o', $feed)
    if ($VersionSuffix) { $packArgs += "-p:VersionSuffix=$VersionSuffix" }
    Run dotnet (@('pack') + $packArgs)
}

$library = Get-ChildItem $feed -Filter 'Maniacal.Scumm.*.nupkg' | Where-Object Name -NotLike '*.symbols.nupkg' | Select-Object -First 1
if (-not $library) { throw "no Maniacal.Scumm package in $feed" }
$version = $library.Name -replace '^Maniacal\.Scumm\.(.+)\.nupkg$', '$1'
Write-Host "Package version $version"
Get-ChildItem $feed | ForEach-Object { Write-Host ("  {0,10:N0}  {1}" -f $_.Length, $_.Name) }

# packages restored in this run come from the feed, never from a stale copy in the global cache
$env:NUGET_PACKAGES = $cache
Push-Location $smoke
try {
    Step 'Console app from the package'
    Run dotnet @('run', '--project', 'ConsoleSmoke', '-c', 'Release', "--property:ManiacalVersion=$version", '--',
        $Game, '600', (Join-Path $artifacts 'console.png'))
    $n = 1
    foreach ($more in $MoreGames) {
        Run dotnet @('run', '--project', 'ConsoleSmoke', '-c', 'Release', '--no-build', "--property:ManiacalVersion=$version", '--',
            $more, '600', (Join-Path $artifacts "console$n.png"))
        $n++
    }

    Step 'Maniacal as a dotnet tool'
    Run dotnet @('tool', 'install', 'Maniacal', '--tool-path', $toolDir, '--version', $version, '--add-source', $feed)
    $maniacal = Join-Path $toolDir ($IsWindows ? 'maniacal.exe' : 'maniacal')
    Run $maniacal @($Game, '--list')
    Run $maniacal @($Game, '--run', '600', '--screenshot', (Join-Path $artifacts 'tool.png'))
}
finally {
    Pop-Location
}

Step 'Done'
Write-Host "packages in $feed; screenshots in $artifacts"
