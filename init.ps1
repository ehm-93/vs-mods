#!/usr/bin/env pwsh
<#
.SYNOPSIS
    One-time initialization script for vs-mods monorepo.
    Run this after cloning to create solution files.
#>

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$SrcRoot = Join-Path $RepoRoot "src"

Write-Host "Initializing vs-mods monorepo..." -ForegroundColor Cyan

# Create root solution
Write-Host "`nCreating vs-mods.sln..." -ForegroundColor Yellow
dotnet new sln -n vs-mods -o $RepoRoot --force

# Create domain solutions
$domains = Get-ChildItem $SrcRoot -Directory
foreach ($domain in $domains) {
    Write-Host "Creating $($domain.Name).sln..." -ForegroundColor Yellow
    dotnet new sln -n $domain.Name -o $domain.FullName --force
}

# Find and add all .csproj files
Write-Host "`nDiscovering projects..." -ForegroundColor Yellow
$projects = Get-ChildItem $SrcRoot -Recurse -Filter "*.csproj" |
    Where-Object { $_.FullName -notmatch "\\(bin|obj|tools)\\" }

foreach ($proj in $projects) {
    Write-Host "  Found: $($proj.Name)" -ForegroundColor Gray

    # Add to root solution
    dotnet sln (Join-Path $RepoRoot "vs-mods.sln") add $proj.FullName 2>$null

    # Add to domain solution if applicable
    foreach ($domain in $domains) {
        if ($proj.FullName.StartsWith($domain.FullName)) {
            $domainSln = Join-Path $domain.FullName "$($domain.Name).sln"
            if (Test-Path $domainSln) {
                dotnet sln $domainSln add $proj.FullName 2>$null
            }
        }
    }
}

Write-Host "`nInitialization complete!" -ForegroundColor Green
Write-Host "`nNext steps:" -ForegroundColor Yellow
Write-Host "  1. Create your first mod:" -ForegroundColor Gray
Write-Host "     ./tools/new-mod.ps1 -Domain crops -Name weeds" -ForegroundColor White
Write-Host "  2. Build:" -ForegroundColor Gray
Write-Host "     ./tools/build.ps1 build" -ForegroundColor White
Write-Host "  3. List mods:" -ForegroundColor Gray
Write-Host "     ./tools/build.ps1 list" -ForegroundColor White
