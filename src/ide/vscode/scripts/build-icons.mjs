// Generates resources/icon.png (256x256 Marketplace icon) from resources/brand/neolinter-mark.svg and the
// light/dark UI icons from resources/brand/neolinter-glyph.svg (currentColor). Run: npm run build-icons.
// No dependency: the mark only uses rounded rects, round-capped strokes and one quadratic path, which are
// rasterized here (4x4 supersampling). If the mark changes shape, update `mark` below to match the SVG.
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { deflateSync } from 'node:zlib';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', 'resources');
const SIZE = 256;
const AA = 4;

const hex = (c) => [1, 3, 5].map((i) => Number.parseInt(c.slice(i, i + 2), 16));

function quad(p0, c, p1, steps = 16) {
  return Array.from({ length: steps + 1 }, (_, i) => {
    const t = i / steps, u = 1 - t;
    return [u * u * p0[0] + 2 * u * t * c[0] + t * t * p1[0], u * u * p0[1] + 2 * u * t * c[1] + t * t * p1[1]];
  });
}

// Wave: M62 196 q6-10 12 0 t12 0 t12 0 t12 0 H178 (each t reflects the previous control point).
const wave = [[62, 196]];
let control = [68, 186];
for (let x = 62, i = 0; i < 4; i++, x += 12) {
  wave.push(...quad([x, 196], control, [x + 12, 196]).slice(1));
  control = [2 * (x + 12) - control[0], 196 + (196 - control[1])];
}
wave.push([178, 196]);

// Painter's order, same as the SVG.
const mark = [
  { rect: [0, 0, 240, 240, 56], color: '#1C1917' },
  { line: [[76, 156], [76, 60]], width: 22, color: '#FAFAF9' },
  { line: [[164, 156], [164, 60]], width: 22, color: '#FAFAF9' },
  { line: [[76, 60], [164, 156]], width: 22, color: '#FB923C' },
  { line: wave, width: 8, color: '#FB923C' },
];

function segmentDistance([px, py], [ax, ay], [bx, by]) {
  const dx = bx - ax, dy = by - ay;
  const t = Math.max(0, Math.min(1, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)));
  return Math.hypot(px - ax - t * dx, py - ay - t * dy);
}

function covers(shape, p) {
  if (shape.rect) {
    const [x, y, w, h, r] = shape.rect;
    const qx = Math.abs(p[0] - (x + w / 2)) - (w / 2 - r), qy = Math.abs(p[1] - (y + h / 2)) - (h / 2 - r);
    return Math.hypot(Math.max(qx, 0), Math.max(qy, 0)) + Math.min(Math.max(qx, qy), 0) <= r;
  }
  return shape.line.some((a, i) => i > 0 && segmentDistance(p, shape.line[i - 1], a) <= shape.width / 2);
}

function render() {
  const scale = 240 / SIZE;
  const raw = Buffer.alloc((SIZE * 4 + 1) * SIZE);
  for (let y = 0; y < SIZE; y++) {
    raw[y * (SIZE * 4 + 1)] = 0;
    for (let x = 0; x < SIZE; x++) {
      let r = 0, g = 0, b = 0, a = 0;
      for (let sy = 0; sy < AA; sy++) {
        for (let sx = 0; sx < AA; sx++) {
          const p = [(x + (sx + 0.5) / AA) * scale, (y + (sy + 0.5) / AA) * scale];
          const top = mark.findLast((shape) => covers(shape, p));
          if (top) {
            const [cr, cg, cb] = hex(top.color);
            r += cr; g += cg; b += cb; a++;
          }
        }
      }
      const o = y * (SIZE * 4 + 1) + 1 + x * 4;
      if (a) raw.set([Math.round(r / a), Math.round(g / a), Math.round(b / a), Math.round((a * 255) / (AA * AA))], o);
    }
  }
  return raw;
}

const crcTable = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});
const crc32 = (buf) => (buf.reduce((c, byte) => crcTable[(c ^ byte) & 255] ^ (c >>> 8), 0xffffffff) ^ 0xffffffff) >>> 0;

function chunk(type, data) {
  const out = Buffer.alloc(12 + data.length);
  out.writeUInt32BE(data.length, 0);
  out.write(type, 4, 'latin1');
  data.copy(out, 8);
  out.writeUInt32BE(crc32(out.subarray(4, 8 + data.length)), 8 + data.length);
  return out;
}

function png(raw) {
  const header = Buffer.alloc(13);
  header.writeUInt32BE(SIZE, 0);
  header.writeUInt32BE(SIZE, 4);
  header.set([8, 6, 0, 0, 0], 8); // 8-bit RGBA
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

mkdirSync(join(root, 'icons'), { recursive: true });
writeFileSync(join(root, 'icon.png'), png(render()));

// VS Code loads command and panel icons as images, where currentColor does not follow the theme: ship one file
// per theme, in the colors of the built-in codicons.
const glyph = readFileSync(join(root, 'brand', 'neolinter-glyph.svg'), 'utf8');
writeFileSync(join(root, 'icons', 'neolinter-light.svg'), glyph.replaceAll('currentColor', '#424242'));
writeFileSync(join(root, 'icons', 'neolinter-dark.svg'), glyph.replaceAll('currentColor', '#C5C5C5'));
