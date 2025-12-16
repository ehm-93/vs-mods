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

.EXAMPLE
    ./build.ps1 list
    ./build.ps1 build
    ./build.ps1 build -Domain farming
    ./build.ps1 package -Domain farming -Mod weeds
    ./build.ps1 install -Domain primitive -Mod thermal-fracturing
#>

param(
    [Parameter(Position=0)]
    [ValidateSet("build", "package", "install", "clean", "list")]
    [string]$Target = "build",
    
    [string]$Domain = "",
    [string]$Mod = "",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent
$SrcRoot = Join-Path $RepoRoot "src"

# Discover all domains (directories under src/ containing mods with modinfo.json)
function Get-Domains {
    Get-ChildItem $SrcRoot -Directory | 
        Where-Object { 
            $_.Name -ne "shared" -and
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
    
    return $targets
}

function Invoke-Build {
    $targets = Get-BuildTargets
    $codeTargets = $targets | Where-Object { $_.HasCode }
    
    if ($codeTargets.Count -eq 0) {
        Write-Host "No code mods to build" -ForegroundColor Yellow
        return
    }
    
    Write-Host "Building $($codeTargets.Count) mod(s)..." -ForegroundColor Cyan
    
    foreach ($mod in $codeTargets) {
        Write-Host "  $($mod.Domain)/$($mod.Name)" -ForegroundColor Gray
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
    
    $targets = Get-BuildTargets
    Write-Host "Installing to $modsDir..." -ForegroundColor Cyan
    
    foreach ($mod in $targets) {
        $zipName = "$($mod.ModId)_$($mod.Version).zip"
        $zipPath = Join-Path $RepoRoot "releases" $zipName
        Copy-Item $zipPath $modsDir/ -Force
        Write-Host "  $($mod.ModId)" -ForegroundColor Green
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

# Main
switch ($Target.ToLower()) {
    "build"   { Invoke-Build }
    "package" { Invoke-Package }
    "install" { Invoke-Install }
    "clean"   { Invoke-Clean }
    "list"    { Invoke-List }
}
