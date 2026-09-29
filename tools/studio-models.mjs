// Procedural models drawn for the studio's own shows (not from Draw_in_3D): the Lạc bird of the Đông Sơn
// bronze drums, with wings that can flap, and the drum's face. Each model is a dense list of coloured points,
// { p: [x, y, z], c: [r, g, b] (0..1, sRGB), g: group }, in model units with the audience looking along +z.
// Groups: 0 = rigid body, 1 = left wing, 2 = right wing; models with wings also give the right wing's hinge
// (the left one is its mirror image). The exporter orders the points by farthest-point sampling.

const TAU = Math.PI * 2;
// Stacked sheets (depth, brightness): from the front they read as one bright drawing, and they let a fleet of
// thousands fill the bird at a sensible size; from the side the bird is a layered relief.
const SHEETS = [[0, 1], [0.045, 0.8], [0.09, 0.65], [0.135, 0.5]];
const mix = (a, b, t) => a.map((v, k) => v + (b[k] - v) * t);
const GOLD = [1.0, 0.66, 0.2];
const WARM = [1.0, 0.84, 0.46];
const WHITE = [1.0, 0.96, 0.84];
const EMBER = [1.0, 0.42, 0.1];

// Deterministic jitter so exports are identical run to run.
function rng(seed) {
  let s = seed >>> 0;
  return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296);
}

// Quadratic Bézier point.
const bez = (a, c, b, t) => [0, 1].map((k) => (1 - t) * (1 - t) * a[k] + 2 * (1 - t) * t * c[k] + t * t * b[k]);

// A tapered stroke along a Bézier, filled with points spaced ~step apart: w0 at the root, w1 at the tip.
function stroke(out, a, c, b, w0, w1, step, colorAt, group, z = 0, bow = 0) {
  const samples = [];
  let length = 0, prev = bez(a, c, b, 0);
  for (let i = 1; i <= 200; i++) {
    const q = bez(a, c, b, i / 200);
    length += Math.hypot(q[0] - prev[0], q[1] - prev[1]);
    prev = q;
  }
  const n = Math.max(2, Math.ceil(length / step));
  for (let i = 0; i <= n; i++) {
    const t = i / n, q = bez(a, c, b, t), q2 = bez(a, c, b, Math.min(1, t + 1e-3)), q1 = bez(a, c, b, Math.max(0, t - 1e-3));
    let tx = q2[0] - q1[0], ty = q2[1] - q1[1];
    const len = Math.hypot(tx, ty) || 1;
    tx /= len; ty /= len;
    const w = w0 + (w1 - w0) * t, rows = Math.max(1, Math.round(w / step));
    for (let r = 0; r < rows; r++) {
      const u = rows === 1 ? 0 : (r / (rows - 1) - 0.5) * w;
      const edge = rows === 1 ? 1 : Math.abs(r / (rows - 1) - 0.5) * 2;
      samples.push({ p: [q[0] - ty * u, q[1] + tx * u, z + bow * Math.sin(Math.PI * t)], c: colorAt(t, edge), g: group });
    }
  }
  out.push(...samples);
}

// Filled ellipse.
function blob(out, cx, cy, rx, ry, step, colorAt, group, z = 0) {
  for (let y = -ry; y <= ry; y += step * 0.866) {
    const row = Math.round((y + ry) / (step * 0.866));
    for (let x = -rx + (row % 2) * step * 0.5; x <= rx; x += step) {
      const d = (x * x) / (rx * rx) + (y * y) / (ry * ry);
      if (d <= 1) out.push({ p: [cx + x, cy + y, z], c: colorAt(Math.sqrt(d)), g: group });
    }
  }
}

/**
 * The Lạc bird: neck raised, beak to the sky, wings spread in a fan of long feathers whose tips curl upward,
 * tail plumes below. Four stacked sheets give the feathers depth and room for a big fleet.
 */
export function lacBird() {
  const out = [], step = 0.0045, random = rng(7);
  // The wing hinge (the shoulder), right side; the left wing mirrors it. A clear gap separates the wing roots from
  // the body, so a flapping wing never brushes the drones of the body.
  const hinge = [0.13, 0.02];
  for (const side of [1, -1]) {
    const group = side > 0 ? 2 : 1, X = (v) => [v[0] * side, v[1]];
    for (const [z, dim] of SHEETS) {
      const shade = (col) => col.map((v) => v * dim);
      // A crescent of long feathers flowing from the shoulder up to high tips; the lower ones end shorter,
      // leaving the scalloped trailing edge of the reference drawings.
      const feathers = 11;
      for (let k = 0; k < feathers; k++) {
        const f = k / (feathers - 1);
        const root = [0.15 + 0.02 * f, 0.1 - 0.17 * f];
        const tip = [0.99 - 0.36 * Math.pow(f, 1.15), 0.56 - 0.7 * f];
        const lift = 0.16 - 0.07 * f;
        const control = [(root[0] + tip[0]) * 0.5 - 0.06, (root[1] + tip[1]) * 0.5 + lift];
        const color = (t, e) => {
          const base = mix(mix(WARM, GOLD, f * 0.8), WHITE, (1 - f) * 0.35);
          return shade(mix(mix(base, WARM, e * 0.35), WHITE, Math.pow(t, 4) * 0.8));
        };
        stroke(out, X(root), X(control), X(tip), 0.028 - 0.006 * f, 0.009, step, color, group, z);
      }
      // Coverts: short feathers over the inner wing, so it reads as one wing and not a comb.
      for (let k = 0; k < 6; k++) {
        const f = k / 5;
        const root = [0.15, 0.09 - 0.13 * f];
        const tip = [0.42 + 0.06 * f, 0.26 - 0.2 * f];
        stroke(out, X(root), X([(root[0] + tip[0]) / 2, (root[1] + tip[1]) / 2 + 0.05]), X(tip), 0.022, 0.012, step,
          (t, e) => shade(mix(WHITE, WARM, 0.3 + 0.3 * t)), group, z);
      }
    }
  }
  // Body, neck raised to the sky, head, beak and tail plumes: the rigid part (group 0).
  for (const [z, dim] of SHEETS) {
    const shade = (col) => col.map((v) => v * dim);
    blob(out, 0, -0.01, 0.052, 0.125, step, (d) => shade(mix(WHITE, WARM, d)), 0, z);
    stroke(out, [0, 0.09], [0.028, 0.27], [0, 0.43], 0.032, 0.021, step, (t) => shade(mix(WARM, WHITE, 0.4 + 0.5 * t)), 0, z);
    blob(out, 0, 0.452, 0.034, 0.028, step, () => shade(WHITE), 0, z);
    stroke(out, [0, 0.475], [0.002, 0.54], [0.004, 0.62], 0.013, 0.004, step, () => shade(mix(EMBER, WARM, 0.35)), 0, z);
    stroke(out, [-0.025, 0.465], [-0.07, 0.49], [-0.11, 0.47], 0.011, 0.004, step, (t) => shade(mix(WARM, EMBER, t * 0.6)), 0, z);
    for (let k = 0; k < 5; k++) {
      const f = k / 4 - 0.5, a = [0, -0.12];
      const tip = [f * 0.3, -0.52 + Math.abs(f) * 0.1];
      stroke(out, a, [f * 0.06, -0.33], tip, 0.028, 0.009, step, (t, e) => shade(mix(mix(GOLD, WARM, e), EMBER, t * 0.4)), 0, z);
    }
  }
  for (const q of out) {
    const s = 0.9 + 0.2 * random();
    q.c = q.c.map((v) => Math.min(1, v * s));
  }
  return { name: "Lac bird", points: out, hinge: [hinge[0], hinge[1], 0] };
}

/**
 * The face of a Đông Sơn bronze drum: a fourteen-ray sun at the centre, concentric bands of circles-with-dots and
 * ladders, a frieze of flying Lạc birds, and the rim.
 */
export function drumFace() {
  const out = [], step = 0.004;
  const ring = (r, w, col) => {
    const n = Math.ceil((TAU * r) / step);
    const rows = Math.max(1, Math.round(w / step));
    for (let j = 0; j < rows; j++) {
      const rr = r + (rows === 1 ? 0 : (j / (rows - 1) - 0.5) * w);
      for (let i = 0; i < n; i++) {
        const a = (i / n) * TAU;
        out.push({ p: [Math.cos(a) * rr, Math.sin(a) * rr, 0], c: col, g: 0 });
      }
    }
  };
  // The sun: a solid core and fourteen rays with feathered gaps between them.
  blob(out, 0, 0, 0.07, 0.07, step, () => WHITE, 0);
  const rays = 14;
  for (let k = 0; k < rays; k++) {
    const a = (k / rays) * TAU, half = (TAU / rays) * 0.22;
    for (let r = 0.07; r <= 0.3; r += step * 0.9) {
      const w = half * (1 - (r - 0.07) / 0.23);
      const n = Math.max(1, Math.round((w * 2 * r) / step));
      for (let i = 0; i <= n; i++) {
        const b = a + (n === 0 ? 0 : (i / n - 0.5) * 2 * w);
        out.push({ p: [Math.cos(b) * r, Math.sin(b) * r, 0], c: mix(WHITE, GOLD, (r - 0.07) / 0.23), g: 0 });
      }
    }
  }
  ring(0.34, 0.012, WARM);
  // Band of circles-with-dots.
  const circles = 30;
  for (let k = 0; k < circles; k++) {
    const a = (k / circles) * TAU, cx = Math.cos(a) * 0.4, cy = Math.sin(a) * 0.4;
    ring0(out, cx, cy, 0.024, step, GOLD);
    blob(out, cx, cy, 0.006, 0.006, step, () => WARM, 0);
  }
  ring(0.46, 0.012, WARM);
  // Ladder band.
  const ladders = 90;
  for (let k = 0; k < ladders; k++) {
    const a = (k / ladders) * TAU;
    for (let r = 0.48; r <= 0.53; r += step) out.push({ p: [Math.cos(a) * r, Math.sin(a) * r, 0], c: GOLD, g: 0 });
  }
  ring(0.55, 0.012, WARM);
  // Frieze of flying birds, beaks forward, circling.
  const birds = 8;
  for (let k = 0; k < birds; k++) {
    const a = (k / birds) * TAU;
    birdGlyph(out, a, 0.705, 0.2, step);
  }
  ring(0.86, 0.014, WARM);
  // Rim: tick marks and the outer edge.
  const ticks = 120;
  for (let k = 0; k < ticks; k++) {
    const a = (k / ticks) * TAU;
    for (let r = 0.89; r <= 0.95; r += step) out.push({ p: [Math.cos(a) * r, Math.sin(a) * r, 0], c: mix(GOLD, EMBER, 0.2), g: 0 });
  }
  ring(0.985, 0.02, WARM);
  // A second, dimmer sheet behind the face doubles its capacity for big fleets.
  const back = out.map((q) => ({ p: [q.p[0], q.p[1], 0.06], c: q.c.map((v) => v * 0.7), g: 0 }));
  out.push(...back);
  return { name: "Bronze drum", points: out };
}

function ring0(out, cx, cy, r, step, col) {
  const n = Math.max(8, Math.ceil((TAU * r) / step));
  for (let i = 0; i < n; i++) {
    const a = (i / n) * TAU;
    out.push({ p: [cx + Math.cos(a) * r, cy + Math.sin(a) * r, 0], c: col, g: 0 });
  }
}

// A flying Lạc bird centred at angle a on radius r, flying counter-clockwise (tangent), about len long.
function birdGlyph(out, a, r, len, step) {
  const cx = Math.cos(a) * r, cy = Math.sin(a) * r;
  const tx = -Math.sin(a), ty = Math.cos(a), nx = Math.cos(a), ny = Math.sin(a);
  const at = (u, v) => [cx + tx * u * len + nx * v * len, cy + ty * u * len + ny * v * len];
  const part = (u0, v0, uc, vc, u1, v1, w0, w1, col) =>
    stroke(out, at(u0, v0), at(uc, vc), at(u1, v1), w0 * len, w1 * len, step, (t) => mix(col, WHITE, t * 0.3), 0);
  part(-0.45, 0, 0, 0.03, 0.3, 0, 0.14, 0.1, WARM); // body
  part(0.3, 0, 0.45, 0.02, 0.66, 0.01, 0.05, 0.015, WHITE); // long beak
  part(-0.05, 0.02, 0.0, 0.3, 0.12, 0.5, 0.1, 0.03, GOLD); // wing raised
  part(-0.15, 0.02, -0.2, 0.28, -0.12, 0.46, 0.09, 0.03, GOLD);
  part(-0.45, 0, -0.6, 0.06, -0.7, 0.2, 0.07, 0.02, GOLD); // tail plumes
  part(-0.45, 0, -0.62, -0.04, -0.74, -0.12, 0.07, 0.02, GOLD);
  part(0.24, 0.03, 0.26, 0.14, 0.2, 0.24, 0.05, 0.015, WARM); // crest
}

export const STUDIO_MODELS = [lacBird, drumFace];
