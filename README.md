# vs-mods

Monorepo for Vintage Story mods by ehm-93.

## Structure

```
vs-mods/
├── src/
│   ├── farming/          # Farming mechanics (weeds, blight, mulch, etc.)
│   │   ├── weeds/
│   │   ├── blight/
│   │   └── shared/       # Farming-specific shared code
│   ├── primitive/        # Primitive survival (thermal fracturing, etc.)
│   │   └── thermal-fracturing/
│   └── shared/           # Cross-domain shared code
│       └── VSModLib/
├── tools/                # Build and scaffolding scripts
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
./tools/build.ps1 build -Domain farming

# Build one mod
./tools/build.ps1 build -Domain farming -Mod weeds

# Package for release
./tools/build.ps1 package -Domain farming

# Install to game for testing
./tools/build.ps1 install -Domain farming -Mod weeds
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
./tools/new-mod.ps1 -Domain farming -Name irrigation

# Content-only mod (just JSON assets)
./tools/new-mod.ps1 -Domain farming -Name exotic-crops -Type content
```

This creates:
```
src/farming/irrigation/
├── irrigation.csproj
├── modinfo.json
├── IrrigationModSystem.cs
└── assets/farmingirrigation/
    ├── patches/
    └── lang/en.json
```

## Domains

| Domain | Description |
|--------|-------------|
| `farming` | Crop mechanics: weeds, blight, mulch, generations, vernalization |
| `primitive` | Early-game survival: thermal fracturing, tallow candles |

## License

MIT-0
