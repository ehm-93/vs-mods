#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Scaffold a new mod in the vs-mods monorepo.

.PARAMETER Domain
    The domain to create the mod in (farming, primitive, etc).

.PARAMETER Name
    The mod name (use kebab-case, e.g., "thermal-fracturing").

.PARAMETER Type
    Mod type: "code" (default) or "content".

.EXAMPLE
    ./new-mod.ps1 -Domain farming -Name irrigation
    ./new-mod.ps1 -Domain farming -Name exotic-crops -Type content
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$Domain,

    [Parameter(Mandatory=$true)]
    [string]$Name,

    [ValidateSet("code", "content")]
    [string]$Type = "code"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent
$SrcRoot = Join-Path $RepoRoot "src"

$DomainPath = Join-Path $SrcRoot $Domain
$ModPath = Join-Path $DomainPath $Name

# Generate mod ID: domain + name without hyphens, lowercase
$ModId = "$Domain$($Name -replace '-','')".ToLower()

# Generate PascalCase name for C# classes
$PascalName = ($Name -split '-' | ForEach-Object {
    if ($_.Length -gt 0) { $_.Substring(0,1).ToUpper() + $_.Substring(1).ToLower() }
}) -join ''

# Generate PascalCase domain name for namespace
$PascalDomain = $Domain.Substring(0,1).ToUpper() + $Domain.Substring(1).ToLower()

# Validate
if (-not (Test-Path $DomainPath)) {
    Write-Host "Domain '$Domain' doesn't exist. Creating it..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path $DomainPath | Out-Null

    # Create domain-level Directory.Build.props
    $domainProps = @"
<Project>
  <!-- Import parent Directory.Build.props -->
  <Import Project="`$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '`$(MSBuildThisFileDirectory)../'))" />

  <!-- Domain-specific configuration for $Domain mods -->
  <PropertyGroup>
    <ModIdPrefix>$Domain</ModIdPrefix>
  </PropertyGroup>
</Project>
"@
    Set-Content -Path (Join-Path $DomainPath "Directory.Build.props") -Value $domainProps
    Write-Host "  Created src/$Domain/Directory.Build.props" -ForegroundColor Green
}

if (Test-Path $ModPath) {
    throw "Mod '$Name' already exists in $Domain"
}

Write-Host "Creating src/$Domain/$Name ($ModId)..." -ForegroundColor Cyan

# Create mod directory
New-Item -ItemType Directory -Force -Path $ModPath | Out-Null

# Create modinfo.json
$modInfo = [ordered]@{
    type = $Type
    modid = $ModId
    name = "$Domain/$Name"
    description = "TODO: Add description"
    authors = @("ehm-93")
    version = "0.1.0"
    dependencies = [ordered]@{
        game = "1.22.0"
    }
}

# Add common mod dependency for code mods (unless this IS the common mod)
if ($Type -eq "code" -and $Name -ne "common") {
    $commonModId = "${Domain}common"
    $modInfo.dependencies[$commonModId] = "*"
}

$modInfoJson = $modInfo | ConvertTo-Json -Depth 3
Set-Content -Path (Join-Path $ModPath "modinfo.json") -Value $modInfoJson
Write-Host "  Created modinfo.json" -ForegroundColor Green

# Create assets directory structure
$assetsPath = Join-Path $ModPath "assets" $ModId
New-Item -ItemType Directory -Force -Path (Join-Path $assetsPath "patches") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $assetsPath "lang") | Out-Null
Write-Host "  Created assets/$ModId/" -ForegroundColor Green

# Create lang file
$langContent = @{
    "$($ModId):modname" = "$Domain/$Name"
    "$($ModId):moddesc" = "TODO: Add description"
} | ConvertTo-Json
Set-Content -Path (Join-Path $assetsPath "lang" "en.json") -Value $langContent

if ($Type -eq "code") {
    # Create .csproj (inherits from Directory.Build.props; globs fetched dep DLLs)
    $csproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <!-- Inherits from root and domain Directory.Build.props -->
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <!-- Dependency DLLs fetched by tools/build.ps1 from this mod's modinfo
       (dependencies + optionalDependencies). Compile-only (Private=false):
       provided by the player's install, never bundled into our output. -->
  <ItemGroup>
    <DepAssembly Include="`$(MSBuildProjectDirectory)/deps/*/*.dll" />
  </ItemGroup>

  <Target Name="AddDepReferences" BeforeTargets="ResolveAssemblyReferences">
    <ItemGroup>
      <Reference Include="%(DepAssembly.Filename)">
        <HintPath>%(DepAssembly.FullPath)</HintPath>
        <Private>false</Private>
      </Reference>
    </ItemGroup>
  </Target>
</Project>
"@
    Set-Content -Path (Join-Path $ModPath "$Name.csproj") -Value $csproj
    Write-Host "  Created $Name.csproj" -ForegroundColor Green

    # Add reference to common mod (if this isn't the common mod)
    if ($Name -ne "common") {
        $commonPath = Join-Path $DomainPath "common" "common.csproj"
        if (Test-Path $commonPath) {
            $csprojPath = Join-Path $ModPath "$Name.csproj"
            dotnet add $csprojPath reference $commonPath 2>$null
            Write-Host "  Added reference to common mod" -ForegroundColor Green
        } else {
            Write-Host "  Warning: common mod doesn't exist yet. Create it with:" -ForegroundColor Yellow
            Write-Host "    ./tools/new-mod.ps1 -Domain $Domain -Name common" -ForegroundColor Gray
        }
    }

    # Create ModSystem
    $namespace = "Ehm93.VS.$PascalDomain.$PascalName"
    $modSystem = @"
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Client;

namespace $namespace;

public class ${PascalName}ModSystem : ModSystem
{
    public const string ModId = "$ModId";

    public override void Start(ICoreAPI api)
    {
        api.Logger.Notification(`$"[{ModId}] Starting...");
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Logger.Notification(`$"[{ModId}] Server-side initialization");
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Logger.Notification(`$"[{ModId}] Client-side initialization");
    }
}
"@
    Set-Content -Path (Join-Path $ModPath "${PascalName}ModSystem.cs") -Value $modSystem
    Write-Host "  Created ${PascalName}ModSystem.cs" -ForegroundColor Green

    # Add to domain solution if it exists
    $slnPath = Join-Path $DomainPath "$Domain.sln"
    if (Test-Path $slnPath) {
        $csprojPath = Join-Path $ModPath "$Name.csproj"
        dotnet sln $slnPath add $csprojPath 2>$null
        Write-Host "  Added to $Domain.sln" -ForegroundColor Green
    }

    # Add to root solution if it exists
    $rootSlnPath = Join-Path $RepoRoot "vs-mods.sln"
    if (Test-Path $rootSlnPath) {
        $csprojPath = Join-Path $ModPath "$Name.csproj"
        dotnet sln $rootSlnPath add $csprojPath 2>$null
        Write-Host "  Added to vs-mods.sln" -ForegroundColor Green
    }
}

Write-Host "`nCreated: $ModPath" -ForegroundColor Green
Write-Host "`nNext steps:" -ForegroundColor Yellow
Write-Host "  1. Edit modinfo.json with proper name/description" -ForegroundColor Gray
Write-Host "  2. Add your code to ${PascalName}ModSystem.cs" -ForegroundColor Gray
Write-Host "  3. Add JSON patches in assets/$ModId/patches/" -ForegroundColor Gray
Write-Host "  4. Build: ./tools/build.ps1 build -Domain $Domain -Mod $Name" -ForegroundColor Gray
Write-Host "  5. Test:  ./tools/build.ps1 install -Domain $Domain -Mod $Name" -ForegroundColor Gray
