// Generates gibbet pose variants from the VANILLA surface drifter: a corpse
// hanging from a rusted chain. Three candidate poses to pick from:
//   limp     - pure decor: vertical dangle, snapped neck, arms at sides
//   grip     - the tell for the attentive: one arm raised, holding the chain
//   withered - desiccated: thinned limbs, twisted, chin on chest
//   node gen-gibbet.mjs
// Output: gibbet-{limp,grip,withered}.json next to this script (design only;
// the picked pose gets promoted into assets/ later).
//
// Frame: drifter faces -X; Body-local +x = its back. Chain texture borrows
// the bloat's rust sheet.
import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const DIR = dirname(fileURLToPath(import.meta.url));
const VANILLA =
  "C:/Users/emmet/AppData/Roaming/Vintagestory/assets/survival/shapes/entity/lore/drifter/surface.json";

const RUST_FULL = [1, 1, 15, 15];

function faces(uv, texture) {
  const f = {};
  for (const s of ["north", "east", "south", "west", "up", "down"])
    f[s] = { texture, uv: [...uv] };
  return f;
}

const round3 = (n) => +n.toFixed(3);

function box(name, from, to, opts = {}) {
  const el = {
    name,
    from: from.map(round3),
    to: to.map(round3),
    rotationOrigin: (opts.origin ?? from).map(round3),
    faces: faces(opts.uv ?? RUST_FULL, opts.texture ?? "#rust"),
    children: [],
  };
  for (const ax of ["X", "Y", "Z"]) {
    const v = opts.rot?.[ax.toLowerCase()];
    if (v) el[`rotation${ax}`] = +v.toFixed(1);
  }
  return el;
}

function findEl(list, name) {
  for (const el of list) {
    if (el.name === name) return el;
    const hit = findEl(el.children ?? [], name);
    if (hit) return hit;
  }
  return null;
}

// Replace an element's static rotation outright (animations add on top).
function repose(doc, name, rot) {
  const el = findEl(doc.elements, name);
  if (!el) throw new Error(`no element ${name}`);
  for (const ax of ["X", "Y", "Z"]) {
    const v = rot[ax.toLowerCase()];
    if (v) el[`rotation${ax}`] = v;
    else delete el[`rotation${ax}`];
  }
}

function scaleEl(doc, name, s) {
  const el = findEl(doc.elements, name);
  el.scaleX = s;
  el.scaleZ = s;
}

// Limp hanged pose - everything dangles plumb, neck snapped sideways.
const LIMP = {
  "Body": { z: 4 },
  "Abdomen": { z: -2 },
  "Neck": { z: 6 },
  "Head": { x: 15, z: -55 },
  "Kupris": { z: 6 },
  "L shoulder": { x: 6, z: 2 },
  "R shoulder": { x: -6, z: 2 },
  "L upper arm": { x: -4, z: -2 },
  "R upper arm": { x: 4, z: -2 },
  "L lower arm": { x: -4 },
  "R lower arm": { x: 4 },
  "L hand": { x: -6, z: -4 },
  "R hand": { x: 6, z: -4 },
  "L thigh": { x: -2, z: -3 },
  "R thigh": { x: 2, z: -3 },
  "L feet": { x: 25, z: 3 },
  "R feet": { x: -25, z: 3 },
};

const VARIANTS = {
  limp: { pose: { ...LIMP } },
  grip: {
    pose: {
      ...LIMP,
      "L shoulder": { x: 8, z: 168 },
      "L lower arm": { x: -25, z: -8 },
      "L hand": { x: -15 },
      "Head": { x: -10, z: -18 },
      "L thigh": { x: -30, z: -6 },
      "R thigh": { x: -18, z: -4 },
      "L feet": { x: 30, z: 3 },
      "R feet": { x: -30, z: 3 },
    },
  },
  withered: {
    pose: { ...LIMP, "Body": { y: 25, z: 4 }, "Head": { x: 25, z: -62 } },
    thin: ["L shoulder", "R shoulder", "L thigh", "R thigh", "Neck"],
  },
};

// Chain rising from above the head (head top ~y21), plus a noose ring at the
// neck. Root-level so it stays plumb regardless of body pose.
function chainAndNoose() {
  const els = [];
  const cx = 5.3, cz = 8.0;
  for (let i = 0; i < 8; i++) {
    const y = 20.8 + i * 1.6;
    els.push(
      i % 2 === 0
        ? box(`ChainLink${i}`, [cx - 0.45, y, cz - 0.18], [cx + 0.45, y + 1.9, cz + 0.18])
        : box(`ChainLink${i}`, [cx - 0.18, y, cz - 0.45], [cx + 0.18, y + 1.9, cz + 0.45])
    );
  }
  // noose: four thin sides ringing the neck
  els.push(box("NooseW", [4.0, 18.4, 6.6], [4.5, 19.2, 9.4]));
  els.push(box("NooseE", [8.1, 18.4, 6.6], [8.6, 19.2, 9.4]));
  els.push(box("NooseN", [4.2, 18.4, 6.2], [8.4, 19.2, 6.7]));
  els.push(box("NooseS", [4.2, 18.4, 9.3], [8.4, 19.2, 9.8]));
  return els;
}

const vanilla = JSON.parse(readFileSync(VANILLA, "utf8"));

for (const [name, cfg] of Object.entries(VARIANTS)) {
  const doc = structuredClone(vanilla);

  // pin vanilla texture paths to the game domain (mod-domain gotcha)
  for (const [k, v] of Object.entries(doc.textures))
    if (!v.includes(":")) doc.textures[k] = `game:${v}`;
  doc.textures.rust = "worldinterestingmobs:entity/bloat/rust";
  doc.textureSizes ??= {};
  doc.textureSizes.rust = [16, 16];

  for (const [el, rot] of Object.entries(cfg.pose)) repose(doc, el, rot);
  for (const el of cfg.thin ?? []) scaleEl(doc, el, 0.7);
  doc.elements.push(...chainAndNoose());

  const out = join(DIR, `gibbet-${name}.json`);
  writeFileSync(out, JSON.stringify(doc, null, "\t"));
  console.log(`gibbet-${name}.json written`);
}
