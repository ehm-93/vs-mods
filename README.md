# vs-mods

Monorepo for Vintage Story mods by ehm-93.

## Structure

```
vs-mods/
├── src/
│   ├── crops/            # Crop mechanics (weeds, crop quality, etc.)
│   │   ├── common/       # Common code shared across crops mods
│   │   └── weeds/        # Weeds affecting crop growth
│   ├── worldgen/         # World generation (caves, etc.)
│   │   ├── common/       # Common code shared across worldgen mods
│   │   └── caves/        # Multi-tier cave generation system
│   ├── primitive/        # Primitive survival (thermal fracturing, etc.)
│   │   └── thermal-fracturing/
├── tools/                # Build and scaffolding scripts
│   ├── build.ps1         # Main build script
│   └── new-mod.ps1       # Scaffold new mods
├── bin/                  # Build output (gitignored)
└── releases/             # Packaged mods (gitignored)
```

## Quick Start

```powershell
# Initialize solution files (run once after clone)
./init.ps1

# List all mods
./tools/build.ps1 list

# Create a new mod
./tools/new-mod.ps1 -Domain farming -Name irrigation

# Build everything
./tools/build.ps1 build

# Build one domain
./tools/build.ps1 build -Domain crops

# Build one mod
./tools/build.ps1 build -Domain crops -Mod weeds

# Visualize caves (quick testing)
cd src/worldgen/caves-visualizer
./viz.ps1 stats             # Show cave density by depth
./viz.ps1 connectivity      # Analyze grid connections
./viz.ps1 slice -Seed 99    # ASCII art horizontal slice
```

# Package for release
./tools/build.ps1 package -Domain crops

# Install to game for testing
./tools/build.ps1 install -Domain crops -Mod weeds
```

## Environment Setup

Set these environment variables:

```powershell
# Where Vintage Story is installed
$env:VINTAGE_STORY = "C:\Program Files\Vintage Story"

# Where your saves/mods/config live (optional, auto-detected on Windows)
$env:VINTAGE_STORY_DATA = "$env:APPDATA\VintagestoryData"
```

## Adding a New Mod

```powershell
# Code mod (C# + assets)
./tools/new-mod.ps1 -Domain crops -Name irrigation

# Content-only mod (just JSON assets)
./tools/new-mod.ps1 -Domain crops -Name exotic-crops -Type content
```

This creates:
```
src/crops/irrigation/
├── irrigation.csproj
├── modinfo.json
├── IrrigationModSystem.cs
└── assets/cropsirrigation/
    ├── patches/
    └── lang/en.json
```

The mod will automatically:
- Reference the domain's `common` mod (both as a project reference and mod dependency)
- Add the common mod to its `modinfo.json` dependencies

If the domain doesn't have a `common` mod yet, create it first:
```powershell
./tools/new-mod.ps1 -Domain crops -Name common
```

## Domains

| Domain | Description |
|--------|-------------|
| `crops` | Crop mechanics: weeds, crop quality, seasons |
| `worldgen` | World generation: caves, structures |
| `primitive` | Early-game survival: thermal fracturing, tallow candles |

## License

MIT-0
