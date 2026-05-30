#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fetch optional dependency mods from the Vintage Story ModDB.

.DESCRIPTION
    For each "modid@version" below, queries the ModDB API
    (https://mods.vintagestory.at/api/mod/<modid>), finds the release whose
    modversion matches, downloads its zip into .cache/, then extracts it to
    deps/<modid>/. The extracted dir is deleted and re-extracted every run;
    cached zips are reused unless -Force is set.

.PARAMETER Force
    Re-download cached zips even if they already exist.
#>

param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$DepsDir = $PSScriptRoot
$CacheDir = Join-Path $DepsDir ".cache"
$ApiBase = "https://mods.vintagestory.at/api/mod"

# optional deps: modid@version
$Deps = @(
    "primitivesurvival@5.0.5"
    "expandedfoods@2.0.0-dev.8"
    "aculinaryartillery@2.0.0-dev.15"
    "butchering@1.13.4"
)

New-Item -ItemType Directory -Force -Path $CacheDir | Out-Null

$failed = @()

foreach ($dep in $Deps) {
    $modid, $version = $dep -split "@", 2
    Write-Host "$modid@$version" -ForegroundColor Cyan

    try {
        $resp = Invoke-RestMethod -Uri "$ApiBase/$modid" -ErrorAction Stop
    }
    catch {
        Write-Host "  ! API request failed: $($_.Exception.Message)" -ForegroundColor Red
        $failed += $dep
        continue
    }

    if ($resp.statuscode -and $resp.statuscode -ne "200") {
        Write-Host "  ! ModDB returned status $($resp.statuscode)" -ForegroundColor Red
        $failed += $dep
        continue
    }

    $release = $resp.mod.releases | Where-Object { $_.modversion -eq $version } | Select-Object -First 1
    if (-not $release) {
        $available = ($resp.mod.releases | Select-Object -First 5 | ForEach-Object { $_.modversion }) -join ", "
        Write-Host "  ! No release for version $version (recent: $available)" -ForegroundColor Red
        $failed += $dep
        continue
    }

    # Download to cache (reused across runs unless -Force)
    $zip = Join-Path $CacheDir $release.filename
    if ((Test-Path $zip) -and -not $Force) {
        Write-Host "  = cached $($release.filename)" -ForegroundColor DarkGray
    }
    else {
        Write-Host "  > $($release.filename)" -ForegroundColor Gray
        Invoke-WebRequest -Uri $release.mainfile -OutFile $zip
    }

    # Always delete and re-extract to deps/<modid>/
    $target = Join-Path $DepsDir $modid
    if (Test-Path $target) {
        Remove-Item -Recurse -Force $target
    }
    Expand-Archive -Path $zip -DestinationPath $target -Force
    Write-Host "  + extracted to $modid/" -ForegroundColor Green
}

if ($failed.Count -gt 0) {
    throw "Failed to fetch $($failed.Count) of $($Deps.Count) dependencies: $($failed -join ', ')"
}

Write-Host "Fetch complete." -ForegroundColor Green
