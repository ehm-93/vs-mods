// ORIGINAL bodies (no drifter kitbash) for three roster mobs:
//   knell   - bell-bearer beast: quadruped arch with the bell slung beneath;
//             the walk swings the clapper, so its gait IS the tolling
//   gloam   - charred stilt-tree: kinked spine, splayed stilts, branch arms,
//             rib-cage crown around a hollow (eyes glow there later)
//   mourner - living horn: low crawler, flared trumpet face, bellows-sac hind
//   node gen-originals.mjs
// Textures reuse the bloat sheets (sac = flesh, rust = metal/char) so no new
// art is needed at concept stage. NOTE: original bodies = original animations
// later (no free drifter anim set).
import { writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const DIR = dirname(fileURLToPath(import.meta.url));

const SAC = { uv: [0, 0, 32, 32], texture: "#sac" };
const RUST = { uv: [1, 1, 15, 15], texture: "#rust" };
const CHAR = { uv: [13, 13, 16, 16], texture: "#rust" }; // near-black corner

function faces(o) {
  const f = {};
  for (const s of ["north", "east", "south", "west", "up", "down"])
    f[s] = { texture: o.texture, uv: [...o.uv] };
  return f;
}

const round3 = (n) => +n.toFixed(3);

function box(name, from, to, mat = SAC, opts = {}) {
  const el = {
    name,
    from: from.map(round3),
    to: to.map(round3),
    rotationOrigin: (opts.origin ?? from).map(round3),
    faces: faces(mat),
    children: [],
  };
  for (const ax of ["X", "Y", "Z"]) {
    const v = opts.rot?.[ax.toLowerCase()];
    if (v) el[`rotation${ax}`] = +v.toFixed(1);
  }
  return el;
}

function doc(elements) {
  return {
    editor: { allAngles: true, entityTextureMode: true },
    textureWidth: 32,
    textureHeight: 32,
    textureSizes: { sac: [32, 32], rust: [16, 16] },
    textures: {
      sac: "worldinterestingmobs:entity/bloat/sac",
      rust: "worldinterestingmobs:entity/bloat/rust",
    },
    elements,
  };
}

// ---- knell: the bell-bearer beast ----
function knell() {
  const els = [
    // vaulted body arch, shoulder hump, rear haunch
    box("Body", [-6, 24, -1], [26, 36, 17]),
    box("Hump", [-8, 27, 0.5], [4, 41, 15.5], SAC, { rot: { z: -6 }, origin: [-8, 27, 8] }),
    box("Haunch", [18, 25, 0.6], [27, 38, 15.4], SAC, { rot: { z: 7 }, origin: [22, 25, 8] }),
    // rust plates along the spine ridge
    box("PlateA", [2, 35, 6], [7, 39, 10], RUST, { rot: { z: -6 }, origin: [2, 35, 8] }),
    box("PlateB", [10, 35.5, 6.5], [15, 39.5, 10.5], RUST),
    box("PlateC", [17, 34, 6], [22, 38, 10], RUST, { rot: { z: 8 }, origin: [17, 34, 8] }),
    // low-slung vestigial head under a rust cowl
    box("Neck", [-8, 27, 6], [-3, 31, 10], SAC, { rot: { z: 10 }, origin: [-3, 29, 8] }),
    box("Skull", [-14, 23, 5.5], [-7, 29, 10.5], SAC, { rot: { z: 18 }, origin: [-7, 26, 8] }),
    box("Cowl", [-15, 28, 5], [-8, 30.5, 11], RUST, { rot: { z: 18 }, origin: [-7, 26, 8] }),
    // four pillar legs, slightly splayed
    box("LegFL", [-6, 0, -2], [0, 26, 3], SAC, { rot: { z: 3, x: -3 }, origin: [-3, 26, 0.5] }),
    box("LegFR", [-6, 0, 13], [0, 26, 18], SAC, { rot: { z: 3, x: 3 }, origin: [-3, 26, 15.5] }),
    box("LegRL", [20, 0, -2], [26, 26, 3], SAC, { rot: { z: -3, x: -3 }, origin: [23, 26, 0.5] }),
    box("LegRR", [20, 0, 13], [26, 26, 18], SAC, { rot: { z: -3, x: 3 }, origin: [23, 26, 15.5] }),
    // the bell, slung beneath the arch between the legs
    box("ChainA", [9.5, 19, 7.4], [10.5, 24.2, 8.6], RUST),
    box("ChainB", [9.6, 17.5, 7.5], [10.4, 19.5, 8.5], RUST),
    box("BellCrown", [7, 15, 5], [13, 19, 11], RUST),
    box("BellWaist", [6, 10, 4], [14, 15.5, 12], RUST),
    box("BellFlare", [5, 5, 3], [15, 10.5, 13], RUST),
    box("BellLip", [4.5, 3.5, 2.5], [15.5, 5.5, 13.5], RUST),
    box("Clapper", [9, 1.5, 7], [11, 4.5, 9], CHAR),
  ];
  return doc(els);
}

// ---- gloam: the charred stilt-tree ----
function gloam() {
  const els = [
    // kinked spine column
    box("Spine1", [7, 14, 7], [9.5, 26, 9.5], CHAR),
    box("Spine2", [6.8, 25, 6.8], [9.2, 36, 9.2], CHAR, { rot: { z: 4 }, origin: [8, 25, 8] }),
    box("Spine3", [7, 35, 7], [9, 44, 9], CHAR, { rot: { z: -5 }, origin: [8, 35, 8] }),
    // rib-cage crown around a hollow
    box("RibA", [6, 42, 6], [7.2, 50, 7.2], CHAR, { rot: { x: -8, z: -8 }, origin: [6.6, 42, 6.6] }),
    box("RibB", [8.8, 42, 8.8], [10, 50, 10], CHAR, { rot: { x: 8, z: 8 }, origin: [9.4, 42, 9.4] }),
    box("RibC", [6, 42, 8.8], [7.2, 50, 10], CHAR, { rot: { x: 8, z: -8 }, origin: [6.6, 42, 9.4] }),
    box("RibD", [8.8, 42, 6], [10, 50, 7.2], CHAR, { rot: { x: -8, z: 8 }, origin: [9.4, 42, 6.6] }),
    box("Hollow", [7.3, 43, 7.3], [8.7, 48, 8.7], RUST), // faint rust core behind the ribs
    // two long branch arms, each two kinked segments
    box("ArmL1", [5.2, 24, 7], [6.6, 37, 8.3], CHAR, { rot: { z: 14 }, origin: [5.9, 36, 7.6] }),
    box("ArmL2", [3.2, 12, 7.1], [4.5, 25, 8.2], CHAR, { rot: { z: 7 }, origin: [3.8, 24, 7.6] }),
    box("TwigL1", [2.6, 7, 7.2], [3.4, 13, 7.9], CHAR, { rot: { z: 12 }, origin: [3, 12, 7.5] }),
    box("TwigL2", [3.4, 8, 7.3], [4.1, 13.5, 8], CHAR, { rot: { z: -9 }, origin: [3.7, 13, 7.6] }),
    box("ArmR1", [9.6, 24, 7.7], [11, 37, 9], CHAR, { rot: { z: -14 }, origin: [10.3, 36, 8.4] }),
    box("ArmR2", [11.6, 13, 7.8], [12.9, 26, 8.9], CHAR, { rot: { z: -7 }, origin: [12.2, 25, 8.4] }),
    box("TwigR1", [12.7, 8, 7.9], [13.5, 14, 8.6], CHAR, { rot: { z: -12 }, origin: [13.1, 13, 8.2] }),
    box("TwigR2", [12, 9, 8], [12.7, 14.5, 8.7], CHAR, { rot: { z: 9 }, origin: [12.3, 14, 8.3] }),
    // four splayed stilt legs
    box("StiltA", [7, 0, 7], [8.4, 16, 8.4], CHAR, { rot: { x: 9, z: 9 }, origin: [7.7, 15, 7.7] }),
    box("StiltB", [7.2, 0, 7.2], [8.6, 16, 8.6], CHAR, { rot: { x: -9, z: 9 }, origin: [7.9, 15, 7.9] }),
    box("StiltC", [6.8, 0, 6.8], [8.2, 16, 8.2], CHAR, { rot: { x: 9, z: -9 }, origin: [7.5, 15, 7.5] }),
    box("StiltD", [7.4, 0, 6.9], [8.8, 16, 8.3], CHAR, { rot: { x: -9, z: -9 }, origin: [8.1, 15, 7.6] }),
  ];
  return doc(els);
}

// ---- mourner: the living horn ----
function mourner() {
  const hornRot = { rot: { z: -8 }, origin: [5, 9.5, 8] };
  const els = [
    // small crouched body
    box("Body", [4, 6, 5], [14, 13, 11]),
    // trumpet face, flaring open forward and slightly skyward - the flare and
    // lip are RING frames so the dark maw shows through from the front
    box("Throat", [0, 7, 6.5], [5, 12, 9.5], SAC),
    box("HornMid", [-4, 6, 5.5], [0.5, 13, 10.5], SAC, hornRot),
    box("FlareTop", [-8, 12.5, 4], [-3.5, 14.5, 12], SAC, hornRot),
    box("FlareBot", [-8, 4.5, 4], [-3.5, 6.5, 12], SAC, hornRot),
    box("FlareL", [-8, 6.5, 4], [-3.5, 12.5, 6], SAC, hornRot),
    box("FlareR", [-8, 6.5, 10], [-3.5, 12.5, 12], SAC, hornRot),
    box("LipTop", [-10.5, 13.5, 3], [-7.5, 15.8, 13], SAC, hornRot),
    box("LipBot", [-10.5, 3.2, 3], [-7.5, 5.5, 13], SAC, hornRot),
    box("LipL", [-10.5, 5.5, 3], [-7.5, 13.5, 5], SAC, hornRot),
    box("LipR", [-10.5, 5.5, 11], [-7.5, 13.5, 13], SAC, hornRot),
    box("Maw", [-9.5, 5.7, 5.2], [1, 13.3, 10.8], CHAR, hornRot), // the dark void it sings from
    // bellows bladder dragging behind, strapped in rust
    box("Bladder", [12.5, 3, 3.5], [23, 12, 12.5]),
    box("StrapA", [14, 11.4, 3.2], [15.2, 12.8, 12.8], RUST),
    box("StrapB", [18.5, 10.9, 3.4], [19.7, 12.3, 12.6], RUST),
    // four thin crooked legs, crouched
    box("LegFL", [2.5, 0, 2.8], [3.9, 8.5, 4.2], CHAR, { rot: { z: 14, x: 10 }, origin: [3.2, 8, 3.5] }),
    box("LegFR", [2.5, 0, 11.8], [3.9, 8.5, 13.2], CHAR, { rot: { z: 14, x: -10 }, origin: [3.2, 8, 12.5] }),
    box("LegRL", [16, 0, 2.6], [17.4, 7, 4], CHAR, { rot: { z: -12, x: 12 }, origin: [16.7, 6.5, 3.3] }),
    box("LegRR", [16, 0, 12], [17.4, 7, 13.4], CHAR, { rot: { z: -12, x: -12 }, origin: [16.7, 6.5, 12.7] }),
  ];
  return doc(els);
}

// gloam graduated to hand-sculpting via the shapes MCP (2026-06-11) - its
// gloam-original.json is now the source of truth; regenerating would clobber
// it, so it is excluded here. The gloam() function above is kept as history.
const MODELS = { knell, mourner };
for (const [name, build] of Object.entries(MODELS)) {
  const out = join(DIR, `${name}-original.json`);
  writeFileSync(out, JSON.stringify(build(), null, "\t"));
  console.log(`${name}-original.json written`);
}
