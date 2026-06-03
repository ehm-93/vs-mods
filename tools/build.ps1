#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build script for vs-mods monorepo.

.DESCRIPTION
    Builds, packages, and installs Vintage Story mods organized by domain.

.PARAMETER Target
    The build target: build, package, install, clean, list

.PARAMETER Domain
    Optional domain filter (farming, primitive, etc). Empty = all domains.

.PARAMETER Mod
    Optional mod filter. Requires -Domain to be set.

.PARAMETER Configuration
    Build configuration: Debug or Release. Default: Release

.PARAMETER Force
    Re-fetch optional dependencies even if already present (re-downloads cached zips
    and re-extracts). Without this, deps are fetched only when missing.

.EXAMPLE
    ./build.ps1 list
    ./build.ps1 build
    ./build.ps1 build -Domain farming
    ./build.ps1 build -Force
    ./build.ps1 package -Domain farming -Mod weeds
    ./build.ps1 install -Domain primitive -Mod thermal-fracturing
#>

param(
    [Parameter(Position=0)]
    [ValidateSet("build", "package", "install", "clean", "list")]
    [string]$Target = "build",

    [string]$Domain = "",
    [string]$Mod = "",
    [string]$Configuration = "Release",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent
$SrcRoot = Join-Path $RepoRoot "src"

# Shared cache for downloaded dependency-mod zips (gitignored). Each dep is extracted
# per-mod into deps/<modid>/ for compile-time references; the zips are reused for install.
$DepCacheDir = Join-Path $RepoRoot ".depcache"
$ModDbApiBase = "https://mods.vintagestory.at/api/mod"
$script:DepZipCache = @{}   # "modid@version" -> cached zip path (memoized per run)
$script:DepInfoCache = @{}  # dep zip path -> its external deps (memoized per run)

# Discover all domains (directories under src/ containing mods with modinfo.json)
function Get-Domains {
    Get-ChildItem $SrcRoot -Directory |
        Where-Object {

            (Get-ChildItem $_.FullName -Directory | Where-Object { Test-Path "$($_.FullName)\modinfo.json" }).Count -gt 0
        } |
        Select-Object -ExpandProperty Name
}

# Discover mods in a domain
function Get-ModsInDomain($domainName) {
    $domainPath = Join-Path $SrcRoot $domainName
    Get-ChildItem $domainPath -Directory |
        Where-Object { Test-Path "$($_.FullName)\modinfo.json" } |
        Select-Object -ExpandProperty Name
}

# Get mod metadata
function Get-ModInfo($domainName, $modName) {
    $modPath = Join-Path $SrcRoot $domainName $modName
    $infoPath = Join-Path $modPath "modinfo.json"

    if (-not (Test-Path $infoPath)) {
        throw "modinfo.json not found: $infoPath"
    }

    $info = Get-Content $infoPath -Raw | ConvertFrom-Json
    $csprojPath = Join-Path $modPath "$modName.csproj"

    return @{
        Domain = $domainName
        Name = $modName
        ModId = $info.modid
        Version = $info.version
        Type = $info.type
        Path = $modPath
        CsprojPath = $csprojPath
        HasCode = Test-Path $csprojPath
        Dependencies = $info.dependencies
        OptionalDependencies = $info.optionalDependencies
    }
}

# --- Dependencies (driven by each modinfo's dependencies / optionalDependencies) ---

# All mod ids defined in this repo, lowercased. Used to skip internal deps (built locally,
# not fetched from the ModDB) when resolving a mod's dependency list.
function Get-LocalModIds {
    $ids = @{}
    foreach ($domain in (Get-Domains)) {
        foreach ($modName in (Get-ModsInDomain $domain)) {
            $id = (Get-ModInfo $domain $modName).ModId
            if ($id) { $ids[$id.ToString().ToLower()] = $true }
        }
    }
    return $ids
}

# External (ModDB) dependencies for a mod: every entry in dependencies + optionalDependencies
# except 'game' and any mod id defined in this repo. Returns objects with ModId + Version.
# Tolerant of modinfo shape — `dependencies` may be an object {modid:version}, an array of bare
# modids, or a single modid string; a value may itself be an object carrying a .version member.
function Get-ExternalDeps($mod, $localModIds) {
    $deps = @()
    foreach ($section in @($mod.Dependencies, $mod.OptionalDependencies)) {
        if (-not $section) { continue }

        # Normalise the section into a list of @{ Id; Version } regardless of its JSON shape.
        # (A bare array/string carries no versions, so those default to '' = latest.)
        $pairs = @()
        if ($section -is [string]) {
            $pairs += @{ Id = $section; Version = '' }
        }
        elseif ($section -is [System.Collections.IEnumerable]) {
            foreach ($el in $section) { $pairs += @{ Id = [string]$el; Version = '' } }
        }
        else {
            foreach ($prop in $section.PSObject.Properties) {
                $v = if ($prop.Value -is [System.Management.Automation.PSCustomObject]) {
                    [string]$prop.Value.version
                } else { [string]$prop.Value }
                $pairs += @{ Id = $prop.Name; Version = $v }
            }
        }

        foreach ($p in $pairs) {
            $depId = ([string]$p.Id).Trim()
            if ([string]::IsNullOrWhiteSpace($depId)) { continue }
            if ($depId -eq 'game') { continue }
            if ($localModIds.ContainsKey($depId.ToLower())) { continue }
            $deps += [pscustomobject]@{ ModId = $depId; Version = $p.Version }
        }
    }
    return $deps
}

# Resolve a ModDB release for modid@version. Empty or '*' version = latest release.
function Resolve-DepRelease($modid, $version) {
    $resp = Invoke-RestMethod -Uri "$ModDbApiBase/$modid" -ErrorAction Stop
    if ($resp.statuscode -and $resp.statuscode -ne "200") {
        throw "ModDB returned status $($resp.statuscode) for '$modid'"
    }
    $releases = $resp.mod.releases
    if (-not $releases) { throw "ModDB has no releases for '$modid'" }

    if ($version -and $version -ne '*') {
        $release = $releases | Where-Object { $_.modversion -eq $version } | Select-Object -First 1
        if (-not $release) {
            $recent = ($releases | Select-Object -First 5 | ForEach-Object { $_.modversion }) -join ", "
            throw "No release of '$modid' for version '$version' (recent: $recent)"
        }
        return $release
    }
    return ($releases | Select-Object -First 1)  # latest
}

# Ensure the dep's zip is in the shared cache; return its path. Memoized per run.
function Get-DepZip($modid, $version) {
    $key = "$modid@$version"
    if (-not $Force -and $script:DepZipCache.ContainsKey($key)) {
        return $script:DepZipCache[$key]
    }

    New-Item -ItemType Directory -Force -Path $DepCacheDir | Out-Null
    $release = Resolve-DepRelease $modid $version
    $zip = Join-Path $DepCacheDir $release.filename
    if ((Test-Path $zip) -and -not $Force) {
        Write-Host "      = cached $($release.filename)" -ForegroundColor DarkGray
    }
    else {
        Write-Host "      > $($release.filename)" -ForegroundColor Gray
        Invoke-WebRequest -Uri $release.mainfile -OutFile $zip
    }

    $script:DepZipCache[$key] = $zip
    return $zip
}

# Read a fetched dependency mod's bundled modinfo.json (from inside its zip) and return its
# external REQUIRED dependencies (its own 'dependencies' minus 'game' and repo-local mods).
# A dependency's *optional* dependencies are not followed. Parse failures are non-fatal.
function Get-DepsFromZip($zipPath, $localModIds) {
    if ($script:DepInfoCache.ContainsKey($zipPath)) { return $script:DepInfoCache[$zipPath] }

    $raw = $null
    Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        # Prefer a root-level modinfo.json (shortest path) over any nested one.
        $entry = $archive.Entries |
            Where-Object { $_.Name -ieq 'modinfo.json' } |
            Sort-Object { $_.FullName.Length } |
            Select-Object -First 1
        if ($entry) {
            $reader = [System.IO.StreamReader]::new($entry.Open())
            try { $raw = $reader.ReadToEnd() } finally { $reader.Dispose() }
        }
    }
    finally { $archive.Dispose() }

    $result = @()
    if ($raw) {
        try {
            $info = $raw | ConvertFrom-Json
            $result = @(Get-ExternalDeps @{ Dependencies = $info.dependencies; OptionalDependencies = $null } $localModIds)
        }
        catch {
            Write-Host "      ! couldn't parse modinfo in $(Split-Path $zipPath -Leaf); skipping its transitive deps" -ForegroundColor DarkYellow
        }
    }

    $script:DepInfoCache[$zipPath] = $result
    return $result
}

# Full transitive closure of external (ModDB) dependencies, starting from a set of root deps.
# Fetches each (downloads its zip), then reads that zip's modinfo for sub-dependencies. BFS with
# a visited set, so cycles and diamonds terminate. Deduped by mod id: the first-resolved version
# wins; a differing version reached later is skipped with a warning. Returns @{ModId;Version;Zip}.
function Get-TransitiveDeps($rootDeps, $localModIds) {
    $resolved = [ordered]@{}
    $queue = [System.Collections.Generic.Queue[object]]::new()
    foreach ($d in $rootDeps) { $queue.Enqueue($d) }

    while ($queue.Count -gt 0) {
        $dep = $queue.Dequeue()
        $key = ([string]$dep.ModId).Trim().ToLower()
        if (-not $key) { continue }
        if ($resolved.Contains($key)) {
            if ($resolved[$key].Version -ne $dep.Version) {
                Write-Host "      ! version conflict for '$($dep.ModId)': keeping $($resolved[$key].Version), ignoring $($dep.Version)" -ForegroundColor DarkYellow
            }
            continue
        }

        $zip = Get-DepZip $dep.ModId $dep.Version
        $resolved[$key] = [pscustomobject]@{ ModId = $dep.ModId; Version = $dep.Version; Zip = $zip }

        foreach ($sub in (Get-DepsFromZip $zip $localModIds)) {
            if (-not $resolved.Contains($sub.ModId.ToLower())) { $queue.Enqueue($sub) }
        }
    }

    return $resolved.Values
}

# Fetch a mod's full transitive external-dependency closure (from its modinfo) into deps/<modid>/
# for compile-time references — transitive so a dep-of-a-dep's DLL is present too. Skips deps
# already extracted and up to date (modinfo not newer) unless -Force.
function Invoke-FetchDeps($mod, $localModIds) {
    $rootDeps = @(Get-ExternalDeps $mod $localModIds)
    if ($rootDeps.Count -eq 0) { return }
    $closure = @(Get-TransitiveDeps $rootDeps $localModIds)

    $depsDir = Join-Path $mod.Path "deps"
    New-Item -ItemType Directory -Force -Path $depsDir | Out-Null
    $modinfoTime = (Get-Item (Join-Path $mod.Path "modinfo.json")).LastWriteTime

    foreach ($dep in $closure) {
        $target = Join-Path $depsDir $dep.ModId
        $present = (Test-Path $target) -and (Get-ChildItem $target -Force -ErrorAction SilentlyContinue)
        # Offline-fast skip: present (non-empty) and modinfo hasn't changed since extraction.
        if (-not $Force -and $present -and $modinfoTime -le (Get-Item $target).LastWriteTime) {
            continue
        }

        Write-Host "    dep $($dep.ModId)@$($dep.Version)" -ForegroundColor Gray
        if (Test-Path $target) { Remove-Item -Recurse -Force $target }
        Expand-Archive -Path $dep.Zip -DestinationPath $target -Force
    }
}

# Determine what to build based on filters
function Get-BuildTargets {
    $targets = @()

    if ($Mod -and $Domain) {
        # Single mod
        $targets += Get-ModInfo $Domain $Mod
    }
    elseif ($Domain) {
        # All mods in domain
        foreach ($mod in (Get-ModsInDomain $Domain)) {
            $targets += Get-ModInfo $Domain $mod
        }
    }
    else {
        # All mods in all domains
        foreach ($domain in (Get-Domains)) {
            foreach ($mod in (Get-ModsInDomain $domain)) {
                $targets += Get-ModInfo $domain $mod
            }
        }
    }

    return ,$targets  # leading comma stops PowerShell unwrapping a 1-element array to a scalar
}

function Invoke-Build {
    $targets = Get-BuildTargets
    $codeTargets = @($targets | Where-Object { $_.HasCode })

    if ($codeTargets.Count -eq 0) {
        Write-Host "No code mods to build" -ForegroundColor Yellow
        return
    }

    Write-Host "Building $($codeTargets.Count) mod(s)..." -ForegroundColor Cyan

    $localModIds = Get-LocalModIds
    foreach ($mod in $codeTargets) {
        Write-Host "  $($mod.Domain)/$($mod.Name)" -ForegroundColor Gray
        Invoke-FetchDeps $mod $localModIds
        dotnet build $mod.CsprojPath -c $Configuration --nologo -v q
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed for $($mod.Domain)/$($mod.Name)"
        }
    }

    Write-Host "Build complete!" -ForegroundColor Green
}

function Invoke-Package {
    Invoke-Build

    $targets = Get-BuildTargets
    $releaseDir = Join-Path $RepoRoot "releases"
    New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

    Write-Host "Packaging $($targets.Count) mod(s)..." -ForegroundColor Cyan

    foreach ($mod in $targets) {
        $zipName = "$($mod.ModId)_$($mod.Version).zip"
        $zipPath = Join-Path $releaseDir $zipName
        $staging = Join-Path $releaseDir "staging_$($mod.ModId)"

        # Clean staging
        if (Test-Path $staging) {
            Remove-Item -Recurse -Force $staging
        }
        New-Item -ItemType Directory -Force -Path $staging | Out-Null

        # Copy DLL if code mod
        if ($mod.HasCode) {
            $dllDir = Join-Path $RepoRoot "bin" $Configuration $mod.Name
            $dlls = Get-ChildItem $dllDir -Filter "*.dll" -ErrorAction SilentlyContinue
            foreach ($dll in $dlls) {
                Copy-Item $dll.FullName $staging/
            }
        }

        # Copy modinfo.json
        Copy-Item (Join-Path $mod.Path "modinfo.json") $staging/

        # Copy modicon.png if present (shown in the in-game mod manager)
        $iconPath = Join-Path $mod.Path "modicon.png"
        if (Test-Path $iconPath) {
            Copy-Item $iconPath $staging/
        }

        # Copy the repo LICENSE into every package
        $licensePath = Join-Path $RepoRoot "LICENSE"
        if (Test-Path $licensePath) {
            Copy-Item $licensePath $staging/
        }

        # Copy assets
        $assetsPath = Join-Path $mod.Path "assets"
        if (Test-Path $assetsPath) {
            Copy-Item -Recurse $assetsPath $staging/
        }

        # Remove old zip if exists
        if (Test-Path $zipPath) {
            Remove-Item $zipPath
        }

        # Create zip
        Compress-Archive -Path "$staging\*" -DestinationPath $zipPath -Force
        Remove-Item -Recurse -Force $staging

        Write-Host "  $zipName" -ForegroundColor Green
    }

    Write-Host "Packaging complete! Output: $releaseDir" -ForegroundColor Green
}

function Invoke-Install {
    Invoke-Package

    $vsDataPath = $env:VINTAGE_STORY_DATA
    if (-not $vsDataPath) {
        $vsDataPath = Join-Path $env:APPDATA "VintagestoryData"
    }
    $modsDir = Join-Path $vsDataPath "Mods"

    if (-not (Test-Path $modsDir)) {
        throw "Mods directory not found: $modsDir`nSet VINTAGE_STORY_DATA environment variable."
    }

    $targets = @(Get-BuildTargets)
    $localModIds = Get-LocalModIds

    # Resolve and download EVERYTHING to be deployed (the built target zips + the full transitive
    # closure of all their external deps) BEFORE touching the Mods folder, so a fetch failure can
    # never leave a wiped, half-installed folder. The closure is computed once over the union of
    # all targets' root deps, so a shared dep's version is chosen deterministically (first wins).
    $rootDeps = @()
    foreach ($mod in $targets) { $rootDeps += Get-ExternalDeps $mod $localModIds }
    $closure = @(Get-TransitiveDeps $rootDeps $localModIds)

    # Now it's safe to wipe and redeploy. Emptying clears the WHOLE folder (including any mods
    # added manually) so the deploy is exactly what this repo builds — a clean test instance.
    $existing = @(Get-ChildItem $modsDir -Force)
    if ($existing.Count -gt 0) {
        Write-Host "Emptying $modsDir ($($existing.Count) item(s))..." -ForegroundColor Yellow
        $existing | Remove-Item -Recurse -Force
    }

    Write-Host "Installing to $modsDir..." -ForegroundColor Cyan
    foreach ($mod in $targets) {
        $zipPath = Join-Path $RepoRoot "releases" "$($mod.ModId)_$($mod.Version).zip"
        Copy-Item $zipPath $modsDir/ -Force
        Write-Host "  $($mod.ModId)" -ForegroundColor Green
    }
    foreach ($dep in $closure) {
        Copy-Item $dep.Zip $modsDir/ -Force
        Write-Host "    + dep $($dep.ModId)@$($dep.Version)" -ForegroundColor DarkGray
    }

    Write-Host "Install complete!" -ForegroundColor Green
}

function Invoke-Clean {
    Write-Host "Cleaning..." -ForegroundColor Cyan

    $binDir = Join-Path $RepoRoot "bin"
    $releasesDir = Join-Path $RepoRoot "releases"

    if (Test-Path $binDir) { Remove-Item -Recurse -Force $binDir }
    if (Test-Path $releasesDir) { Remove-Item -Recurse -Force $releasesDir }

    # Clean obj directories
    Get-ChildItem $RepoRoot -Recurse -Directory -Filter "obj" |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host "Clean complete!" -ForegroundColor Green
}

function Invoke-List {
    Write-Host "`n=== vs-mods ===" -ForegroundColor Cyan

    $domains = Get-Domains
    if ($domains.Count -eq 0) {
        Write-Host "`n  No domains found. Create one with:" -ForegroundColor Yellow
        Write-Host "    ./tools/new-mod.ps1 -Domain farming -Name weeds" -ForegroundColor Gray
        return
    }

    foreach ($domain in $domains) {
        Write-Host "`n  src/$domain/" -ForegroundColor Yellow
        $mods = Get-ModsInDomain $domain

        if ($mods.Count -eq 0) {
            Write-Host "    (empty)" -ForegroundColor DarkGray
            continue
        }

        foreach ($mod in $mods) {
            $info = Get-ModInfo $domain $mod
            $type = if ($info.HasCode) { "code" } else { "content" }
            Write-Host "    $mod " -NoNewline -ForegroundColor White
            Write-Host "($($info.ModId) v$($info.Version)) " -NoNewline -ForegroundColor DarkGray
            Write-Host "[$type]" -ForegroundColor DarkCyan
        }
    }
    Write-Host ""
}

# Main (skipped when the script is dot-sourced, so individual functions can be tested)
if ($MyInvocation.InvocationName -ne '.') {
    switch ($Target.ToLower()) {
        "build"   { Invoke-Build }
        "package" { Invoke-Package }
        "install" { Invoke-Install }
        "clean"   { Invoke-Clean }
        "list"    { Invoke-List }
    }
}
