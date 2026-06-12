# Interesting Enemies

New rusty hostile mobs with interesting and varied AI. Horror through *behavior*, not stats —
the scary part is what they do, not how hard they hit.

Naming follows vanilla register (drifter, shiver, bowtorn): blunt single-word folk-nouns,
lowercase in prose, like names survivors coined for things they'd rather not describe.

## Design principles

- **Sound before sight.** Every mob telegraphs through audio long before you see it. Dread lives
  in the gap between hearing and seeing.
- **They exploit light.** Light is the player's safety resource in VS. Mobs that respect, avoid,
  or *attack* light invert that safety.
- **Movement when unobserved.** Checking "is the player looking at me" (view-angle + LOS ray) is
  cheap and powers half of these designs. Things that reposition when you blink are scarier than
  things that charge.
- **Commitment and consequence.** No take-backs. Once a thing triggers, the only out is killing it
  or escaping it — never canceling it.
- **They arrive, never appear.** Nothing spawns in your face. Everything stalks in from darkness,
  ceiling, or distance, so there's always a "how long was it there?" moment.
- **They attack your safety net, not just your HP bar.** Torches, doors, livestock, temporal
  stability. Losing the *home* is worse than losing health.

---

## The roster

### 1. Chaff — the swarm

Hundreds of corroded, moth-sized locust scraps that fly as a single organism. Found perched in
cave rooms and ruin interiors, coating walls and ceilings like rust stains — until your torchlight
touches them and the wall *peels off and takes flight*.

**Behavior loop**
- Dormant: members perch motionless on walls/ceiling, visually near-indistinguishable from rust
  staining. A hidden **core** entity (the swarm brain) sleeps in the middle of them.
- Disturbed: light or noise wakes the core. The swarm lifts and *murmurates* — boids steering
  (separation/alignment/cohesion) around the core, which does the actual pathfinding.
- Attack pattern: the swarm doesn't beeline. It circles overhead in a tightening helix, then
  collapses into a funnel dive through the player — chip damage per body passing through.
- **Torch-snuffing:** any pass through a placed torch or held light has a chance to extinguish it.
  The swarm's opening move is to put you in the dark with it.
- Individuals are nearly free to kill but meaningless. Fire/smoke scatters the swarm temporarily.
  The real kill is the core — a fist-sized, brighter mote that hides in the densest part of the
  cloud and *uses the swarm as cover*.

**Horror:** the sound — thousands of dry metal wings, volume mapped to swarm proximity. And the
dawning realization in a "decorated" cave room that the decoration is breathing.

**Feasibility:** one server entity (core) does A*/steering; members are collision-less light
entities doing local boids around it (butterflies prove cheap flyers are viable). Member positions
can even be largely client-interpolated from core state. Perched-dormant = stationary entities
with a flat-against-block pose.

---

### 2. Bloat — the committed bomb

A drifter whose chest cavity has swollen into a translucent sac of glowing rust crystal. It does
not want to be seen. (Supersedes `medium/creepers.md`.)

**Behavior loop**
- **Stalk:** maintains a distance band from the player and actively stays *out of line of sight* —
  samples nearby positions, scores them by "blocked from player's eyes," and pathfinds cover to
  cover. You track it by its wet breathing and the faint orange glow it casts on walls it hides
  behind.
- **Reposition:** if the player holds it in view for more than a couple seconds, it breaks LOS —
  it does not approach while watched.
- **Trigger:** proximity fuse (player within N blocks, regardless of LOS) or taking any damage.
- **Fuse — the point of no return:** a rising shriek, chest light pulsing faster. It stops hiding
  and *sprints at you*, weaving, ignoring flinch/knockback. The fuse cannot be canceled, blocked,
  or out-waited. Two outs: kill it before the timer (low HP, but fast and erratic), or get
  out of radius / behind terrain.
- **Explosion:** modest block damage, heavy entity damage — and a **temporal rupture**: a local
  stability drain zone lingers where it detonated, with a chance to pull a couple of regular
  drifters through. The boom is not the end of the problem.

**Horror:** the trigger usually happens from a blind angle — you hear the shriek start and you
*don't know where it's coming from*. Three seconds of pure spatial-audio panic.

**Feasibility:** LOS scoring = ray from player eye position to candidate points, a few rays per
think tick. Fuse state = high-priority AI task that overrides hurt-flinch. Explosion via
`CreateExplosion`; stability drain hooks the temporal stability system.

---

### 3. Gibbet — the ceiling ambusher

In ruin shafts and tall cave passages you sometimes see them: desiccated bodies dangling from
rusted chain, swaying slightly. Old-world executions, hung in irons as warnings. Ambient decor.

Most of them are.

**Behavior loop**
- **Perch:** hangs inert from the ceiling — no nameplate, no aggro, idle sway animation matching
  the genuinely-decorative variants (worldgen places real corpse-decor so the mimic has a crowd
  to hide in). Tells, for the attentive: it's *facing you*, and it drips.
- **Drop:** when a player passes beneath (cylinder check + downward LOS), it releases — falls on
  your head, **latches**, and starts strangling: damage over time, sprint disabled, FOV/screen
  wrench, your own swings at it are awkward (it's *on you*).
- **Detach options:** hurt it enough, sprint-slam into a wall, or have a friend carve it off you.
- **Escape:** at low HP it disengages, scuttles up the nearest wall into darkness, and
  **re-perches somewhere ahead of you**. It will try again. It does not stop trying until it's
  dead or you've left its territory.

**Horror:** retroactive — after your first gibbet, every dangling corpse in every ruin is a
threat. The mod only needs a few real ones for players to fear all of them. (And the genuinely
safe ones ensure "just shoot every corpse" wastes arrows... and hitting a live one wakes it.)

**Feasibility:** the perch is just a stationary entity at a ceiling block — no climbing
locomotion needed for v1. Latch = ride the player entity (attachment point), like a reverse
mount. Wall-escape can fake climbing: gravity off, lerp up the wall face, then teleport-snap to
the next perch point picked from ceiling blocks along the player's likely path (corridor
direction).

---

### 4. Knell — the squad with a heart

A massive drifter-thing carrying a corroded bell on a yoke across its shoulders, attended by a
flock of small, fast, cowardly creatures — **vespers**. Ties into vanilla bell lore: the bell is
what binds them.

**Behavior loop**
- **While the knell lives:** it advances slowly, ringing. Each toll emboldens the vespers —
  they coordinate like a pack with assigned roles: two flank wide, one feints at your face, one
  waits behind you for your back to turn (slot-based positioning around the target, leader
  assigns slots). Vespers individually are weak and *will not* press an attack while you face
  them — they matador, retreat, rotate. The knell itself is slow, tanky, hits like a wall.
- **Kill order dilemma:**
  - **Kill the knell first:** the bell stops. Every vesper *panics* — screaming scatter into
    the dark. But panicked vespers don't despawn: any that get cornered (or that you chase)
    flip to frenzy — reckless, fast, all-in. And survivors that escape into darkness regroup and
    **stalk** you for the rest of the night, attacking only opportunistically.
  - **Kill vespers first:** the knell consumes their corpses — eats them off the ground —
    healing and gaining toll rate. Cull too many and you've built a faster, angrier wall.
- Either order has a cost. The "right" answer is positioning, not target priority.

**Horror:** the bell. You hear a single distant toll at night and you know the whole apparatus is
out there somewhere, walking. And after the kill — the *silence*, then the screaming, is worse
than the fight.

**Feasibility:** vanilla `herdId` already groups entities; the knell carries the shared
blackboard (slot assignments, morale state). Big-death broadcast flips every herd member's task
tree to panic/frenzy/stalk. Slot positioning is offset-points around the target entity, re-scored
per think tick.

---

### 5. Gloam — the thing at the edge of the light

A tall, thin figure that is never in your light. Ever. It stands precisely at the boundary where
torchlight fails, and it watches.

**Behavior loop**
- Maintains distance = your light radius + 1. Light is its wall; it will never cross into
  illumination above a threshold.
- **Moves only when unobserved** — look at it and it's a statue; look away and it repositions
  (silently, instantly stops when your view sweeps back).
- It does not attack you. It attacks your **light**: snuffs placed torches at the edge of your
  camp, one by one, working inward. Lanterns it can't snuff, it studies.
- It positions to **herd** — placing itself along your escape routes, narrowing your options
  toward darkness or hazard. If your light fully dies with it nearby... it closes the distance
  while you fumble, and the first hit comes from behind.
- Hates fire damage; a thrown torch landing near it forces a long retreat.

**Horror:** pure presence. Ninety percent of encounters are "it stood there for ten minutes and
left." The player's relationship with their own light supply becomes the mechanic — watching your
torch burn down in a cave with a gloam outside the glow is the scariest resource timer in the
game.

**Feasibility:** light level via `GetLightLevel` at candidate positions; observed-check = player
view angle + LOS ray (same primitive as the bloat). Torch-snuffing = targeted block removal with
a sound cue. Needs careful spawn gating (deep caves, moonless nights) so it stays rare enough to
stay scary.

---

### 6. Mourner — the lure

Something that learned to make the sounds that make you walk toward them.

**Behavior loop**
- Passive listener: records categories of recent nearby sound events — your pick striking stone,
  footsteps, a sheep, a door.
- **Lure:** from hiding, it replays them — *slightly wrong*. Pitch a little low, rhythm a little
  off. Mining sounds from a dead-end gallery. A bleat from a side passage (free food, in a game
  where food matters). Your own footsteps behind you, stopping when you stop, one beat late.
- It picks lure positions with intent: projected sound source sits between the player and a
  chokepoint or drop, with itself waiting at the ambush point.
- In melee it's a glass cannon — strong opener from behind/dark, folds quickly when faced. The
  fight was never the point; the *walk* was.

**Horror:** it weaponizes the game's own audio language against player trust. After meeting one,
every distant mining sound on a multiplayer server is suspect. ("Is that you in the east
tunnel?" "...I'm not in the east tunnel.")

**Feasibility:** server knows block-break events, entity sounds, door interactions — keep a small
ring buffer per mourner of categorized sounds heard in range and replay assets at an offset
position. Footstep mimicry approximated from player movement state. Chokepoint selection can be
cheap: prefer lure points whose path from the player passes within ambush range of the mourner.

---

### 7. Knocker — the home invasion

Not a cave mob. A homestead mob. It comes to *your* base, at night, and it does not come in.
(The name is honest folklore — knockers were the things miners heard tapping in the dark.)

**Behavior loop**
- Night 1: three slow knocks on your door, long after dark. Nothing outside. (It knocks, then
  repositions out of LOS of windows/door — same hide primitive as the bloat.)
- Following nights, escalation: it circles the property snuffing exterior torches. It tests —
  knocks on walls, on shutters, rattles a gate. It kills penned livestock *silently*, one animal
  a night, found in the morning. It learns your walls: probes for the gap in your fence, the
  unlit corner, and uses the same approach route once it finds one.
- It never forces entry and never fights at your door. The escalation continues until you do the
  thing it's shaping you to do: **go outside in the dark and hunt it.** In the open at night it's
  a genuinely dangerous fight — fast, circling, breaking LOS between passes.
- Killing it ends the visits for a long while. Hiding inside forever means losing the animals,
  the torches, and eventually your nerve.

**Horror:** VS's homestead is the player's one safe place; this mob is engineered to make safety
feel conditional. Knocking is the cheapest possible mechanic (a sound event on a door block) and
the single scariest thing on this list.

**Feasibility:** all existing primitives — pathfind to door/torch/animal targets, sound events,
LOS-hiding. Needs a per-player/per-base "campaign" state tracking escalation night over night.
Should respect land claims config on servers.

---

## Seeds (not yet designed)

- **Wake** — a shrouded figure that appears at extreme render distance, always facing you,
  following in your wake. Every time you sleep or a storm passes, it is closer. It never chases;
  it only *progresses*. Kill it on your own terms (brutal melee fight) or live with the
  countdown.
- **Rustworm** — terrestrial sibling of the gibbet: a burrower that trails disturbed-soil blocks
  past your fields at night and bursts up through floors that aren't stone.
- **Flicker** — exists only at low temporal stability; phases in and out (unhittable while
  phased), leaves afterimages where it *was* a second ago, so you're always fighting your own
  latency.
- **Choir** — dead drifters near a knell's bell don't stay dead; corpses you didn't burn get
  back up on the next toll.

## Shared tech

Most of the roster runs on five reusable primitives — worth building as a small AI library first:

1. **Observed-check** — is this entity within the player's view cone *and* unoccluded (LOS ray
   from eye position)? Powers: bloat stalking, gloam freeze, knocker hiding, gibbet tells.
2. **Cover scoring** — sample nearby reachable points, score by LOS-blocked-from-player.
   Powers: bloat, knocker, mourner ambush placement.
3. **Herd blackboard** — leader entity holds shared state (slots, morale, escalation) over
   vanilla `herdId`; death broadcast flips follower task trees. Powers: knell, chaff core.
4. **Swarm anchor** — one pathfinding brain, N collision-less steering bodies. Powers: chaff.
5. **Sound-event bus** — server-side ring buffer of categorized audible events per region.
   Powers: mourner lures, chaff wake-up, knocker knocks.

Spawn gating matters as much as AI: every one of these gets dramatically less scary if it's
common. Rare, biome/depth/condition-gated, and never two ambush archetypes in the same chunk.
