// Builds Assets/DroneStar/Data/ShapePack.bytes from the Draw_in_3D formation library
// (web/src/formation-assets.js: Blender models sampled as 4096 coloured points in farthest-point order,
// so every prefix is evenly spread).
//
// A 4096-drone show cannot light every drone from exactly 4096 points (the spacing filter always drops a
// few), so each model is densified: midpoints between near neighbours (never across gaps between separate
// parts) are added, and the union is re-ordered by farthest-point sampling and cut to POINTS (three times the
// largest fleet: greedy spacing packs well only while it uses a small part of the pool), keeping the
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

const POINTS = 12288;

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

function densify(points) {
  const n = points.length, K = 8;
  const P = points.map((q) => q.p);
  const d2 = (a, b) => (a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2;
  const neighbours = [];
  const nearest = new Float64Array(n);
  for (let i = 0; i < n; i++) {
    const best = [];
    for (let j = 0; j < n; j++) {
      if (j === i) continue;
      const d = d2(P[i], P[j]);
      if (best.length < K || d < best[best.length - 1][0]) {
        best.push([d, j]);
        best.sort((x, y) => x[0] - y[0]);
        if (best.length > K) best.pop();
      }
    }
    neighbours.push(best);
    nearest[i] = Math.sqrt(best[0][0]);
  }
  const typical = Float64Array.from(nearest).sort()[n >> 1];
  const limit = (2.2 * typical) ** 2;
  const union = points.slice();
  const seen = new Set();
  for (let i = 0; i < n; i++) {
    for (const [d, j] of neighbours[i]) {
      const key = Math.min(i, j) * n + Math.max(i, j);
      if (d > limit || seen.has(key)) continue;
      seen.add(key);
      const a = points[i], b = points[j];
      union.push({ p: [0, 1, 2].map((k) => (a.p[k] + b.p[k]) / 2), c: [0, 1, 2].map((k) => (a.c[k] + b.c[k]) / 2) });
    }
  }
  // Farthest-point order over the union, starting from the model's own first point.
  const N = union.length, order = [0];
  const gap = new Float64Array(N).fill(Infinity);
  let last = 0;
  while (order.length < Math.min(POINTS, N)) {
    let far = -1, farGap = -1;
    for (let i = 0; i < N; i++) {
      const d = d2(union[i].p, union[last].p);
      if (d < gap[i]) gap[i] = d;
      if (gap[i] > farGap) { farGap = gap[i]; far = i; }
    }
    order.push(far);
    last = far;
  }
  return order.map((i) => union[i]);
}

const chunks = [];
const header = Buffer.alloc(8);
header.write("DSSP", 0, "ascii");
header.writeUInt16LE(1, 4);
const names = Object.keys(assets);
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

const out = join(here, "..", "Assets", "DroneStar", "Data", "ShapePack.bytes");
mkdirSync(dirname(out), { recursive: true });
const buffer = Buffer.concat(chunks);
writeFileSync(out, buffer);
console.log(`wrote ${out} (${(buffer.length / 1024).toFixed(0)} KB)`);
