// Generates the three bloat tier shapes from the VANILLA drifter shape.
// Source of truth for the whole model: re-run after any config tweak.
//   node gen-bloat.mjs
// Output: bloat-{small,large,massive}.json next to this script.
//
// Frame (Body-local, 1/16 block): +x = host's back (up while crawling),
// -y = rear, z = sides. Host torso x 0..5, y 0..6, z 0..7.2. Down = -x.
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const DIR = dirname(fileURLToPath(import.meta.url));
// shapes are written straight into the mod's asset tree
const OUT = join(DIR, "../../assets/worldinterestingmobs/shapes/entity/bloat");
// Host drifter shape per tier (vanilla tiering: normal/deep=surface,
// tainted=spiked1, corrupt=deerhorn, nightmare=knife)
const DRIFTER_SHAPES =
  "C:/Users/emmet/AppData/Roaming/Vintagestory/assets/survival/shapes/entity/lore/drifter";
const SEED = 1337;

// ---------- seeded rng ----------
function mulberry32(a) {
  return function () {
    a |= 0; a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
let rng = mulberry32(SEED);
const r = (lo, hi) => lo + rng() * (hi - lo);
const rsign = () => (rng() < 0.5 ? -1 : 1);
const jit = (v, j) => v + r(-j, j);

// ---------- texture UV regions ----------
const SAC_FULL = [0, 0, 32, 32]; // full mottled flesh (sac.png is 32x32, px space)
const RUST_DARK = [13, 13, 16, 16]; // near-black corner of rust.png (head, legs)
const RUST_PLATE = [1, 1, 15, 15];
// vanilla metal-spike texel strip on the spiked1 drifter sheet (64-unit UV
// space) - same texels the tainted drifter's chest/back spikes use
const SPIKE_UV = [12, 30, 12.5, 32];

function faces(uv, texture, randomRot = false) {
  const f = {};
  for (const s of ["north", "east", "south", "west", "up", "down"]) {
    f[s] = { texture, uv: [...uv] };
    if (randomRot) {
      const rot = [0, 90, 180, 270][Math.floor(rng() * 4)];
      if (rot) f[s].rotation = rot;
    }
  }
  return f;
}

const round3 = (n) => +n.toFixed(3);

function box(name, from, to, opts = {}) {
  const el = {
    name,
    from: from.map(round3),
    to: to.map(round3),
    rotationOrigin: (opts.origin ?? from).map(round3),
    faces: faces(opts.uv, opts.texture, opts.randomRot),
    children: [],
  };
  for (const ax of ["X", "Y", "Z"]) {
    const v = opts.rot?.[ax.toLowerCase()];
    if (v) el[`rotation${ax}`] = +v.toFixed(1);
  }
  return el;
}

// Box elongated along axis ('x+'..'z-'); base = center of base face.
function boxAlong(axis, base, L, t) {
  const [b0, b1, b2] = base, h = t / 2;
  switch (axis) {
    case "x+": return { from: [b0, b1 - h, b2 - h], to: [b0 + L, b1 + h, b2 + h], tip: [L, h, h] };
    case "x-": return { from: [b0 - L, b1 - h, b2 - h], to: [b0, b1 + h, b2 + h], tip: [0, h, h] };
    case "y+": return { from: [b0 - h, b1, b2 - h], to: [b0 + h, b1 + L, b2 + h], tip: [h, L, h] };
    case "y-": return { from: [b0 - h, b1 - L, b2 - h], to: [b0 + h, b1, b2 + h], tip: [h, 0, h] };
    case "z+": return { from: [b0 - h, b1 - h, b2], to: [b0 + h, b1 + h, b2 + L], tip: [h, h, L] };
    case "z-": return { from: [b0 - h, b1 - h, b2 - L], to: [b0 + h, b1 + h, b2], tip: [h, h, 0] };
  }
}

// Chain of segments; each segment is a CHILD of the previous, so kink
// rotations compound down the chain. segments: {axis, L, t, rot}.
function chain(name, base, segments, uv, texture = "#rust") {
  let root = null, parent = null, prevGeo = null, prevSeg = null;
  segments.forEach((seg, i) => {
    let segBase;
    if (i === 0) segBase = base;
    else {
      const tl = [...prevGeo.tip];
      const k = prevSeg.axis[0] === "x" ? 0 : prevSeg.axis[0] === "y" ? 1 : 2;
      tl[k] += prevSeg.axis[1] === "+" ? -0.3 : 0.3; // overlap joint
      segBase = tl;
    }
    const g = boxAlong(seg.axis, segBase, seg.L, seg.t);
    const el = box(`${name}s${i}`, g.from, g.to, { origin: segBase, rot: seg.rot, uv, texture });
    if (parent) parent.children.push(el); else root = el;
    parent = el; prevGeo = g; prevSeg = seg;
  });
  return root;
}

// Papilloma in the vanilla tainted-drifter spike style: razor-thin (vanilla
// spikes are 0.5x0.5) segment chains with violent kinks, textured to the
// SAME metal-spike texel strip the spiked1 host uses.
function papilloma(name, anchor, axis, scale) {
  const n = rng() < 0.3 ? 3 : rng() < 0.75 ? 4 : 5;
  const perp = { x: ["y", "z"], y: ["x", "z"], z: ["x", "y"] }[axis[0]];
  let t = r(0.5, 0.75) * Math.sqrt(scale), L = r(1.6, 2.8) * scale;
  const segs = [];
  for (let i = 0; i < n; i++) {
    const rot = {};
    rot[perp[0]] = i === 0 ? r(-20, 20) : rsign() * r(25, 65);
    rot[perp[1]] = i === 0 ? r(-20, 20) : rsign() * r(15, 45);
    segs.push({ axis, L, t, rot });
    L *= r(0.75, 0.95);
    t *= r(0.85, 0.95);
  }
  return chain(name, anchor.map((v) => jit(v, 0.5)), segs, SPIKE_UV, "#spike");
}

// Spindly 4-segment tick leg (coxa -> femur -> tibia -> tarsus), black.
// cfg.rots is the per-segment kink schedule; thickness tapers down the chain.
function leg(name, cfg, scale) {
  const taper = [1, 0.82, 0.64, 0.5];
  const segs = cfg.Ls.map((L, i) => ({
    axis: cfg.axis,
    L: L * scale,
    t: cfg.t * taper[i] * scale,
    rot: { ...cfg.rots[i] },
  }));
  for (const s of segs)
    for (const k of Object.keys(s.rot)) s.rot[k] = jit(s.rot[k], 4);
  return chain(name, cfg.anchor, segs, RUST_DARK);
}

// 4 legs, all by the head, held TIGHT against the host (small splay, fast
// down-curl so the tips press into the host's body):
//   pair 1 reaches forward over the host's shoulders and curls down against
//   the chest (axis y+, rotZ steps: +z sends +y toward -x = down);
//   pair 2 wraps the torso flanks (axis z+-, rotY steps; ry+ = down for z-,
//   ry- = down for z+).
function buildLegs(tier) {
  const a = tier.legAnchors; // { x, yShoulder, yTorso, zL, zR }
  const { Ls, t } = tier.legLen;
  return [
    leg("LegL1", { anchor: [a.x, a.yShoulder, a.zL], axis: "y+", Ls, t, rots: [{ x: -12, z: 24 }, { z: 34 }, { z: 44 }, { z: 28 }] }, tier.scale),
    leg("LegR1", { anchor: [a.x, a.yShoulder, a.zR], axis: "y+", Ls, t, rots: [{ x: 12, z: 24 }, { z: 34 }, { z: 44 }, { z: 28 }] }, tier.scale),
    leg("LegL2", { anchor: [a.x - 1, a.yTorso, a.zL], axis: "z-", Ls, t, rots: [{ y: -20 }, { y: 34 }, { y: 46 }, { y: 28 }] }, tier.scale),
    leg("LegR2", { anchor: [a.x - 1, a.yTorso, a.zR], axis: "z+", Ls, t, rots: [{ y: 20 }, { y: -34 }, { y: -46 }, { y: -28 }] }, tier.scale),
  ];
}

// Black capitulum clamped against the host's nape, with two palps
// reaching down onto the host's back.
function head(cfg) {
  const [x0, y0, z0] = cfg.from, [, , z1] = cfg.to;
  const pw = 0.8 * cfg.scale, pl = 1.8 * cfg.scale;
  return [
    box("CapHead", cfg.from, cfg.to, { uv: RUST_DARK, texture: "#rust" }),
    box("PalpL", [x0 - pl, y0 + 0.3, z0 - pw + 0.2], [x0 + 0.4, y0 + 0.3 + pw, z0 + 0.2],
      { origin: [x0 + 0.4, y0 + 0.3, z0 + 0.2], rot: { z: -16, y: 14 }, uv: RUST_DARK, texture: "#rust" }),
    box("PalpR", [x0 - pl, y0 + 0.3, z1 - 0.2], [x0 + 0.4, y0 + 0.3 + pw, z1 + pw - 0.2],
      { origin: [x0 + 0.4, y0 + 0.3, z1 - 0.2], rot: { z: -16, y: -14 }, uv: RUST_DARK, texture: "#rust" }),
  ];
}

// ---------- tier configs ----------
const TIERS = {
  small: {
    scale: 0.8, host: "surface",
    stoop: 22, limbScale: 0.75, neckScale: 0.85,
    mass: [
      ["MassCore", [3, -5, 1], [10.5, 3.5, 6.2]],
      ["MassDome", [5.5, -6, 1.8], [12, 1, 5.4]],
      ["MassPeak", [8.5, -6.8, 2.4], [11.8, -2, 5]],
      ["MassLobeL", [4, -4.5, -0.7], [10, 2, 1.4]],
      ["MassLobeR", [4, -4.5, 5.8], [10, 2, 7.9]],
    ],
    massRear: [[2.5, -3, 0.8], [8.5, 3.5, 6.2]],
    plates: [],
    head: { from: [2.4, 3.2, 2.8], to: [5.6, 6.0, 4.6], scale: 0.8 },
    legAnchors: { x: 5.0, yShoulder: 4.4, yTorso: 1.2, zL: 2.0, zR: 5.2 },
    legLen: { Ls: [1.7, 1.5, 1.5, 0.9], t: 0.62 },
    spurs: [
      [[11.0, -1.0, 3.2], "x+"],
      [[10.2, -3.8, 4.6], "x+"],
      [[9.2, -6.3, 3.6], "y-"],
      [[8.6, -2.0, 7.6], "z+"],
      [[8.4, 0.3, -0.4], "z-"],
    ],
  },
  large: {
    scale: 1.0, host: "spiked1",
    stoop: 26, limbScale: 0.6, neckScale: 0.75,
    mass: [
      ["MassCore", [3, -7, 0.2], [12.5, 4, 7]],
      ["MassDome", [6, -8, 1.2], [14.5, 1.5, 6]],
      ["MassPeak", [10, -8.5, 2], [14.2, -2.5, 5.5]],
      ["MassLobeL", [4, -6.5, -1.7], [11.5, 2.5, 1]],
      ["MassLobeR", [4, -6.5, 6.2], [11.5, 2.5, 8.9]],
    ],
    massRear: [[2, -4.5, 0.2], [10.5, 4.5, 6.8]],
    plates: [
      ["PlateA", [13.9, -3.5, 2.6], [14.6, -1.2, 5.2]],
      ["PlateB", [10.5, -9.2, 2.8], [12.5, -8.3, 5]],
    ],
    head: { from: [2.6, 3.2, 2.6], to: [6.2, 6.4, 4.8], scale: 1.0 },
    legAnchors: { x: 5.2, yShoulder: 4.6, yTorso: 1.2, zL: 1.9, zR: 5.3 },
    legLen: { Ls: [2.0, 1.8, 1.8, 1.1], t: 0.68 },
    spurs: [
      [[13.8, -4.5, 3.4], "x+"],
      [[14.0, -1.0, 4.6], "x+"],
      [[13.2, 1.8, 2.6], "x+"],
      [[12.6, -6.2, 5.6], "x+"],
      [[11.5, -8.0, 3.2], "y-"],
      [[9.8, -8.3, 5.2], "y-"],
      [[9.4, -2.5, 8.6], "z+"],
      [[8.6, -4.5, -1.5], "z-"],
    ],
  },
  massive: {
    scale: 1.25, host: "deerhorn",
    stoop: 30, limbScale: 0.5, neckScale: 0.6,
    mass: [
      ["MassCore", [2.5, -9, -0.8], [14, 4.5, 8]],
      ["MassDome", [5, -10, 0.5], [17, 2, 6.7]],
      ["MassPeak", [10, -11, 1.4], [16.5, -2.5, 6]],
      ["MassLobeL", [3.5, -8, -3], [13, 3, 1.5]],
      ["MassLobeR", [3.5, -8, 5.7], [13, 3, 10.2]],
    ],
    massRear: [[1.5, -6, -0.5], [12, 5, 7.5]],
    plates: [
      ["PlateA", [16.4, -4.5, 2.4], [17.3, -1.5, 5.6]],
      ["PlateB", [12, -11.8, 2.6], [14.5, -10.7, 5.4]],
      ["PlateC", [10.5, -6, 9.9], [12.8, -3.8, 10.8]],
    ],
    head: { from: [2.8, 3.4, 2.4], to: [7.0, 7.0, 5.2], scale: 1.2 },
    legAnchors: { x: 5.6, yShoulder: 4.8, yTorso: 1.0, zL: 1.7, zR: 5.5 },
    legLen: { Ls: [2.4, 2.2, 2.2, 1.3], t: 0.78 },
    spurs: [
      [[16.4, -5.5, 3.4], "x+"],
      [[16.6, -2.0, 5.0], "x+"],
      [[16.0, 1.5, 2.8], "x+"],
      [[15.2, -8.0, 4.4], "x+"],
      [[14.8, 3.4, 6.0], "x+"],
      [[13.0, -10.6, 3.0], "y-"],
      [[11.0, -10.9, 5.6], "y-"],
      [[9.6, -12.0, 4.0], "y-"],
      [[10.4, -3.0, 9.9], "z+"],
      [[12.0, -6.5, 9.7], "z+"],
      [[9.8, -5.0, -2.8], "z-"],
      [[12.4, -1.5, -2.6], "z-"],
    ],
  },
};

// ---------- doc helpers ----------
function findEl(list, name) {
  for (const el of list) {
    if (el.name === name) return el;
    const hit = findEl(el.children ?? [], name);
    if (hit) return hit;
  }
  return null;
}

// ---------- main ----------
for (const [tierName, tier] of Object.entries(TIERS)) {
  rng = mulberry32(SEED + tierName.length * 101);
  const doc = JSON.parse(readFileSync(`${DRIFTER_SHAPES}/${tier.host}.json`, "utf8"));

  // The vanilla shape references its textures UNPREFIXED, which inside our
  // mod's domain would resolve to worldinterestingmobs:* and silently break
  // in-game - pin them to the game domain.
  for (const [k, v] of Object.entries(doc.textures))
    if (!v.includes(":")) doc.textures[k] = `game:${v}`;

  // textures (px UV space via textureSizes; spike = the vanilla tainted
  // drifter sheet so papillomas share the host family's spike texels)
  doc.textures.sac = "worldinterestingmobs:entity/bloat/sac";
  doc.textures.rust = "worldinterestingmobs:entity/bloat/rust";
  doc.textures.spike = "game:entity/lore/drifter/spiked1";
  doc.textureSizes ??= {};
  doc.textureSizes.sac = [32, 32];
  doc.textureSizes.rust = [16, 16];
  doc.textureSizes.spike = [64, 64];

  const body = findEl(doc.elements, "Body");
  const abdomen = findEl(doc.elements, "Abdomen");

  // posture + host consumption
  body.rotationZ = tier.stoop;
  for (const n of ["L thigh", "R thigh", "L shoulder", "R shoulder"]) {
    const el = findEl(doc.elements, n);
    el.scaleX = tier.limbScale;
    el.scaleZ = tier.limbScale;
  }
  const neck = findEl(doc.elements, "Neck");
  neck.scaleX = tier.neckScale;
  neck.scaleZ = tier.neckScale;

  // the parasite body
  for (const [name, from, to] of tier.mass)
    body.children.push(box(name, from, to, { uv: SAC_FULL, texture: "#sac", randomRot: true }));
  abdomen.children.push(box("MassRear", ...tier.massRear, { uv: SAC_FULL, texture: "#sac", randomRot: true }));
  for (const [name, from, to] of tier.plates)
    body.children.push(box(name, from, to, { uv: RUST_PLATE, texture: "#rust" }));

  // parasite anatomy
  body.children.push(...head(tier.head));
  body.children.push(...buildLegs(tier));
  tier.spurs.forEach(([a, axis], i) =>
    body.children.push(papilloma(`Spur${String(i + 1).padStart(2, "0")}`, a, axis, tier.scale))
  );

  mkdirSync(OUT, { recursive: true });
  const out = join(OUT, `${tierName}.json`);
  writeFileSync(out, JSON.stringify(doc, null, "\t"));
  console.log(`${tierName}.json: host=${tier.host}, ${tier.spurs.length} papillomas, 4 legs, head+palps, ${tier.mass.length + 1} mass boxes`);
}
