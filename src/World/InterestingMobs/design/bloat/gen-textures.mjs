// Prints paste_grid text for the bloat textures (run, then paste into the
// pixel editor and export):
//   node gen-textures.mjs
//
// sac 32x32 palette:           rust 16x16 palette:
//  1 #2a211a grime              1 #3e2e1e iron-base
//  2 #44382c flesh-darkest      2 #8a4e1c oxide
//  3 #564a3c flesh-dark         3 #c1742a oxide-bright
//  4 #635648 flesh-base         4 #1f1812 crevice (near-black)
//  5 #71634f flesh-mid          5 #6e6557 metal-glint
//  6 #7d7060 flesh-light        6 #5a3b22 oxide-dark
//  7 #8e7f6b flesh-highlight    7 #2e2218 iron-dark
//  8 #6b3c16 oxide-deep
//  9 #94531d oxide-mid
//  a #c1742a oxide-bright
//  b #d98a3a oxide-hot
//
// rust.png constraints: px (13,13)-(15,15) solid crevice (near-black block,
// sampled by head/legs UV [13,13,16,16]); [2,2,10,10] = spur region.

const SEED = 7;
function mulberry32(a) {
  return function () {
    a |= 0; a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
const rng = mulberry32(SEED);
const r = (lo, hi) => lo + rng() * (hi - lo);
const ch = (i) => (i < 10 ? String(i) : String.fromCharCode(97 + i - 10));

// bilinear value noise on a `cell`-spaced lattice
function noise(w, h, cell) {
  const gw = Math.ceil(w / cell) + 2, gh = Math.ceil(h / cell) + 2;
  const g = Array.from({ length: gh }, () => Array.from({ length: gw }, () => rng()));
  return (x, y) => {
    const fx = x / cell, fy = y / cell;
    const x0 = Math.floor(fx), y0 = Math.floor(fy);
    const tx = fx - x0, ty = fy - y0;
    const s = (a, b, t) => a + (b - a) * (t * t * (3 - 2 * t));
    return s(s(g[y0][x0], g[y0][x0 + 1], tx), s(g[y0 + 1][x0], g[y0 + 1][x0 + 1], tx), ty);
  };
}

// ---------------- sac 32x32 ----------------
const W = 32, H = 32;
const sac = Array.from({ length: H }, () => Array(W).fill(4));
const n1 = noise(W, H, 5), n2 = noise(W, H, 2);

for (let y = 0; y < H; y++) {
  for (let x = 0; x < W; x++) {
    let v = n1(x, y) * 0.7 + n2(x, y) * 0.3;
    // vignette: darker toward edges (fake curvature), extra at the bottom
    const dx = (x - 15.5) / 16, dy = (y - 13) / 16;
    v -= Math.max(0, Math.hypot(dx, dy) - 0.55) * 0.9;
    if (y > 24) v -= (y - 24) * 0.05;
    // highlight zone upper-center
    if (Math.hypot((x - 16) / 10, (y - 7) / 6) < 1) v += 0.12;
    const tone = v < 0.18 ? 2 : v < 0.34 ? 3 : v < 0.58 ? 4 : v < 0.76 ? 5 : v < 0.9 ? 6 : 7;
    sac[y][x] = tone;
  }
}

// vein networks: biased random walks drawing oxide-mid with deep-oxide flanks
function vein(x, y, ang, len, branchDepth) {
  for (let i = 0; i < len; i++) {
    const xi = Math.round(x), yi = Math.round(y);
    if (xi < 0 || yi < 0 || xi >= W || yi >= H) break;
    sac[yi][xi] = 9;
    if (rng() < 0.45) {
      const fx = xi + Math.round(Math.cos(ang + Math.PI / 2));
      const fy = yi + Math.round(Math.sin(ang + Math.PI / 2));
      if (fx >= 0 && fy >= 0 && fx < W && fy < H && sac[fy][fx] !== 9) sac[fy][fx] = 8;
    }
    if (branchDepth > 0 && i > 3 && rng() < 0.12)
      vein(x, y, ang + (rng() < 0.5 ? 1.1 : -1.1), Math.floor(len * 0.55), branchDepth - 1);
    ang += r(-0.45, 0.45);
    x += Math.cos(ang); y += Math.sin(ang);
  }
}
vein(3, 4, 0.8, 26, 2);
vein(27, 2, 2.2, 24, 2);
vein(16, 30, -1.4, 20, 1);

// eruption nodes: hot core + bright ring where growths push through
for (const [cx, cy] of [[8, 12], [23, 18], [14, 25]]) {
  sac[cy][cx] = 11;
  for (const [ox, oy] of [[1, 0], [-1, 0], [0, 1], [0, -1]])
    if (rng() < 0.85) sac[cy + oy][cx + ox] = 10;
  for (const [ox, oy] of [[1, 1], [-1, 1], [1, -1], [-1, -1]])
    if (rng() < 0.5) sac[cy + oy][cx + ox] = 9;
}

// grime pits
for (let i = 0; i < 7; i++) {
  const x = Math.floor(r(1, W - 1)), y = Math.floor(r(1, H - 1));
  sac[y][x] = 1;
  if (rng() < 0.5) sac[y][x + 1] = 2;
}

// ---------------- rust 16x16 ----------------
const RW = 16, RH = 16;
const rust = Array.from({ length: RH }, () => Array(RW).fill(1));
const rn = noise(RW, RH, 3);

for (let y = 0; y < RH; y++) {
  for (let x = 0; x < RW; x++) {
    // diagonal corrosion strata
    const band = Math.floor((x + y + rn(x, y) * 3) / 3) % 4;
    rust[y][x] = [1, 6, 7, 6][band];
    // oxide blooming along band boundaries
    const pos = (x + y + rn(x, y) * 3) % 3;
    if (pos < 0.7 && rng() < 0.6) rust[y][x] = 2;
    if (pos < 0.35 && rng() < 0.25) rust[y][x] = 3;
    if (rng() < 0.05) rust[y][x] = 5; // sparse metallic glints
  }
}
// crevice cracks
function crack(x, y, ang, len) {
  for (let i = 0; i < len; i++) {
    const xi = Math.round(x), yi = Math.round(y);
    if (xi < 0 || yi < 0 || xi >= RW || yi >= RH) break;
    rust[yi][xi] = 4;
    ang += r(-0.5, 0.5);
    x += Math.cos(ang); y += Math.sin(ang);
  }
}
crack(1, 5, -0.4, 13);
crack(10, 0, 1.8, 12);
// reserved near-black block for head/leg UVs
for (let y = 13; y < 16; y++) for (let x = 13; x < 16; x++) rust[y][x] = 4;

// ---------------- print ----------------
console.log("=== SAC 32x32 ===");
console.log(sac.map((row) => row.map(ch).join("")).join("\n"));
console.log("=== RUST 16x16 ===");
console.log(rust.map((row) => row.map(ch).join("")).join("\n"));
