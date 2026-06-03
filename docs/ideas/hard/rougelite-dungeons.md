# Roguelite Dungeons - The Driftworks

A Vintage Story mod. Instanced pocket dimensions you enter as roguelite dungeon runs. You bring your real gear, the dungeon tries to kill you and take it, and you return to a persistent base between runs.

Influences, for feel: Path of Exile's Atlas for the modifier loop, Dark and Darker for the PvP extraction mode.

## The loop

Find the map device in the world. It is fixed, it cannot be crafted, and it sits inside a dungeon you clear once to reach it. From then on you travel back to it to run. At the device you socket a key and any modifiers you are carrying, weigh what you brought against what the modifiers will do to you, and open a run. You drop in, deal with the dungeon, and either leave with the loot or die and lose it. Modifiers and rewards drop inside, so runs partly fuel more runs, but you still have to live in the real world to keep the supply up.

## The stake

You carry your real inventory in. Death means you lose what you brought. That single rule is the tension the whole mod hangs on. The clock, the dark, and the hazards are frightening in proportion to what you walked in holding. Bring nothing and the dungeon is theater.

There is no restricted loadout and no safe stash inside. Considered and cut. The risk is the point.

## Two modes

A server runs one or the other, never both at once. The loot math differs enough that mixing them is a separate problem.

PvE. Each activation opens a private instance for you and your party. The device never locks, so the next person walks up and gets their own. Death loses your gear.

PvP, extraction. A handful of fixed devices feed one shared session. You drop in, find loot, and have to reach an extraction point to keep it. The extracts sit away from the entries on the worst ground, because the tension is in leaving, not arriving. Corpse looting is the economy. Without it you have PvP where every loss is wasted, not an extraction game.

## What scales

Not monster density. Vintage Story combat is slow melee and bow against a few enemies, and throwing more bodies at the player makes a clunky slog, not a power fantasy. Difficulty comes from pressure the engine already does well:

- Temporal instability increases over time while you are inside. The dungeon itself is the threat.
- The dungeon is dark. You manage your own light and visibility.
- The objective runs on a timer. Extract the loot under escalating spawns, not kill everything.

Modifiers turn these knobs up. They do not just multiply mob damage.

In PvP the stability clock doubles as the extraction timer. It already pushes people toward the exits as it runs down, which is exactly when you want fights to spike. 

## The map device

A (or many) fixed worldgen object, found, never crafted. It sits at the heart of a gate dungeon, an ordinary persistent worldgen structure with loot and an objective. Clear the gate once to reach the device, then travel back freely. Both the gate and the device are persistent. Neither needs any dimension tech, so they are your first playable content and they sidestep the riskiest engineering entirely.

The device stores nothing. Nobody owns it. You socket transiently at activation, the items are consumed, the run starts, and the block saves no inventory. The shared device never accumulates someone else's junk.

Every run opens with a trip, and Vintage Story travel is slow. Put the device on a translocator so players wire it into their base network. The long haul happens once on discovery. Re-runs are a step through a portal.

## Modifiers and keys

The key is a consumable item carrying a tier and a layout id. It proves a run is allowed to exist.

Modifiers are physical items and deliberately dumb. Their attributes are a list of knobs to turn and reward deltas, with no logic of their own. The device reads them at activation. Each one raises danger and reward together, which is the deal worth making. Types to build toward: pure risk for reward, theme or biome selectors, guaranteed encounter tokens, layout shapers. They drop inside dungeons and turn up in the overworld.

## Rewards

The hard rule: never drop something the player would otherwise mine. Mining is the actual game and it is more fun than the fight, so ore as a reward kills the loop. Rewards worth getting are things you cannot get, or cannot get fast, any other way. Rare temporal gear. Dungeon-only decor and blocks. Recipes and schematics. High-tier mob drops. The modifier items themselves, which is how the loop partly feeds itself.

## Progression

A light unlock layer. More modifier types and more sockets as you go. That is all.

No Atlas passive tree. It is hundreds of hours of balance work for depth the game does not need. The light version gets most of the feeling for a fraction of the cost.

## Core and content

The shape is a core mod plus content mods, the way VS Village does it. The core owns the machinery; content mods own the flavor, and nothing in the core knows or cares what flavors exist.

The core is the framework. The dimension lifecycle, the map device and its gate, the run controller and the loop, and the systems behind keys, modifiers, and rewards — expressed as registries and interfaces rather than concrete content. It ships no playable dungeon of its own beyond one reference tileset thin enough to prove the pipe end to end. Everything the core defines, it defines as an extension point.

A content mod fills those points. A roguelite content pack contributes a dungeon tileset of prefab rooms, a set of modifiers, drop and reward tables, custom entities and encounters, themes and biome selectors — keyed to a tier and a layout the core understands. Most of it is data. Code only where a flavor needs logic the data cannot express: a modifier that does something a knob list cannot, an entity with real behavior.

Compartmentalize the flavors. Each content pack is its own asset domain, self-contained and removable. The core degrades gracefully: an unknown tileset, modifier, or drop id is skipped, never fatal, so pulling a pack subtracts its flavor and leaves everything else standing. Two packs never reach into each other; they only register against the core.

This is also what makes external contribution realistic. The contract is the core's API plus the content schema, both documented and kept small and stable. A contributor writes a pack against the extension points without ever touching the core, the same way VS Village's addon packs never touch VS Village itself. It fits the repo we already have, where a mod's asset domain equals its modid and content references the core through the core's domain.

## Technical shape

A C# code mod, not a content pack. Dimension lifecycle, the device behavior, and the run controller all need code. The mod depends on Manifold (1.21+), a library built on the public engine API that exposes ephemeral custom dimensions with their own worldgen, transit events, and dimension-index recycling. We read its source: an ephemeral dimension releases its index back to a shared pool on teardown, so the number of runs over a save's lifetime is unbounded. What it does not do is delete the run's blocks from disk — and we are fine with that, see below.

The nouns:

- Gate dungeon. Persistent worldgen structure. Gets you to the device.
- Map device. Fixed worldgen block, uncraftable, stores no inventory.
- Dimension key. Consumable item, carries tier and layout.
- Modifier items. Dumb items, knobs and reward deltas.
- Run dimension. Ephemeral, registered through Manifold, built from prefab rooms on a seed.
- Reward table. Keyed off tier and which modifiers were socketed.

Two state objects carry the run. RunConfig is built once at activation and frozen: seed, tier, layout, knob values, reward profile. RunState is live: objective progress, timer, participants, spawn budget, exit reason. In PvP, RunState becomes SessionState and tracks several independent players and several exits at once.

The lifecycle, in order:

1. Player sockets a key and modifiers, activates the device.
2. Device validates the sockets and builds a RunConfig.
3. Mod asks Manifold for an ephemeral dimension seeded by that config.
4. Worldgen stitches prefab rooms into a sealed layout, pregenerated around the spawn.
5. Apply per-run state: stability drain, light, hazards, objective, spawn budget.
6. Open the portal, transit the player in, the run begins.
7. The controller tracks objective and clock while the run is live.
8. Exit fires on one of four conditions: objective met, death, timer out, or walking out. In PvP, reaching an extraction point is the win exit. Each maps to a different payout.
9. On a surviving exit, the player keeps the inventory carried in and rewards are added. On death the carried inventory is lost in PvE, or dropped as a lootable bag in PvP.
10. Tear the run down. Release the dimension index back to Manifold's pool. Leave the chunks on disk.

The real constraint, and what changed. This first read as a leak risk: the base engine never frees a dimension index, so opening and discarding dimensions all day would bloat the save forever. Digging into the engine and Manifold's source corrected both halves of that.

There is no cumulative allocator to exhaust. A top-level dimension is nothing but chunks sitting at a Y-band; the engine keeps no registry and no counter for them. The ten-bit dimension field caps the index *value* at 0 to 1023, not the number of dimensions you have ever used. Reusing an index costs nothing, and Manifold already recycles indices for ephemeral dimensions on teardown. Lifetime run count is a non-issue.

What is real is the *concurrent* ceiling. Manifold hands mods indices 10 through 1023, so at most ~1014 dimensions can be live at the same instant; raw, bypassing Manifold, you get ~1021 (3 through 1023). That bounds simultaneous active runs on a server, not total runs. It is the one number to design around, and it bites hardest in PvE, where every player can open a private instance — a busy server is the stress case, not a single grinder. First mitigation: release an index the moment a run empties. Second, if ~1014 live instances ever becomes a real wall, pack many runs into one dimension by giving each its own XZ region. A single dimension holds millions of XZ-separated cells — it is exactly how the engine tiles mini-dimensions into dimension 1 — so the index becomes a coarse isolation unit and XZ becomes the effectively-unlimited one. Not needed on day one, but it is the escape hatch, and it means 1023 is never a hard wall, only a tuning knob.

On disk we are deciding *not* to clean up. VS saves are large and growing them is not a problem we are solving. The only true purge means reflecting into the chunk thread's private database anyway — the public delete API only touches dimension 0 — and it is not worth it. Better: the orphaned chunks are an asset. A run you can walk back into is a debugging archive, and possibly a feature. Teardown frees the index and walks away from the blocks.

That decision has a catch with teeth. Because the blocks stay, a recycled index still holds the previous run's geometry at its old columns, and Manifold marks columns generated and never unmarks them — point a new run at a used column and worldgen is skipped, dropping the player into stale rooms. The fix is to never reuse a column: give every run a unique, monotonic XZ origin. The index is the concurrency lease, recycled and capped at ~1014 live; the XZ origin is per-run identity, unbounded, and it doubles as that run's address in the archive. Two runs never share ground, so nothing collides and nothing needs clearing. PvP changes none of this — a session is one dimension with one origin, freed when it empties.

Build order, riskiest first:

1. Stand up an empty ephemeral dimension that opens, lets you in, lets you out, and releases its index, triggered by a debug command. Prove the cycle: open and close it a thousand times, each at a fresh monotonic XZ origin, and confirm the live-index count returns to baseline and a recycled index generates fresh geometry instead of inheriting the last run's. Disk growth is expected and fine. Prove this before anything sits on it.
2. The gate dungeon and device, on a parallel track, since they need no dimension tech and give you something playable now.
3. Prefab worldgen for the run dimension.
4. The run controller and the objective.
5. Wire modifiers into RunConfig.
6. The reward table.

Past that point it is all fine-tuning.

## Open decisions

These define the shape and are not yet made.

- The PvP exit model. How extraction actually works is the thing the whole PvP loop turns on, and there is nothing on it yet beyond extracts sitting away from entries.
- Reaching the device. A one-time world event where the first clear opens it for everyone, or something each player earns, which means carrying per-player progression state.
- One device or a handful. One makes it a pilgrimage and a server hub. A handful keeps nobody stranded a continent away. In PvP a handful is close to required, since they are the matchmaker.
