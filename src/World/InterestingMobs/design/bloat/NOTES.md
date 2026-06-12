# Bloat — design notes

Walking-bomb stalker (see `docs/ideas/hard/interesting-enemies.md`). Model concept: an
engorged tick — the rust mass IS the creature, wearing a drifter as transport and food.

## Decisions (locked with Emmett, 2026-06-11)

- **Silhouette:** gorged-tick. Mass peaks over the hindquarters (one lobe is parented to
  the Abdomen bone so the rear carries/drags it through leg animations). Host limbs, neck
  and head are scaled down per tier — the drifter shrinks as the mass grows.
- **Crawl-only locomotion.** Too heavy to walk upright. Entity JSON should only reference
  the `crawl*` animations (crawlidle / crawlwalk / crawlrun / crawlattack / crawlhurt /
  crawldie / crawlstun). Stand keyframes still exist in the shape files — harmless, unused.
- **Palette:** EXACT vanilla colors, extracted from the host sheets with the shapes-MCP
  `palette_extract` tool (spiked1 dominants: #84725a/#736551/#95856b flesh, #2a2119 grime,
  #482b12/#6b390e/#935c26 muted oxide). Sac sits a half-step darker than the host but in
  the same family. Sac flesh has NO glow; growths have no glow yet either. Glow returns
  later as the FUSE telegraph (texture swap / glow pulse).
- **Rust corruption:** papillomas in VANILLA's own spike language — modeled on the tainted
  drifter's "metal in chest/back" protrusions (razor-thin ~0.5-0.75 cross-section, 3-5
  chained segments, violent kinks ±25-65°) and textured to the SAME metal-spike texel
  strip on the spiked1 sheet (texture key `spike` → `entity/lore/drifter/spiked1`, UV
  [12,30,12.5,32] in 64-space). Plus flat rust scab plates. Seeded-RNG generated.
- **The lump is a PARASITE, not a tumor** — tick-accurate anatomy (ref: engorged-tick photo):
  - **Head:** small near-BLACK capitulum (`CapHead`) clamped directly against the host's
    nape — buried at the mass/host junction, not protruding. Two black palps (`PalpL/R`)
    reach down onto the host's back. Future fuse-anim hook: palps could visibly pump.
  - **Legs:** 4 spindly black 4-segment legs (coxa→femur→tibia→tarsus, tapering), held
    TIGHT against the host — minimal splay, fast down-curl so the tips press into the
    body: pair 1 reaches forward OVER the host's shoulders and curls against the chest,
    pair 2 wraps the torso flanks. All anchored by the head. Named `Leg{L,R}{1,2}s{0..3}`
    — separate elements, so they can get their own twitch/grip animations later.
  - **Papillomas:** `Spur{01..NN}s{0..3}` chains, anchored on the dome/rear/flanks.
- **Three tiers, underground only** (never surface; weak variant may appear shallow).
  Higher tiers ride higher-tier drifter hosts (vanilla: tainted=spiked1, corrupt=deerhorn,
  nightmare=knife — held in reserve for a possible 4th tier or boss):
  | tier | shape | host shape | papillomas | plates | host consumption | suggested entity scale |
  |---|---|---|---|---|---|---|
  | bloat | `bloat-small.json` | surface | 5 | 0 | limbs 0.75, neck 0.85, stoop 22° | ~0.8 |
  | large bloat | `bloat-large.json` | spiked1 (tainted) | 8 | 2 | limbs 0.6, neck 0.75, stoop 26° | ~1.1 |
  | massive bloat | `bloat-massive.json` | deerhorn (corrupt) | 12 | 3 | limbs 0.5, neck 0.6, stoop 30° | ~1.4 |
  All tiers have 4 legs + head; legs/growths get longer and chunkier with tier scale.
  Spawn depth gates the tier (shallow → small only; deep → large/massive). Note: spiked1
  ships with 2 vanilla duplicate-element-name errors and the spiked/deerhorn shapes have
  `#null` texture refs on `enabled:false` faces — both harmless (engine drops them), the
  render footnote "missing tex: null" is cosmetic.

## How the model is built — IMPORTANT

**`gen-bloat.mjs` is the source of truth.** It reads the VANILLA host drifter shapes from
the game install and emits the three tier jsons DIRECTLY INTO the asset tree at
`assets/worldinterestingmobs/shapes/entity/bloat/{small,large,massive}.json` (mass boxes,
stoop, limb shrink, plates, head, legs, papillomas, texture wiring — including pinning the
vanilla shape's unprefixed texture paths to `game:` so they don't mis-resolve into our
domain). Do not hand-edit the generated jsons — tweak the config tables and re-run:

    node gen-bloat.mjs

Deterministic per SEED. The shapes MCP is used as the review harness (shape_open + render
with `texturesRoot` pointed at `assets/worldinterestingmobs/textures`).

Texture conventions (`assets/worldinterestingmobs/textures/entity/bloat/`, declared via
`textureSizes` so shape UVs are in pixel space). Both textures are themselves GENERATED —
`gen-textures.mjs` prints paste_grid text (value-noise mottle, vignette curvature,
random-walk vein networks, corrosion strata); paste into the pixel editor and export:
- `sac.png` (32x32) — parasite flesh: deep-drifter palette mottle, edge vignette for fake
  curvature, branching oxide veins with bright eruption nodes. Mass faces get random
  0/90/180/270 UV rotation per face (in gen-bloat.mjs) so the boxes don't tile identically.
- `rust.png` (16x16) — corroded iron strata for growths/legs/head. Pixel region map: oxide
  mid [2,2,10,10] (spurs), plates [1,1,15,15], and px (13,13)-(15,15) is a reserved
  NEAR-BLACK block — the head/leg UV [13,13,16,16]. Keep that corner black when repainting.

## Files

- `gen-bloat.mjs` — shape generator (source of truth; writes into assets/).
- `gen-textures.mjs` — texture generator (prints paste_grid text + palettes).
- `bloat-{small,large,massive}.png` — crawlidle 4-view renders.
- Registered assets: `entities/bloat.json` (tier variantgroup small/large/massive;
  class EntityAgent for now; crawl-only client animations; client size 0.85/1.05/1.25;
  placeholder vanilla-style AI: meleeattack/seekentity/wander on crawl anims; underground
  spawnconditions — maxY 0.92/0.6/0.35, never surface; drifter sounds pitched down;
  gear-rusty/gear-temporal harvest drops) + lang entries (Bloat / Large bloat /
  Massive bloat). Builds clean via `tools/build.ps1 build -Domain World -Mod InterestingMobs`
  (csproj must be named `InterestingMobs.csproj` = folder name, or build.ps1 treats the
  mod as content-only).

## AI (implemented 2026-06-11, needs in-game testing)

**NO melee attack — the bloat's only weapon is the detonation.** Two custom tasks in
`Bloat/` (registered as `bloatstalk` / `bloatfuse` in the ModSystem, subclassing
`AiTaskBaseTargetable` — VS 1.22 note: AiTaskBase lives in Vintagestory.API.Common but
ships in VSEssentials.dll; legacy task codes still registered alongside the new `-r` ones):

- **bloatstalk** (priority 1.5): weeping-angel rules keyed on the player's view cone
  (~120° "on-screen" test; narrow ~25° cone drives the stared-at meter). Off-screen ->
  HUNTS: abandons the band and closes to `approachRange` (4) — running beyond maxRange,
  creeping inside — crossing the fuse trigger on the way in (the sneak-up kill).
  On-screen + LOS-blocked -> statue (no fidgeting; moving across gaps is what gets
  glimpsed). On-screen + visible -> slips into cover; stared at past 1600ms -> breaks for
  fresh cover at run speed. Commits to moves (no mid-path re-planning = no wiggle); the
  walk anim only plays while actually moving (base classes treadmill it for the whole
  task otherwise). Yields when `bloatArmed` is set. Has a `debug: true` task config flag
  that logs dist/align/LOS decisions to server-main.log (verified in-game 2026-06-11).
  HARD-WON GOTCHAS: (1) LOS rays MUST filter to collidable blocks —
  plain RayTraceForSelection counts grass tufts/snow layers as walls, so losOpen reads
  false ~always on natural terrain; (2) vanilla CanSensePlayer returns false in
  creative/spectator — behavior tests need survival mode (damage-arming still works in
  creative, which masks the difference); (3) view-vector math verified correct: align
  +1.0 = staring at it.
- **bloatfuse** (priority 5, priorityForCancel 10 = nothing interrupts it): arms on player
  within triggerRange (4/5/6 by tier, through walls) OR any damage (OnEntityHurt). Once
  armed there is NO disarm — sprints at the nearest player (crawlrun anim, repeating
  drifter-aggro shriek every 600ms), and after fuseMs (2.6/3.2/4.2s) detonates:
  `CreateExplosion` (destruction 1.5/2.5/3.5, injure 3.5/5/7) plus a temporal rupture —
  OwnStability drain on players within 2x injure radius (0.05/0.1/0.18, distance falloff).
  Dies with EnumDespawnReason.Removed (no corpse — it's gone). Death before the timer is
  the only cancel.

## Next steps

1. In-game test: `/entity spawn worldinterestingmobs:bloat-small` — shapes/anims load,
   per-element scaleX/Z renders, stalk keeps cover believably (tune coverSamples/band),
   fuse triggers/explodes, stability drain felt. Watch server-main.log for task errors.
2. Fuse visuals: glow pulse on sac + rust while armed (client can read `bloatArmed`
   watched attribute), palp pumping anim, dedicated rising-shriek sound.
3. Tuning: speeds, fuse times, blast radii, spawn chance/caps once seen in-world.
4. Later: rupture spawns drifters / leaves a stability-drain zone.
