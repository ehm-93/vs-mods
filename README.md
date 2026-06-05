# vs-mods

Monorepo for [Vintage Story](https://www.vintagestory.at/) mods by ehm-93 (Emmett_chef).

Mods are grouped into **domains** (the folders under `src/`). A single build
script compiles, packages, and installs any subset, and fetches each mod's external
dependencies automatically.

## Layout

```
vs-mods/
├── src/<Domain>/<Mod>/   # one folder per mod, grouped into PascalCase domains
├── tools/                # build.ps1, new-mod.ps1, bench/
├── docs/ideas/           # design backlog (easy / medium / hard)
├── deps/, .depcache/     # fetched dependencies
└── bin/, releases/       # build & package output
```

Each mod lives in `src/<Domain>/<Mod>/` with its own `modinfo.json` (name, description,
mod ID, dependencies). For the current set of domains and mods, run `./tools/build.ps1 list`
rather than relying on a list here.

## Quick Start

```powershell
# Initialize solution files (run once after clone)
./init.ps1

# List all domains and mods
./tools/build.ps1 list

# Build everything (auto-fetches missing dependencies)
./tools/build.ps1 build

# Build one domain
./tools/build.ps1 build -Domain Crops

# Build one mod
./tools/build.ps1 build -Domain Primitive -Mod Pemmican

# Package a domain (or mod) into releases/
./tools/build.ps1 package -Domain Crops

# Install to the game's Mods folder for testing
./tools/build.ps1 install -Domain Primitive -Mod Pemmican
```

`-Domain` takes the PascalCase folder name; `-Mod` requires `-Domain`. Other targets are
`clean` and `list`. Add `-Configuration Debug` for a debug build (default is `Release`), and
`-Force` to re-fetch dependencies even when they're already present.

`install` leaves the Mods folder intact and only replaces the mods (and deps) it deploys, so
manually-added mods survive. Add `-Clean` to **empty the entire VS Mods folder first** for a
pristine test instance:

```powershell
./tools/build.ps1 install -Clean
```

## Environment Setup

Set these environment variables:

```powershell
# Where Vintage Story is installed
$env:VINTAGE_STORY = "C:\Program Files\Vintage Story"

# Where your saves/mods/config live (optional, auto-detected on Windows)
$env:VINTAGE_STORY_DATA = "$env:APPDATA\VintagestoryData"
```

## Dependencies

External dependency mods are declared per mod in `modinfo.json` under `dependencies`
(required) and `optionalDependencies` (compat, not required at runtime).

`tools/build.ps1` is the single source for resolving them. It reads both maps, skips `game`
and any mod defined in this repo, and fetches the rest **transitively** from the
[VS ModDB](https://mods.vintagestory.at/). `install` copies the whole transitive closure into
the Mods folder so installed mods can actually load.

## Adding a New Mod

```powershell
# Code mod (C# + assets)
./tools/new-mod.ps1 -Domain Crops -Name Irrigation

# Content-only mod (just JSON assets)
./tools/new-mod.ps1 -Domain Crops -Name ExoticCrops -Type content
```

## License

[MIT-0](LICENSE) (MIT No Attribution).
