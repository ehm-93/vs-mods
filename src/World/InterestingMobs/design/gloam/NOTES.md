# Gloam — direction-setting concepts

Six maximum-entropy body plans for the light-boundary watcher (see
`docs/ideas/hard/interesting-enemies.md` #5). Nothing is locked — these exist to pick a
direction. All built from scratch (NOT reposed drifters, unlike `design/concepts/gloam.json`)
on the bloat texture kit (rust 16x16 / sac 32x32; render with `texturesRoot` at
`assets/worldinterestingmobs/textures`).

| variant | one-line | height | anim |
|---|---|---|---|
| `wader` | stilt-heron: 85% legs, neck cranes DOWN so the head hangs at player face height | 4.8 | `stride` — high-step walk, bird-steadicam head stays dead level |
| `shroud` | limbless mourning veil: hooded void face, fabric panels to a floating hem, hollow underneath, a hand pressing from inside | 3.0 | — |
| `snuffer` | the smolder: charcoal-slab log-man, ember glow leaking through seams, hollow snuffer-bell head, flame-pinching spike arm | 3.0 | — |
| `snag` | false tree: passes as a dead snag from all views; two branches are arms with twig fingers, knot-hole eyes | 3.5 | `reveal` — 0–12 still, cracks open into a stalking crouch (Hold) |
| `crooked` | the wrong man: human at 20 m; backwards head, double elbows, reversed feet, twisted waist at 10 m | 2.2 | — |
| `shine` | eyeshine heap: near-black 6-legged low mass, only a fan of 16 glow eyes reads at distance | 1.25 tall × 2.4 long | — |

## Picked direction: wader (2026-06-12)

Emmett picked the wader; `gloam-wader-v2.{json,png}` + `gloam-wader-v2-stride.gif` is the
cleaned-up pass, capped at **3 blocks** (2.79 actual, was 4.8). Rebuilt from scratch
(uniform scaling would have thinned the stilts away); same DNA, fixes the v1 nits:

- **Balance (Emmett feedback, two rounds):** torso knot sits BEHIND the vertical through
  the feet (torso z-center ~5.4 vs foot contact ~7.5) so the body counterweights the
  cantilevered neck+head, vulture-style; hips squatted from y36 to y33.5 with deeper
  knee/ankle bends for a coiled stance. Round 2: torso pitched rotX +14 about a SHOULDER
  pivot (not the hips) so the bum juts back behind the legs while the shoulders stay put;
  Neck1 deepened -62 → -85 to exactly cancel the +23 delta so the head arc/height is
  unchanged. SIGN GOTCHA: positive rotX swings below-pivot geometry BACK (-Z) /
  above-pivot forward — a first attempt at -22 sent the bum forward. Round 3: hips moved
  DOWN 2 + BACK 2.5 (to y31.5, z5.5 — behind the planted feet at z~7.5) and ankles pulled
  back, same bone lengths: feet held fixed and the chain re-solved as 2-link IK
  (pick cannon lean 20°/16° → ankle position → solve thigh/shank). Final thetas
  L 32.3/-39.7/20, R 29.7/-36.5/16. Final height 2.66 blocks.

- Head is now a narrow hanging skull with brow, backward jaw-hook, and two 1.4u ember
  eyes that actually read — eyes use the **sac eruption-node texel uv [8,12,9,13]**
  (#B87430, brightest warm texel in the kit; rust oxide texels are too dark to read even
  at full glow — same trap the shine builder hit).
- Eye line sits at ~1.7 blocks ≈ player face height; head hangs ~0.85 blocks in front of
  the feet (was ~1.2 — looked tippy).
- Asymmetric legs kept: L 3-seg (11/12/13.5u, thetas 18/-28/8), R 4-drop (12/12/12,
  14/-24/4); knee/ankle knobs, 3-toe splay + heel spur feet.
- `stride` anim (60f Repeat, 2s cycle — Emmett: "pick its feet up and place them
  deliberately, think a heron"; round 2: "no pause at the top, reach farther"): per leg —
  trail (whole chain rotates back, foot flat = treadmill) → pick-up (heel peels, foot
  gathers up-back) → high tuck (foot ~0.8 blocks up under the bum, toes DROOPED ~50°) →
  continuous unfold (knee stays high, foot arcs forward-down, NO hold at the top — an
  earlier freeze beat read as a hitch) → far reach → toe-first placement at z~12, ~4
  units ahead of neutral (IK-solved to meet the ground) → stance slide back. STANCE
  RULE (round 3 — "hitch while in ground contact"): every grounded key must be IK-solved
  PER LEG so the foot tracks a straight constant-velocity ground line (here 0.46 u/f, y
  flat); reusing the bind pose as a stance key gives a speed step, and rotating the whole
  chain around the hip (constant radius) makes the planted foot rise off the ground near
  trail. Left and right need separate solutions (different bone lengths/statics); swing
  keys can be shared (airborne). Keyframes derived from world-angle targets with
  ground-contact checks, then converted to additive engine rotations — naive additive
  guessing folds legs past horizontal into side-kicks.
  Torso sways ±1.2° with Neck1 counter-rotating so the hanging head stays dead level; 9°
  head-yaw saccade at f22-37.

## Critic pass (2026-06-12)

All six geometrically sound; no pair shares a silhouette. Standouts: **snag** (cleanest
execution, the reveal is the money shot), **wader** (most original silhouette, scariest
single idea), **crooked** (best horror-in-the-details). Noted nits if a direction gets
picked: wader's head is a featureless slab (eyes don't read at render scale); shroud's hood
is too square from top; snuffer's bell head reads as a pancake stack and its ember seams are
faint in flat renders (real in-game glow should prove out); shine's eye cluster mixes pale
gray texels with ember orange so it reads speckle rather than arcs.

## Files

- `gloam-<variant>.json` — shape (shapes-MCP authored; per-variant seeded doc_script)
- `gloam-<variant>.png` — n/e/sw/top view grid, 1-block grid + scale bar
- `gloam-wader-stride.gif`, `gloam-snag-reveal.gif` — demo anims (e/sw views, 30 fps)

Picked direction(s) should get an evolved deterministic generator (gen-gloam.mjs) like
bloat/gibbet — these jsons are concept artifacts, not source of truth for production.

## FACING CONVENTION (learned in-game 2026-06-12)

**VS entity shapes must be authored facing WEST (-X)** — verified by rendering the vanilla
surface drifter (face shows in the `w` view). The wader was originally built facing +Z
(south) and in-game it walked 90° to the RIGHT of its visual facing. Fixed by rotating the
whole shape -90° about +Y in doc_script (front +Z → -X): root points `(x,y,z)→(16-z,y,x)`;
CHILD locals are relative to the parent's FROM corner, and rotation makes a different
material corner the min-corner, so each child level maps `(x,y,z)→(parentOldSizeZ-z,y,x)`
(missing that shift accumulates down chains and explodes the model); element + keyframe
rotations `[rx,ry,rz]→[-rz,ry,rx]`; faces remap north→east→south→west→north. Caveat: VS
applies element rotations Z→Y→X, so multi-axis elements (head cock, wings, toes) come
through with tiny order-swap errors — cosmetic here. Future gloam work (and gen-gloam.mjs)
should author facing -X from the start; walk anims advance toward -X.

## In-game test entity (2026-06-12)

`gloam-wader-v2.json` emitted as-is to `assets/worldinterestingmobs/shapes/entity/gloam/wader.json`
and registered as `entities/gloam.json` (+ lang "Gloam") so the movement can be felt in-game —
**placeholder AI only** (wander on `stride` + lookaround + getoutofwater; no spawnconditions, no
despawn — command-spawn only): `/entity spawn worldinterestingmobs:gloam`. Hitbox 0.75x2.5,
eyeHeight 1.55 (the hanging head). FOOT-SKATE CALIBRATION (eyeballed in-game with Emmett
2026-06-12): the stride anim's natural ground speed is 0.865 blocks/s (stance slide 0.4615
u/f x 30fps); converged in-game at wander movespeed 0.0056 @ animationSpeed 1.0 → actual
speed ≈ movespeed x ~154 blocks/s, so for any task: `animationSpeed = movespeed * 154 /
0.865` (Emmett prefers tuning movespeed to the anim, not vice versa; getoutofwater runs
0.0064 @ 1.15). No idle/hurt/die anims
yet: it stands statue-still between wanders (on-theme) and freezes in bind pose on death.
Builds clean. The real gloam AI (light-boundary, statue-when-observed, torch-snuffing) comes
later.

## Head tracking (2026-06-12, mechanism settled by a 4-agent decompile investigation)

`Gloam/EntityBehaviorGloamHeadTracking.cs` (registered `gloamheadtracking`, on the entity's
CLIENT behaviors) makes the hanging head face the nearest living creature. THE WORKING
MECHANISM is the **vanilla EntityBoat-weathervane pattern**: two permanently-active
zero-speed "driver" animations baked into the shape — `headtrackyaw` (351f: frame = 175 +
yawDeg, sweeping Neck ±148.75° + Head ±26.25°) and `headtrackpitch` (51f: frame = 25 +
pitchDeg) — started client-side and SCRUBBED per tick by setting
`RunningAnimation.CurrentFrame` via `Animator.GetAnimationState(code)`.

THREE ENGINE TRAPS (each cost an in-game test; all verified at decompiled-source level):
1. **Speed-0 anims contribute exactly zero unless you force the weights.** At
   `AnimationSpeed == 0`, `RunningAnimation.Progress()` early-returns (EasingFactor never
   ramps from 0) and `CalcBlendedWeight()` is gated on speed != 0 (BlendedWeight stays 0) —
   and every keyframe sample is multiplied by BlendedWeight. The anim shows as active, holds
   its scrubbed frame, and renders NOTHING. Fix: write `BlendedWeight = 1f; EasingFactor =
   1f` every tick (they're public; nothing overwrites them at speed 0). This is exactly what
   EntityBoat's weathervane does.
2. **The server purges client-started animations.** `OnReceivedServerAnimations` removes any
   active anim the server doesn't know (fires every time server AI starts/stops `stride`),
   and every restart resets CurrentFrame to 0 via AnimNowActive. Fix: `ClientSide = true` on
   the AnimationMetaData (engine-provided exemption flag).
3. **`AnimatorBase.Matrices` returns static default-pose matrices when activeAnimCount==0**
   — any pose state is invisible with no animation active. The speed-0 drivers themselves
   keep activeAnimCount ≥ 2 forever (their stop conditions all require Iterations != 0,
   frozen at 0), so this is moot now; the `idle` anim remains for flavor.

WHY NOT THE ENGINE'S HEAD CONTROLLER (`ElementPose.degOff*` / `EntityHeadController`): the
degOff render path itself DOES work for creatures, but (a) the stock controller zeroes
head/neck degOffs EVERY render frame behind an `entity.Pos.HeadYaw != 0` gate that nothing
ever sets for creatures (vanilla attaches head controllers ONLY for players, driven by the
camera), (b) joint upload is first-wins per JointId and `requireJointsForElements` (entity
attribute) is honored only on the anim-cache MISS path — silently dropped on cache hits,
and (c) the cached ElementPose trees are SHARED across entities of a code, so degOff writes
bleed across all instances. Keyframe-referencing an element in any animation is the only
reliable way to give it a joint. Don't resurrect that path.

## CURRENT AI — single FSM (2026-06-12, v3; supersedes the multi-task notes below)

Emmett asked to rethink it holistically. The four competing priority tasks (stalk/strike/flee/
snuff) are GONE, replaced by ONE state machine: `Gloam/AiTaskGloamHunt.cs` (registered
`gloamhunt`, priority 1.5). Still uses `GloamSenses` statics (light = `MaxTimeOfDayLight` +
held-light estimate `heldHsv[2]-dist` via `Entity.LightHsv`; `TryFindGround`). Three concentric
ranges + two timers:

- **Ranges**: detection 22 > stalk 13 > engage 5. **Timers** (from when stalking begins, reset
  on every re-stalk): attack 6s, forceAttack 14s.
- **Approach** — player crossed detection: sprint (0.045 @ sprint anim 2.7) to a dark spot on
  the stalk ring.
- **Stalk** — circle the player at stalk radius, dir L/R picked 50/50 on entry (5%/replan flip),
  dark-preferred standpoints (creep 0.0056 @ stride), divert to **Snuff** a nearby lit torch
  (≥6 from player, 9s cooldown) while circling — SPRINTS to the torch (0.045 @ sprint anim,
  Emmett) then STOMPS it out (plays the stomp anim on arrival, faces the torch, snuffs on the
  impact frame damageAtMs=400ms, returns to stalk after stompMs=930ms). Same animation as the
  attack — it stamps the light out like it'd stamp you. Transitions: player into engage BEFORE attack
  timer → Flee; AFTER → Attack; forceAttack elapsed → Attack; **player light ≤ 4 (no light) →
  Attack now** (the "your light died" pounce); drifted past stalk+2 → Approach.
- **Flee** — sprint to just outside the stalk ring (player + (stalk+3) away from threat), then
  re-Stalk (timers reset). Max 3s.
- **Attack** — Charge (0.052) → at ≤ strikeReach 2.6 Stomp (stomp anim, damage 8 blunt tier 2
  kb 1.5 @ 400ms) → melt into Flee. COMMITTED: damage does not break a running attack.
- **Damage**: `OnEntityHurt` sets pendingFlee → next tick drops to Flee, UNLESS state==Attack
  (Emmett: "flee if not a forced attack"). Records the attacker for flee direction.
- **Disengage**: player past detection×1.5 (33) → task ends → wander/lookaround. getoutofwater
  (1.4) is below hunt (1.5) so it won't bail to water mid-hunt (revisit if it drowns).
- Head-track suppression (client behavior) keys on sprint/stomp anims → auto-off during
  approach/flee/charge/stomp, on during stalk creep (watches you while circling).

WHY ONE TASK: the "did the player cross engage BEFORE or AFTER the attack timer" logic is
cross-state and was unworkable across priority-competing tasks (hence the old priorityForCancel
100 / hurtInterrupt hacks). A single owner of the state makes it trivial and removes all the
preemption juggling.

FIXES (2026-06-12, post-FSM):
- **Darkness pounce now counts HELD light.** `Entity.LightHsv` is only set for entities on FIRE
  (decompiled) — a player carrying a torch reports LightHsv null, so the old held-light estimate
  was always 0 and the pounce fired on block/sun light alone (ignored whether you held a light).
  Fixed: `GloamSenses.HeldLightLevel` reads `GetLightHsv` off the hand slots
  (EntityAgent.Right/LeftHandItemSlot; player hands = active hotbar slot + slot 11), and LightAt
  folds it in. So "player in darkness" = block+sun+held all low → a held torch protects you, true
  darkness (no held, no placed, night) pounces. Bonus: stalk standpoints now avoid the player's
  *held*-torch glow too, hugging the real dark edge.
- **forceAttack de-flaked.** `EnterStalk` reset the attack/forceAttack clock, and snuff diversions
  (Stalk→Snuff→Stalk) hit it on every torch pinch, so forceAttack rarely accumulated. Now
  `EnterStalk(resetTimers)` — true for a fresh stalk (approach/flee/attack→stalk), FALSE returning
  from snuff (snuff is part of stalking). forceAttack now ticks steadily across snuffs.
- **Darkness = immediate pounce, NO stalk** (Emmett). The no-light check is now a TOP-LEVEL
  override in ContinueExecute (before the state switch) + checked in StartExecute, not buried in
  the per-state ticks — so detecting a player in darkness goes straight to Attack from anywhere in
  detection range, skipping approach/stalk entirely. Excluded while state==Attack (don't restart a
  running attack) or Flee (let the post-attack/​damage melt finish — that brief retreat is the
  player's window to relight; relight → not dark → it drops back to stalking instead of
  re-attacking). Persistent darkness = charge→stomp→melt→charge again, relentless.

--- everything below is the SUPERSEDED v2 multi-task design (kept for history) ---

## The real AI (2026-06-12, v2 — Emmett renegged on the angel rules same day)

v1 was weeping-angel (statue-when-observed lurk + darkness-gated seek/melee); Emmett replaced it
before testing: *no freezing* — it stalks openly, circles to your back, and hit-and-runs. Three
server tasks (`Gloam/AiTaskGloam*.cs`, registered gloamstalk/gloamsnuff/gloamstrike) + shared
`GloamSenses` statics (observed-check = view-cone dot 0.5 + collidable-blocks LOS ray; light =
`MaxTimeOfDayLight` PLUS held-light estimate `heldHsv[2] - dist` via `Entity.LightHsv`, because
held torches are client-side dynamic light — `GetLightLevel` can't see them, the player can;
`TryFindGround` + dark-biased `PickRetreatSpot` samplers).

- **gloamstalk 1.2** — the spiral. Every 700ms: step its bearing around the player up to 30°
  toward dead-behind (`player.Pos.Yaw + π`), then stand at the *closest* dark standable radius
  (light ≤ 7, minRange 3.5..maxRange 16) on that bearing — walking the radial march in 1-block
  steps from minRange out. Reads as: strides at you while you watch, slides perpendicular along
  your light's edge, ends up at your back. Face it and it keeps circling; the spiral only wins
  when you stop tracking it. Light > 7 at its feet or player < 2.5 → sprints into the dark
  (0.016 @ sprint 2.85) — it is never seen *standing* in light, only sprinting out of it.
- **gloamstrike COMMITS (Emmett)** — `priorityForCancel: 100` so no other task can preempt it
  once started (VS rule, decompiled: a task preempts the active one only if its `priority` >
  the active task's `PriorityForCancel`, default = priority; nothing else here exceeds 100), AND
  the charge no longer bails on distance (`dist > triggerRange*1.5` flee removed — that made it
  abandon the lunge the moment you gained ground), only a hard 6s timeout. So once it starts a
  strike it runs charge→stomp→flee to completion. (base.ContinueExecute only bails on daytime
  hours, unused here; the strike ends solely on its own phase logic.) STOMP BIAS (Emmett "too
  quick to run instead of stomp"): at the high charge speed it was orbiting/timing out around
  the old 1.9 reach instead of landing the stomp — widened strikeReach 1.9→2.6 (navigate target
  = reach−0.4 = 2.2, stomp fires at ≤2.6) so it commits to the stomp from where it ends up.
  DAMAGE CANCEL (Emmett): OnEntityHurt override sets hurtInterrupt → ContinueExecute returns
  false next tick, so any damage breaks the commit and ends the strike (FinishExecute also stops
  the stomp anim now). hurtInterrupt reset in StartExecute.
- **gloamflee 2.0** (Emmett "flee when it takes damage, not stalk") — dedicated retreat task,
  `AiTaskGloamFlee`. OnEntityHurt (fires on every task instance, active or not) arms
  fleeUntilMs = now + fleeDurationMs (3s) and records the attacker (`DamageSource.GetCauseEntity`
  ?? nearest player); ShouldExecute = within that window. Sprints to dark `PickRetreatSpot`s away
  from the threat (fleeSpeed 0.045 @ sprint anim 2.46), re-picking until the timer ends. Priority
  2.0 (> stalk/snuff/wander) preempts stalking instantly; priorityForCancel 50 so nothing cuts
  the retreat short. Mid-STRIKE hits work via the strike's own hurtInterrupt self-cancel (flee
  2.0 can't preempt the committed strike at PriorityForCancel 100) — strike frees the slot, flee
  wins it next tick. Order: flee 2.0 > strike 1.5 > getoutofwater 1.4 > snuff 1.25 > stalk 1.2
  > wander 1.0 > lookaround 0.5.
- **gloamstrike 1.5** — the payoff. Triggers when it's in your rear arc (>110° off your view),
  within 10, itself in dark, LOS clear. Phases: CHARGE (0.018 @ sprint 3.2, path refreshed
  400ms, committed — turning around doesn't save you; 5s timeout) → at ≤1.9 STOMP (stomp anim,
  yaw-lerped onto target, damage 8 blunt tier 2 kb 1.5 at 400ms = anim slam frame f12,
  projectilehit thud) → FLEE (sprint to a `PickRetreatSpot`, done at 4.5s/14 blocks). Cooldown
  12–25s. One stomp per strike by design.
- **gloamsnuff 1.25** — unchanged from v1, the one job that licenses entering light: nearest
  placed `torch-*-lit-*` within 14 (≥ 6 from player), dart 0.009 @ stride 1.6 (deliberate, NOT
  sprint), freeze-if-observed during the dart, abort if player < 5; at ≤1.4 waits 700ms, swaps
  `-lit-` → `-extinct-` + `sounds/effect/extinguish`. Cooldown 6–16s. Edge-torches-first is
  emergent: nearest torch to a thing in the dark is the outermost.

Order: strike 1.5 > getoutofwater 1.4 > snuff 1.25 > stalk 1.2 > wander 1.0 > lookaround 0.5.
Daytime emergently safe (everything over threshold → stalk only retreats). Tuning knobs:
lightThreshold 7 (≈7-block ring vs basic torch V=14), spiralStepDeg 30, behindAngleDeg 110,
strike cooldown, panicRange 2.5.

## Death animation (2026-06-12)

`death` (33f, onAnimationEnd **Hold** so the corpse stays down) — v2 (Emmett: "just a spasm,
wobble, and fall over"): the bird stays STIFF and topples like a felled tree rather than crumpling.
Spasm (f2: roll jolt ~4° + head snap + leg twitch) → wobble (f8–14: subtle roll teeter ±3–5°,
v3 toned down from ±9–16° which threw the head way out) → timber (f20–30: rigid roll to ~88°
about a ground pivot at y=0,z=8, lands flat on its side), head lolls.
The rigid topple = all three roots (LThigh/RThigh/Torso) roll rotX θ with a WORLD-space offset so
they orbit the SAME pivot (`worldOff` = rigid roll of the root origin about the pivot), offset then
compensated for the engine rotating keyframe offsets by the element's total rotation
`Rz(staticZ)·Rx(θ)` → `keyOff = Rz(−staticZ)·Rx(−θ)·worldOff`. (v1 was a crumple/buckle — replaced.)
Wired as
entity client anim `{ code:"die", animation:"death", blendMode:"Average", weight:20, triggeredBy:{
onControls:["dead"] } }` — VS auto-fires `die` when the entity dies (EntityAgent.Die only plays
the death SOUND, not an anim; the engine's trigger system starts anims whose triggeredBy controls
are active, and "dead" activates on !Alive — same pattern as vanilla chicken). Head-track behavior
eases its Add contribution to 0 when !Alive, so it doesn't fight the collapse. Preview
`gloam-wader-v2-death.gif`.

## Sprint + stomp animations (2026-06-12, shapes MCP, in wader.json)

- **sprint** (40f Repeat, v3 — "frantic ostrich… think about what a sprint means", 3 Emmett
  rounds: v1 stride-copy read tame; v2 added gait/bounce but wings pumped, no flight, body
  centered) = fully IK-solved running gait, 16-key grid: **14f stance / 6f TRUE FLIGHT** per
  step (both feet clearly airborne at the bounce peak), foot slides −8→+8 in model space
  (1.143 u/f → **natural speed 2.143 b/s @ anim 1.0** — `animationSpeed = movespeed*154/2.143`;
  world stride ≈ 1.43 blocks/step, exceeds the 16u contact travel because the body keeps
  moving through flight). **Whole body leads the support line 2.5u forward** (BODY_FWD offset
  on all three roots — sprint = controlled falling; stance loads BEHIND the torso). Bounce
  ±1.6u, low mid-stance f7/f27, peak mid-flight f17/f37; hips IK-solve against the bounced,
  led hip so stance feet stay glued. Round 4 (Emmett): body DROPPED 2u baseline (legs always
  loaded) + LOCKOUT 1.5u — the 2-link solve never extends closer than 1.5u to straight, so
  knee/ankle never snap fully open at toe-off; and the swing lifts the foot AT THE ANKLE
  (cannon held neutral/folded +60→+30→+5 through gather/tuck/unfold, foot hanging under the
  knee), NOT swung forward like a pendulum — it only settles flat in the last 2 frames before
  touchdown. HOCK-FOLD REBUILD (round 7 — "ankle extends too far" = the SHANK-CANNON joint,
  the bird hock): the swing originally pinned the cannon's WORLD angle, so as the IK shank
  moved the fold (shankWorld − cannonWorld) collapsed to +6..+14° through the reach = a nearly
  straight hock (diagnosed by printing both angles). Fix: the swing now places the ANKLE by IK
  and sets the cannon as a FOLD relative to the SOLVED shank (`cannonWorld = shankWorld −
  fold`), so the hock holds its bend wherever the shank lands. Swing control points are
  [p, ankleX, ankleY, fold, footWorld]; fold runs −22→−36→−32→−24 over snap/tuck/reach
  (negative = foot tucked up BEHIND the ankle). Stance stays foot-target; a short PLANT window
  (p 34→40) lerps the foot from the swing-end FK to the −8 contact and the cannon to the
  stance-start −8, so no skate. LESSON: control the JOINT FOLD relative to the IK-solved
  parent, not a child's world angle, when the parent is itself solved. Round 8 ("flex the
  ANKLE to lift the foot, not the waist and knee"): v7 raised the ankle TARGET (ankY 13→16),
  so the IK lifted the whole leg = hip/knee flex. Fix: hold ankY ~constant (≈13, no knee lift),
  translate ankX forward only, and let the negative FOLD do all the foot-lifting — the upper
  leg stays a stable column and the foot rises up-behind purely at the hock. Round 9 ("ankle
  overextending still"): the hock's net bend (cannonWorld−shankWorld, ≈−46 digitigrade at
  toe-off) was being driven POSITIVE (+40) in mid-swing — the cannon swung to the OTHER side
  of the shank, crossing 0 (dead straight) at the f17/f37 sign-flip = the overextension.
  Fix: fold DEEPER in the SAME direction (net stays −46→−68→−40 across the cycle, never near 0).
  The `fold` in ACP = −netBend, so the lift values are POSITIVE (46→68→50). Verify with the
  cannon-rotation readback: net = −59.7 + LCannon.rotationZ must stay one sign all cycle.
  Round 10 deepened the return fold (peak net −68→−95). Round 11 ("increase the gait"):
  STRIDE 16u→19u (foot ±9.5, swing ankle sweep + plant widened to match), body lead −2.5→−3,
  bounce 1.6→1.7. Natural ground speed rose (19/14)·30/16 = 2.14→2.55 b/s, so the JSON
  sprintAnimationSpeed dropped ~16% to keep the foot-skate match (strike 1.4→1.18, stalk
  1.3→1.09 — scaled by the natural-speed ratio off the prior eyeballed-good values).
  Round 12 ("wider gait + extend hip/knee on the back"): STRIDE 19u→21u (SH 10.5), and
  LOCKOUT 3.5→2.0 — the 2-link extension cap only bites where the leg reaches FARTHEST (the
  trailing leg at toe-off), so loosening it extends the back leg's hip/knee while leaving the
  hock fold (set separately) and the near-reach swing untouched. Natural 2.55→2.81 b/s →
  sprintAnimationSpeed strike 1.18→1.07, stalk 1.09→0.99. LOCKOUT is the clean knob for
  "straighter legs" (hip/knee), the swing `fold` for hock bend — they're independent.
  Round 13 ("torso bob feels out of sync"): TWO timing faults, found by auditing torso
  phases vs the leg cycle. (1) the lateral roll was period-20 → it leaned the SAME side at
  BOTH footfalls (+3 at f7 L-stance AND f27 R-stance); a run must lean toward the PLANTED foot,
  alternating = period 40. Fixed: roll = 3.5·sin(2π(f−(COMP−10))/40), +peak toward L at the
  L-compression frame, −peak toward R half a cycle later, ~0 at the flights. (2) the vertical
  bob bottomed at f7 but real mid-stance compression (hip over foot, where footX≈BODY_FWD) is
  at f≈4.7 because the body leads forward — bob low point moved f7→f5 (COMP). Neck vertical
  pump now lags the body bob ~2f (follow-through) instead of locking to it. LESSON: vertical
  bob is per-FOOTFALL (period = half cycle); lateral roll is per-STRIDE (full cycle); and the
  forward body-lead shifts mid-stance earlier than a naive half-stance guess.
  Round 14 ("sprint movement much faster"): doubled the ground speeds — strike chargeSpeed
  0.021→0.042 (~6.5 b/s, ≈ wolf-seek pace), fleeSpeed 0.018→0.036, stalk sprintSpeed
  0.018→0.036; rescaled sprintAnimationSpeed to match (strike 1.07→2.3, stalk 0.99→1.97 via
  movespeed·154/2.81). Round 15 ("a touch faster than a sprinting player"): 0.042 still didn't
  beat sprint, so pushed past wolf-seek (0.045, the known catches-a-sprinter benchmark) —
  chargeSpeed 0.052, fleeSpeed/stalk-sprint 0.045; animSpeed strike 2.84, stalk 2.46 (ratio
  movespeed·54.7). Reference: GlobalConstants BaseMoveSpeed 1.5, SprintSpeedMultiplier 2.0;
  no movespeed cap found in controlledphysics. Watch for charge OVERSHOOT/whiff at this speed
  (strikeReach 1.9) — if it blows past the player, widen strikeReach or add a decel, don't
  just lower speed. Movement speed = the AI task's movespeed (·~154 = b/s); animationSpeed
  only sets leg cadence — they must be scaled together or the feet skate.
  Chaos (current): torso rotX roll ±3.5 period-40
  per step (Neck1 counter-rolls 0.7×), neck pump −3.5..−8.5, head saccades rotY +14/−10.
  Wings LOCKED at full mantle the entire cycle (rotX ±38, rotY twist ±60, single-key holds —
  Emmett: flared constantly, not pumping). Posture constants: Torso rotZ +10, Neck −8,
  Neck2/3 −12.
  NECK SIGN RULE (verified by render after guessing wrong): on this chain **negative rotZ
  extends segments forward/horizontal, positive tips them back toward the vertical hang**
  (same sense as the torso's below-pivot-back: +rotZ = below-pivot geometry swings to +X =
  rear; the model faces −X).
- **stomp** (28f, EaseOut both) = secretary-bird kill stomp, LEFT leg: windup f5 (torso −10
  rears upright, Neck1 +20 recoils, leg gathers thigh −55/shank +70), coil f9 (thigh −85 —
  above horizontal, knee over the hip line — shank +85, cannon −45, foot +40: the foot hangs at
  player-HEAD height, "squishing from the head down" per Emmett), SLAM f12–14 (torso +14 whips
  over, thigh −35 + shank +4 ≈ straight leg driving forward-down, foot toes-down +15, Neck1 −10
  spears), recover f20, neutral f27. HEAD REVISION (Emmett, 2 tries): the neck/head now REAR UP
  AND BACK through the whole stomp, peaking at the slam, instead of dropping. STOMP NECK SIGN
  (render-verified, OPPOSITE of the sprint-derived rule): **negative rotZ rears Head/Neck1 UP and
  BACK, positive drops them down/forward** — first attempt used positive and "sent it further
  down" per Emmett. Values: Neck1 −10→−26→−48 (peak slam f12) / Head −5→−12→−22, settle −12/−5 at
  recover. The sprint's "neg extends forward" rule did NOT transfer (the stomp's forward torso
  pitch + the multi-seg craned neck flip the world-space result) — always RENDER-verify neck
  direction per animation. Reads as the body crashing down while the head whips up and back.
  NECK REFINEMENT (Emmett): base EXTENDS while upper FLEXES (S-curve/cobra crook) — Neck1
  −14→−34→−62 (extend, base rears up taller) but Neck2 +12 / Neck3 +14 / Head +26 at the slam
  (positive = FLEX/curl the upper neck over, head most). Opposite signs base-vs-upper = the
  coiled rear. Neck2/Neck3 keyed 0 at f0/f27 to ease in/out.
  Slam frame f12 = 400ms @ 30fps — gloamstrike's damageAtMs
  matches it. Leg signs (from stride data): thigh −Z = swing forward/up, shank +Z = knee fold,
  cannon −Z = ankle fold, foot +Z = toes down. Secondary motion (Emmett rounds 2–3): the plant
  leg sinks and THE WHOLE BIRD MOVES DOWN TO IT — round 2 folded the knee against a fixed hip,
  which lifted the foot up to the femur (root-element hips can't drop on their own; Emmett
  caught it). The fix: knee fold +k with ankle counter −k (keeps the shin's world angle, foot
  flat), and the leg's shortening becomes a keyframe OFFSET on the whole skeleton (RThigh +
  LThigh + Torso): down 12·(cos36.5°−cos(36.5°+k)) so the foot stays welded to the ground, and
  forward 12·(sin(36.5°+k)−sin36.5°) so it doesn't slide — the bird lunges over the planted
  foot as it sinks. Fold profile k = 5/8/12/2 at windup/coil/SLAM/settle: deepest plunge lands
  WITH the impact (~1.7u down, 1.9u forward). Wings flare like an ostrich mantle (Emmett:
  round 2's ±14° "just flapped"): rotX ±20 windup → ±60 coil → ±50 held through impact → fold
  by f20 (L out = +rotX, R out = −rotX; LWingB tip spreads −rotZ to −14). Reads spread-armed
  from the victim's front view. Mantle composition (3 rounds with Emmett — "flare" meant
  PRESENT THE BROAD FACE FORWARD, not roll the panels out edge-on): rotY twist (L +80 / R −80
  at slam, ±70 coil) spins each hanging side panel about its vertical axis so the rear edge
  swings around-under and the wide face points frontward, THEN rotX roll (±40-45) lifts the
  spread plane. Engine applies RotateByXYZ innermost-Z-first (vertex order Rz→Ry→Rx), so the
  rotY twist composes before the rotX roll; a rotZ-based twist fought the roll and read as
  diagonal fins. HIP-PIN RULE: the Torso pivots at the SHOULDER
  origin (11.1, 39) but the root-sibling thighs attach at the hip (10.5, 31.5) — any torso rotZ
  key needs a compensating keyframe offset `T = v − R(v)`, v = hip − shoulderOrigin = (−0.6,
  −7.5), or the femurs visibly disconnect from the pelvis (first stomp pass did exactly that).
  KEYFRAME-OFFSET ENGINE TRUTH (decompiled `ShapeElement.GetLocalTransformMatrix`, version-0
  anims): `M = T(origin) · R(static+anim) · T(From + keyframeOffset − origin)` — the offset is
  applied INSIDE the rotation, i.e. ROTATED by the element's total rotation. To displace an
  element by parent-space T, store `offset = R(−totalRot) · T`. Parent-space offsets looked fine
  in shapes-MCP renders but sheared the femurs off the pelvis in-game (the MCP composes offsets
  differently — for offset+rotation combos the GAME is the only ground truth). Version-1 anims
  use a different order again (rotation applied after the offset translate).
- Previews: `gloam-wader-v2-sprint.gif`, `gloam-wader-v2-stomp.gif`.

## Head tracking (2026-06-12, mechanism settled by a 4-agent decompile investigation) — continued

Aiming math (client tick): nearest `EntityAgent` within `searchRange` 12 (4x/s, with
hysteresis), `relYaw = AngleRadDistance(entity.Pos.Yaw, atan2(dx,dz))` — creatures render
at `Pos.Yaw` (+90° shape offset); BodyYaw is NEVER maintained client-side for creatures —
eased at `easePerSec` 4. YAW EASING MUST BE LINEAR, NOT SHORTEST-ANGULAR-PATH: the neck
can't rotate through its own back — when the target crosses the ±175° limit behind, the
head must unwind the long way around the front. `AngleRadDistance`-based easing winds
curYaw past the clamp and pins the head at the limit ("head gets stuck if you walk all the
way around it"). While the target sits inside the ~10° blind cone, the head HOLDS the
strain at the last trackable angle (no side-flip whip at dead-180). Pitch toward target
eye height clamped ±25° (rendered pitch is ~80% of commanded — keyframe extremes sum ±20°;
widen if exactness matters). Per-entity-safe (RunningAnimation state is per-entity even
though pose trees are shared). SUPPRESSION (2026-06-12): the additive head-track fights the
deliberate neck poses in `stomp`/`sprint`, so the behavior now eases curYaw/curPitch to 0 (the
driver's no-offset frame) at 3× rate whenever `IsAnimationActive("stomp"|"sprint")` — neck
returns to the animation's pose immediately, tracking resumes smoothly when they end.
