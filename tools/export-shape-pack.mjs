// Builds Assets/DroneStar/Data/ShapePack.bytes from the Draw_in_3D formation library
// (web/src/formation-assets.js: Blender models sampled as 4096 coloured points in farthest-point order,
// so every prefix is evenly spread).
//
// A 4096-drone show cannot light every drone from exactly 4096 points (the spacing filter always drops a
// few), so each model is densified: midpoints between near neighbours (never across gaps between separate
// parts) are added, in rounds until the pool is deep enough, and the union is re-ordered by farthest-point
// sampling and cut to POINTS (three times the largest fleet: greedy spacing packs well only while it uses a
// small part of the pool), keeping the
// "every prefix is evenly spread" property.
//
// usage: node tools/export-shape-pack.mjs [path/to/Draw_in_3D]
//
// Pack format (little-endian):
//   "DSSP" | u16 version=1 | u16 shapeCount
//   per shape: u8 nameLength | name (UTF-8) | u16 pointCount | f32 min[3] | f32 max[3]
//              | i16 positions[pointCount*3] | u8 colours[pointCount*3]
// Positions are in the studio's axes (x right, y up, z away from the audience), so z is flipped from
// Draw_in_3D's right-handed "z toward the audience" convention. Colours are sRGB LED colours with
// Draw_in_3D's baked audience shading.
import { writeFileSync, mkdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const source = resolve(process.argv[2] || join(here, "..", "..", "Draw_in_3D"));
const assets = (await import(pathToFileURL(join(source, "web", "src", "formation-assets.js")).href)).default;

const POINTS = +(process.env.POINTS || 24576);

// The curated models, in picker order. Listed explicitly so shapes added to Draw_in_3D later do not change the
// pack (and with it every baked assignment) until they are chosen here.
const MODELS = ["Robot", "Fish", "Butterfly", "Hot air balloon", "Eiffel Tower", "Big ship", "Whale", "Firework star",
  "Row of fire", "Birthday cake", "Starship launch", "Happy day"];

// Shapes whose flames are part of the picture by default in the Draw_in_3D demo.
const withFire = new Set(["Row of fire", "Starship launch"]);

function merged(name, f) {
  const body = f.body.positions.map((p, i) => ({ p, c: f.body.colors[i] }));
  if (!withFire.has(name)) return body;
  const fire = f.fire.positions.map((p, i) => ({ p, c: f.fire.colors[i] }));
  // Interleave one flame point per seven body points so any prefix keeps the same mix.
  const out = [];
  let b = 0, k = 0;
  while (b < body.length || k < fire.length) {
    for (let r = 0; r < 7 && b < body.length; r++) out.push(body[b++]);
    if (k < fire.length) out.push(fire[k++]);
  }
  return out.slice(0, 4096);
}

// k nearest neighbours by brute force over typed arrays (n is at most a few tens of thousands).
function nearestNeighbours(X, n, K) {
  const idx = new Int32Array(n * K).fill(-1), dist = new Float64Array(n * K).fill(Infinity);
  for (let i = 0; i < n; i++) {
    const xi = X[3 * i], yi = X[3 * i + 1], zi = X[3 * i + 2], o = i * K;
    for (let j = 0; j < n; j++) {
      if (j === i) continue;
      const dx = X[3 * j] - xi, dy = X[3 * j + 1] - yi, dz = X[3 * j + 2] - zi;
      const d = dx * dx + dy * dy + dz * dz;
      if (d >= dist[o + K - 1]) continue;
      let k = K - 1;
      while (k > 0 && dist[o + k - 1] > d) { dist[o + k] = dist[o + k - 1]; idx[o + k] = idx[o + k - 1]; k--; }
      dist[o + k] = d; idx[o + k] = j;
    }
  }
  return { idx, dist };
}

// One round: add the midpoint of every near pair (never across gaps between separate parts).
function densifyRound(set) {
  const { X, C, n } = set, K = 8;
  const { idx, dist } = nearestNeighbours(X, n, K);
  const nearest = new Float64Array(n);
  for (let i = 0; i < n; i++) nearest[i] = dist[i * K];
  const limit = 2.2 * 2.2 * nearest.slice().sort()[n >> 1];
  const seen = new Set(), mids = [];
  for (let i = 0; i < n; i++) {
    for (let k = 0; k < K; k++) {
      const j = idx[i * K + k];
      if (j < 0 || dist[i * K + k] > limit) continue;
      const key = Math.min(i, j) * n + Math.max(i, j);
      if (seen.has(key)) continue;
      seen.add(key);
      mids.push(i, j);
    }
  }
  const m = mids.length / 2, N = n + m;
  const X2 = new Float64Array(3 * N), C2 = new Float64Array(3 * N);
  X2.set(X); C2.set(C);
  for (let q = 0; q < m; q++) {
    const i = mids[2 * q], j = mids[2 * q + 1];
    for (let k = 0; k < 3; k++) {
      X2[3 * (n + q) + k] = (X[3 * i + k] + X[3 * j + k]) / 2;
      C2[3 * (n + q) + k] = (C[3 * i + k] + C[3 * j + k]) / 2;
    }
  }
  return { X: X2, C: C2, n: N };
}

function densify(points) {
  let set = { X: Float64Array.from(points.flatMap((q) => q.p)), C: Float64Array.from(points.flatMap((q) => q.c)), n: points.length };
  while (set.n < POINTS * 1.3) set = densifyRound(set);
  // Farthest-point order over the pool, starting from the model's own first point.
  const { X, C, n } = set, order = [0];
  const gap = new Float64Array(n).fill(Infinity);
  let last = 0;
  while (order.length < Math.min(POINTS, n)) {
    const lx = X[3 * last], ly = X[3 * last + 1], lz = X[3 * last + 2];
    let far = -1, farGap = -1;
    for (let i = 0; i < n; i++) {
      const dx = X[3 * i] - lx, dy = X[3 * i + 1] - ly, dz = X[3 * i + 2] - lz;
      const d = dx * dx + dy * dy + dz * dz;
      if (d < gap[i]) gap[i] = d;
      if (gap[i] > farGap) { farGap = gap[i]; far = i; }
    }
    order.push(far);
    last = far;
  }
  return order.map((i) => ({ p: [X[3 * i], X[3 * i + 1], X[3 * i + 2]], c: [C[3 * i], C[3 * i + 1], C[3 * i + 2]] }));
}

const chunks = [];
const header = Buffer.alloc(8);
header.write("DSSP", 0, "ascii");
header.writeUInt16LE(1, 4);
const missing = MODELS.filter((name) => !(name in assets));
if (missing.length) throw new Error("Draw_in_3D no longer has: " + missing.join(", "));
const names = MODELS;
header.writeUInt16LE(names.length, 6);
chunks.push(header);

for (const name of names) {
  const points = densify(merged(name, assets[name]).map(({ p, c }) => ({ p: [p[0], p[1], -p[2]], c })));
  const min = [0, 1, 2].map((k) => Math.min(...points.map((q) => q.p[k])));
  const max = [0, 1, 2].map((k) => Math.max(...points.map((q) => q.p[k])));
  const nameBytes = Buffer.from(name, "utf8");
  const head = Buffer.alloc(1 + nameBytes.length + 2 + 24);
  let o = 0;
  head.writeUInt8(nameBytes.length, o); o += 1;
  nameBytes.copy(head, o); o += nameBytes.length;
  head.writeUInt16LE(points.length, o); o += 2;
  for (const v of [...min, ...max]) { head.writeFloatLE(v, o); o += 4; }
  const pos = Buffer.alloc(points.length * 6);
  const col = Buffer.alloc(points.length * 3);
  points.forEach(({ p, c }, i) => {
    for (let k = 0; k < 3; k++) {
      const span = max[k] - min[k] || 1;
      const q = Math.round(((p[k] - min[k]) / span) * 65535) - 32768;
      pos.writeInt16LE(Math.max(-32768, Math.min(32767, q)), (i * 3 + k) * 2);
      col.writeUInt8(Math.max(0, Math.min(255, Math.round(c[k] * 255))), i * 3 + k);
    }
  });
  chunks.push(head, pos, col);
  const size = max.map((v, k) => (v - min[k]).toFixed(1)).join(" x ");
  console.log(`${name.padEnd(18)} ${points.length} points, ${size} units`);
}

const out = process.env.OUT || join(here, "..", "Assets", "DroneStar", "Data", "ShapePack.bytes");
mkdirSync(dirname(out), { recursive: true });
const buffer = Buffer.concat(chunks);
writeFileSync(out, buffer);
console.log(`wrote ${out} (${(buffer.length / 1024).toFixed(0)} KB)`);
