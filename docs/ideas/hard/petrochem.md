# Vintage Story Petrochem Mod Family: Reseed Summary

## What this is

Design work for a set of Vintage Story mods. Started as a single "nuclear reactor mod" idea, expanded into a family of five stacked mods with the reactor as the capstone. Below is everything decided so far, plus the open questions, so a future conversation can pick up without redoing the thinking.

## Hard constraints (these shape everything)

No electricity, ever. All processes run on heat, rotation (shaft/mechanical power), or fluid movement. Steam is the top-tier prime mover and is tolerated. This isn't a limitation, it forced the design to stay inside Vintage Story's actual vocabulary and it fits a roughly 1890s tech level.

No real pressure simulation. Fluids and gases use a topology-and-state model: connected-and-intact means it flows, breached means it leaks and triggers a hazard. The moment anyone tries to model real pressure/phase/thermodynamics, that's a different and much larger project. This line is what keeps the whole family buildable.

Realism carries the design. We don't invent silly steps. The real industrial processes are already absurd in length and hidden prerequisites, and the humor (for the reactor mod specifically) is in the ratio of effort to payoff. A player who looks up any step should find we weren't exaggerating.

Tedium in moderation. Depth goes where each layer fails in a way that teaches something. Where realism would only add busywork, collapse it to one step.

## The five mods (build order, bottom to top)

1. Plumbing. The foundation. Pipes, connectors, sealed vessels, sources, consumers, and breach/leak behavior. Topology and binary state only, no pressure sim. Gases are just fluids flagged for hazard/containment. Least glamorous, most load-bearing. Nothing chemical works until this exists.

2. Steam. Sits on plumbing. Steam as a pipeable process fluid (reactant and heat source) and as mechanical prime mover. Likely mostly vanilla already; the mod's job is making it usable in the plumbing framework, not reinventing it.

3. Coal Tar. The entry point to the chemistry tree and the first mod with real standalone wealth. The coking oven is the front door: bake coal without air, get coke plus a fan of byproduct streams, each opening a branch — coke (iron/reduction), coke-oven gas (hydrogen + methane), ammoniacal liquor (ammonia), light oil (benzene/aromatics), tar (creosote, naphthalene, phenol, pitch). Highest standalone value; a full industrial-chemistry mod on its own. Stage it: ship coke + hydrogen + ammonia first, add aromatic/tar branches later.

4. Chemical Foundations (working name). The shared toolkit so chemistry machinery isn't reinvented per-mod. Owns: reaction vessels (the general heated input-heat-output workhorse), the corrosion mechanic (vessels degrade holding aggressive contents unless made of the right material), acid production (sulfuric at minimum), common reagent/gas handling, specialty metallurgy (nickel alloy). Boundary rule: if more than one downstream mod shares it, it lives here. THIS BOUNDARY IS THE MOST IMPORTANT UNRESOLVED DECISION in the family and the next thing to work out.

5. Uranium Enrichment and Reactor. The capstone, the only genuinely non-standalone mod. Draws hydrogen + ammonia from Coal Tar, acid + vessels + corrosion-resistant metal + gas handling from Foundations, steam + plumbing from the base. The reframing that makes it work: every supporting stream is sourced from a mod the player already built for other reasons, so the reactor is the demanding customer of an existing industry, not a device with reluctant overhead.

## The reactor fuel spine (fully specified)

Corrected, de-collapsed, chemically honest sequence from rock to bundle:

1. Mine pitchblende (deep-only uranium ore, low assay — lots of rock, little uranium).
2. Crush to dust (existing helve-hammer crusher — clean fit).
3. Acid leach (new stirred vat, consumes sulfuric acid; crushed ore + acid → uranium in solution).
4. Precipitate (add ammonia as precipitant → solid yellowcake falls out; filter and keep solid). This step was initially missed and then added.
5. Front-end conversion, climbing UP by adding fluorine (three reactions, not one):
   - Calcine + reduce under hydrogen → UO2 powder (600–800°C).
   - React UO2 with hydrogen fluoride (HF) gas → UF4 solid ("green salt"); water vapor byproduct.
   - React UF4 with elemental fluorine gas → UF6 gas. Most corrosive/dangerous front-end step.
6. Enrichment. UF6 gas pumped through a cascade of centrifuges. Gas in, gas out, only the U-235 isotope ratio changes — chemistry untouched. Player-scaled: ~4–6 centrifuges = minimum viable fuel, each additional one buys diminishing extra enrichment (more reactor output / longer fuel life). HARD CAP around 12–16 (not 20 — tedium + framerate). The cap also does safety duty: keeps achievable enrichment in the low range by construction.
7. Back-end conversion, climbing DOWN by removing fluorine. Dry route chosen: a shaft-driven heated rotating kiln. Inputs UF6 + steam + hydrogen; outputs UO2 powder + HF gas (captured and recycled to the front-end hydrofluorination step). The rotating kiln is mechanically driven (fits no-electricity) and is a single dramatic recognizable machine.
8. Compression (NEW block — vanilla has no powder press; a mechanically-driven press compacts UO2 powder into fragile "green" pellets).
9. Sintering (existing kiln; fire green pellets to dense ceramic at ~1700°C — the single hottest step in the whole spine).
10. Bundling (load pellets into zirconium cladding tubes, bundle into a fuel assembly).

The fluorine loop is the elegant structural fact: fluorine is added going up the front end and stripped off coming down the back end; the HF from the back-end kiln feeds the front-end hydrofluorination. This ties the two ends of the spine into a loop, not a line — and it's real, not a gameplay invention.

Note: the front end secretly has the same hidden depth as the back end (oxide → UF4 → UF6 is three steps); we chose to model it honestly rather than collapse it.

## Supporting streams / tributaries (all 1890s-honest, no electricity)

- Sulfuric acid: from sulfur, or from fluorite + acid. CRITICAL: acid feeds TWO tributaries — the ore leach AND the fluorine supply — which makes the acid works very central (the "why did I have to build a chemical plant first" beat).
- Fluorine and HF: from fluorite (calcium fluoride) ore + sulfuric acid, plus the recycled HF loop. The three gas streams (H2, HF, F2) are really "one gas-handling iceberg wearing three hats" — build gas handling once, reuse everywhere.
- Hydrogen (no electrolysis — that's electrical): steam-iron process (steam over hot iron → hydrogen + rusted iron, iron regenerated by a reducing gas) is the clean pick because output is just hydrogen + rust. Alternative: water gas (steam over hot coke → H2 + CO) but that needs gas separation. Coke-oven gas is a third source but also needs separation.
- Ammonia (no Haber — that's post-1909): recovered as byproduct of coking coal (ammoniacal liquor scrubbed from coke-oven off-gas). Ties ammonia to coke/iron infrastructure.
- Zirconium cladding line: separate parallel production line converging only at bundling. Zirconium must have hafnium removed or it poisons the reactor's neutrons. THE HAFNIUM LOOP: the hafnium removed here is a strong neutron absorber = exactly what control rods need. Fuel-line waste feeds the control trunk.

## The reactor itself (four trunks + operation)

Four production trunks converge on a multiblock reactor assembly: Fuel (the tall spine above), Vessel (heavy steel shell + refractory liner + lead/concrete shielding — best vanilla fit, wide-and-shallow), Control (absorber rods on a shaft-driven rack/screw drive with a fail-safe drop — nearly pure vanilla mechanical vocabulary), Coolant loop (shaft-driven circulation; if driving power stops, circulation stops).

Reaction model: four player inputs — fuel enrichment (set at fabrication), water level (moderator + coolant), control rod position (primary throttle), and automatic negative temperature feedback (heats up → reaction nudges down → self-stabilizing). Decay heat is a SEPARATE heat source that persists after shutdown and decays over real time, independent of the chain reaction. Bounded to the controllable regime by design — no prompt-criticality modeling. Output heat drives the steam plant. Instrumentation is mechanical gauges (needle-and-dial, like steam pressure gauges), no digital readouts.

## Key physics facts the design rests on (worth keeping straight)

- Water does two unrelated jobs and they fail in OPPOSITE directions. As moderator: losing it is SAFE (neutrons don't slow, reaction stops). As coolant: losing it is the DISASTER. This is the single most important physics fact in the whole tree.
- Fukushima clarified this: the reactors scrammed successfully (chain reaction stopped on the earthquake, before the tsunami). The disaster was DECAY HEAT — fission products keep generating heat for days regardless of the chain reaction, the tsunami killed the cooling pumps, fuel cooked itself. Shutdown ≠ safe. This gives the mod two distinct failure modes from one water mechanic: drain a running core → reaction stops but decay heat melts it if uncooled; pull rods with water present → power runaway.
- "Reduce under hydrogen" = heat in a hydrogen atmosphere; hydrogen strips oxygen off the material and carries it away as water vapor. Same principle as a bloomery (low-oxygen reducing environment). Lands you on UO2 both times it's used.
- Radiological hazard structure (we decided to model ONLY radiation, NOT heavy-metal toxicity, because "no one wants heavy metal toxicity in their reactor mod"): low-enriched uranium is a weak alpha emitter — can't penetrate skin. The ENTIRE front half of the spine (ore through fabrication) is radiologically low-risk; fresh fuel is genuinely handled without heavy shielding. The radiological CLIFF is the reactor: spent fuel coming out is intensely, lethally radioactive (fission products). So: modest radiation upstream, severe radiation only for the operating core and spent fuel. Clean mechanic = no meaningful radiation hazard until the reactor runs.
- Green (unfired) pellets fragility: like a pill-compressor tablet or a dry bouillon cube / compressed chalk — holds shape for careful transfer, but chips/crumbles if dropped or knocked. Not sandcastle-slumping, not pin-drop-shattering. Honest mechanic = breakage risk on careless handling between compression and sintering, NOT a radiation risk.
- Heat gradient across the spine: chemistry runs "warm" (a few hundred °C up to 600–800 for reductions), well below iron-working (1100–1500). The one hot spike is sintering at ~1700°C — higher than iron — landing right at the end, a natural place for the hardest heat requirement.

## Scope: the icebergs, ranked

- Pressure/plumbing simulation: the REAL iceberg, can sink the project. Resolved by the topology-and-state abstraction. Hold this line.
- Gas production/capture (H2, HF, F2): medium iceberg, but it's one system reused three times. Build gas handling once.
- Coal-tar fraction chemistry: a rabbit hole that's only justified if you're building the petrochem family (you are), where it feeds OTHER branches. For the reactor alone it feeds nothing — the reactor only wants H2 and ammonia from coking.
- Nickel-steel alloy: smallest, "a hill not an iceberg." Vanilla already alloys; you add nickel ore + a recipe + the corrosion mechanic.
- The corrosion mechanic and the reaction model: the genuine new systems in the upper mods. Most everything else is content (blocks + recipes).

## Open questions / next decisions

1. THE BIG ONE: the Chemical Foundations boundary — exactly what lives there vs. Coal Tar vs. the Reactor mod. Rule: shared by >1 downstream mod → it goes in Foundations. Acid and reaction vessels clearly qualify; fluorine handling is borderline (depends whether anything but the reactor uses it). This ripples through three of the five mods and is the recommended next topic.
2. VERIFY: does vanilla actually have sulfuric acid? It was guessed to exist (for tanning), but vanilla tanning likely uses limewater/tannin, not sulfuric acid. If acid isn't in vanilla, the acid plant moves from "reuse existing" to "build from scratch," changing that tributary's size. Check the game/wiki before designing around it.
3. Radiation as a status effect: reuse an existing meter (temporal stability, hunger pipeline) or add a dedicated one? Reuse is cheaper/less intrusive.
4. Does steam get its own intermediate tier, or assume the vanilla steam setup as a prerequisite?
5. Enrichment hall size ceiling: centerpiece set-piece vs. voxel-world framerate. Tied to the 12–16 centrifuge cap.
6. Multiblock validation strictness for vessel assembly: too strict frustrates, too loose stops feeling like infrastructure.
7. Gas-handling abstraction: model sealed gas honestly (its own mechanic) vs. treat gas as just another barrel fluid. Honest version fits the mod's tone but is the biggest new system on the fuel spine.

## Artifacts produced so far

- A trunk-level crafting-tree diagram (four trunks + hidden support plant on the vanilla steel/power base).
- reactor-mod-design-doc.md — full fit-gap analysis of the reactor mod against existing vanilla mechanics under the no-electricity stance.
- petrochem-mod-family.md — the five-mod high-level with a dependency/standalone-value/new-systems summary table.

## Design lineage (how the thinking moved, so it isn't re-litigated)

Single reactor mod → realized supporting streams felt like overhead → considered whether coal tar was worth modeling (test: "is it useful to the reactor end goal?" → no, only H2 and ammonia are) → that test revealed the real fork: reactor mod vs. petrochem family → chose petrochem family, which INVERTS the problem: the reactor becomes a capstone customer of an industry the player builds for its own sake, and coal tar's full output slate becomes content instead of distraction. The reactor work already done is preserved intact by this reframing, just recontextualized.
