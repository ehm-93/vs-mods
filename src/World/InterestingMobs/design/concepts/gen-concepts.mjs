// Silhouette concepts for the next mobs, generated from the vanilla surface
// drifter: knell (bell-carrier giant), gloam (tall thin watcher), mourner
// (split-jaw lure). One look each - picked directions get their own dir and
// evolved generator like the bloat/gibbet.
//   node gen-concepts.mjs
// The knell's bell is box-built here; production should graft the real
// entity/lore/bell/bell geometry (it carries one of THOSE bells).
import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const DIR = dirname(fileURLToPath(import.meta.url));
const VANILLA =
  "C:/Users/emmet/AppData/Roaming/Vintagestory/assets/survival/shapes/entity/lore/drifter/surface.json";

const RUST_FULL = [1, 1, 15, 15];
const RUST_DARK = [13, 13, 16, 16];
const SAC_FULL = [0, 0, 32, 32];

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

function repose(doc, name, rot) {
  const el = findEl(doc.elements, name);
  for (const ax of ["X", "Y", "Z"]) {
    const v = rot[ax.toLowerCase()];
    if (v) el[`rotation${ax}`] = v;
    else delete el[`rotation${ax}`];
  }
}

function scaleEl(doc, name, sx, sy, sz) {
  const el = findEl(doc.elements, name);
  if (sx !== 1) el.scaleX = sx;
  if (sy !== 1) el.scaleY = sy;
  if (sz !== 1) el.scaleZ = sz;
}

function loadBase() {
  const doc = JSON.parse(readFileSync(VANILLA, "utf8"));
  for (const [k, v] of Object.entries(doc.textures))
    if (!v.includes(":")) doc.textures[k] = `game:${v}`;
  doc.textures.rust = "worldinterestingmobs:entity/bloat/rust";
  doc.textures.sac = "worldinterestingmobs:entity/bloat/sac";
  doc.textureSizes ??= {};
  doc.textureSizes.rust = [16, 16];
  doc.textureSizes.sac = [32, 32];
  return doc;
}

// ---- knell: hunched giant, small head, yoke across the shoulders with a
// massive corroded bell hanging behind the hump ----
function knell() {
  const doc = loadBase();
  const body = findEl(doc.elements, "Body");

  scaleEl(doc, "Body", 1.35, 1.15, 1.3); // bulk the whole frame
  scaleEl(doc, "Neck", 0.7, 0.85, 0.7); // tiny head on the giant
  repose(doc, "Body", { z: 24 });

  body.children.push(
    box("Yoke", [1.2, 6.0, -3.2], [3.8, 8.0, 10.4]),
    box("BellArm", [2.2, 6.4, 2.8], [10.0, 7.6, 4.4]),
    box("BellChain1", [8.4, 4.9, 3.2], [9.2, 6.5, 4.0]),
    box("BellChain2", [8.55, 3.4, 3.05], [9.05, 5.0, 4.15]),
    box("BellCrown", [7.7, 2.6, 2.5], [9.9, 3.6, 4.7]),
    box("BellWaist", [7.3, 1.0, 2.1], [10.3, 2.8, 5.1]),
    box("BellFlare", [6.9, -0.6, 1.7], [10.7, 1.2, 5.5]),
    box("BellLip", [6.7, -1.2, 1.5], [10.9, -0.4, 5.7]),
    box("BellClapper", [8.5, -1.8, 3.3], [9.1, -0.6, 3.9], { uv: RUST_DARK })
  );
  return doc;
}

// ---- gloam: stretched thin and upright, arms to the knees, watcher ----
function gloam() {
  const doc = loadBase();
  const body = findEl(doc.elements, "Body");

  body.rotationOrigin = [8, 0, 7.9]; // stretch up from the ground
  repose(doc, "Body", { z: 2 });
  scaleEl(doc, "Body", 0.65, 1.55, 0.65);
  scaleEl(doc, "Neck", 1.0, 0.8, 1.0);
  scaleEl(doc, "L shoulder", 0.8, 1.3, 0.8);
  scaleEl(doc, "R shoulder", 0.8, 1.3, 0.8);

  repose(doc, "Neck", { z: 8 });
  repose(doc, "Head", { z: -8 });
  repose(doc, "L shoulder", { x: 6, z: 2 });
  repose(doc, "R shoulder", { x: -6, z: 2 });
  repose(doc, "L upper arm", { x: -4, z: -4 });
  repose(doc, "R upper arm", { x: 4, z: -4 });
  repose(doc, "L lower arm", { x: -4 });
  repose(doc, "R lower arm", { x: 4 });
  repose(doc, "L thigh", { x: -2, z: -4 });
  repose(doc, "R thigh", { x: 2, z: -4 });
  repose(doc, "L feet", { x: 4, z: 8 });
  repose(doc, "R feet", { x: -4, z: 8 });
  return doc;
}

// ---- mourner: deep hunch, jaw split open into a living horn, throat sac ----
function mourner() {
  const doc = loadBase();
  const neck = findEl(doc.elements, "Neck");

  repose(doc, "Body", { z: 34 });
  // crane the head up and back - it calls with its throat to the sky
  repose(doc, "Neck", { z: -22 });
  repose(doc, "Head", { z: -12 });
  scaleEl(doc, "L hand", 1, 1.6, 1);
  scaleEl(doc, "R hand", 1, 1.6, 1);

  neck.children.push(
    // mandibles hinged at the head base, splayed open
    box("JawL", [-1.2, 3.2, 0.6], [3.2, 4.6, 1.9], { rot: { x: -38 }, origin: [1, 4.6, 0.6], uv: [0, 0, 12, 4], texture: "#skin" }),
    box("JawR", [-1.2, 3.2, 2.2], [3.2, 4.6, 3.5], { rot: { x: 38 }, origin: [1, 4.6, 3.5], uv: [0, 0, 12, 4], texture: "#skin" }),
    box("JawDown", [-1.6, 2.6, 1.0], [2.8, 3.8, 3.0], { rot: { z: -50 }, origin: [2.8, 3.8, 2.0], uv: [0, 0, 12, 4], texture: "#skin" }),
    // the hollow - near-black void where the voice comes from
    box("Hollow", [-0.8, 3.4, 1.2], [2.6, 5.2, 2.8], { uv: RUST_DARK }),
    // distended resonator sac at the throat
    box("ThroatSac", [0.2, -1.5, 0.2], [4.6, 2.8, 3.8], { uv: SAC_FULL, texture: "#sac" })
  );
  return doc;
}

const CONCEPTS = { knell, gloam, mourner };
for (const [name, build] of Object.entries(CONCEPTS)) {
  const out = join(DIR, `${name}.json`);
  writeFileSync(out, JSON.stringify(build(), null, "\t"));
  console.log(`${name}.json written`);
}
