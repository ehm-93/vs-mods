# AGENTS.md

Things that aren't obvious from the code. Structure, build commands, env vars, and
scaffolding live in the [README](README.md) — this file is only the traps a human picks up
by working here but a bot won't guess.

## Building

- Build through [tools/build.ps1](tools/build.ps1) (`build` / `package` / `install` / `clean` /
  `list`), never raw `dotnet build`. It wires up the shared `Shared` lib (ILRepacked into each
  mod DLL), output paths, packaging, install, and dependency fetching. Filter with
  `-Domain <PascalCase folder> -Mod <mod folder>`, e.g. `-Domain Primitive -Mod Pemmican`.
- **A green build does not mean the mod works.** `build.ps1` only compiles C#. JSON assets,
  patches, recipes, lang, and textures are validated only when the *game* loads them — see below.
- Scripts are `.ps1`. PowerShell silently no-ops a `.ps` file (the extension matters).
- **`install` wipes the whole VS Mods folder first**, then deploys exactly the filtered repo mods +
  their fetched dependencies. The install target is treated as a clean test instance — don't keep
  unrelated/manually-added mods there, they'll be deleted on the next `install`.

## Telling whether a change actually worked

- Asset/patch/recipe errors surface **at game load, not at build**. After any asset change, run
  the game and read `%APPDATA%\VintagestoryData\Logs\` — `server-main.log` and `client-main.log`
  for `[Error]` / `[Warning]`. Failed patches, unresolved recipe ingredients, and missing
  textures/shapes all log here and nowhere else.
- A block interaction that "does nothing" with no feedback is usually a swallowed server-side
  exception — grep `server-main.log` for `[Error] Exception`.
- The vanilla install (`$VINTAGE_STORY`, i.e. `%APPDATA%\Vintagestory`) is the source of truth:
  copy asset formats from `assets/survival` & `assets/game`, read the API from
  `VintagestoryAPI.xml`, and reference vanilla assets with the `game:` domain prefix.

## Dependencies

- A mod declares its dependency mods in **modinfo.json**: `dependencies` (required) and
  `optionalDependencies` (compat, not required at runtime), each as `modid: version` (`*` = latest).
  `tools/build.ps1` is the single source — it reads both maps, skips `game` and any mod defined in
  this repo, and fetches the rest from the VS ModDB **transitively** (each fetched dep's own modinfo is
  read for its sub-dependencies, BFS-deduped): zips cached in `.depcache/`, the whole closure extracted
  per-mod to `<mod>/deps/<modid>/`. There are no per-mod `fetch.ps1` scripts anymore.
- Both `.depcache/` and `**/deps/` are **gitignored**, so a fresh checkout has no dep DLLs — `build.ps1`
  auto-fetches when they're missing (re-fetches on a modinfo change, or with `-Force`). The `.csproj`
  globs `deps/*/*.dll` as compile-only references (`Private=false` — never bundled into the mod).
- `build.ps1 install` copies the **full transitive closure** of external deps into the Mods folder, so an
  installed mod can actually load (e.g. a mod depending on ExpandedFoods also gets ACulinaryArtillery, which
  ExpandedFoods requires). The whole closure is resolved/downloaded *before* the Mods folder is emptied, so a
  fetch failure can't leave a half-installed folder.

## Asset JSON (this is where bots go wrong)

- Asset JSON is **strict JSON here**, even though VS itself accepts JSON5. No `//` comments, no
  trailing commas. Use `__comment` keys for notes (the repo convention) and keep them short.
- **Never start a file with a comment if anything patches it.** The JSON-patch loader parses the
  *target* file into a token tree, and a leading comment becomes the root →
  `Unable to cast JValue to JArray`, and the patch silently fails. (We keep everything strict, so
  this is moot in practice — but it's why.)
- `*` **is** a wildcard in `XxxByType` keys, including mid-code (e.g. `"smoked-*-redmeat"`).
  Vanilla and dependency mods rely on this — don't "correct" them to literal variant codes.
- Anything referencing **another mod's** assets (compat recipes/patches) must be gated with
  `"dependsOn": [{ "modid": "<other>" }]`, or it logs unresolved-asset warnings when that mod is
  absent. Recipes can't self-gate: make the recipe file a JSON array and *append* the
  foreign-item recipe through a `dependsOn` patch.
- Adding a variant to a `byType`-driven item means adding a `*-<variant>` entry to **every**
  relevant `byType` map (shape, textures, nutrition, gui). Miss one and the variant loads with a
  missing/magenta mesh — and, again, nothing fails at build.
- A mod's asset domain folder must equal its `modid`; if you rename one, rename every `domain:`
  reference too (recipes, shape bases, `Lang.Get`, `AssetLocation`) or things silently don't resolve.

## C#

- Don't reflection-load the net10 VS DLLs from PowerShell (System.Runtime mismatch). Confirm API
  signatures by compiling against them or reading `VintagestoryAPI.xml`.
- `ItemSlot.TakeOutWhole()` **throws** on an empty slot (it doesn't return null). Guard every call
  with `slot.Empty ? null : slot.TakeOutWhole()`.
