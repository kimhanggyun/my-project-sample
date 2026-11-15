// 재화 아이콘 (unity/Assets/Game/Resources/Ui/icons/, 128×128): 엽전(gold.png) · 금괴(gem.png). 2026-11-15 재화 이름 곡식 · 엽전 · 금괴 (economy.md).
// 쓰는 법: node tools/resource_icons.js [출력 폴더]. 곡식(supply.png)은 그대로.
const fs = require("fs"), path = require("path"), zlib = require("zlib");
const OUT = process.argv[2] || path.join(__dirname, "..", "unity", "Assets", "Game", "Resources", "Ui", "icons");
const N = 128, SS = 4, W = N * SS;

function canvas() { return new Float32Array(W * W * 4); }
function plot(px, i, c, a) {
  const o = i * 4, ia = 1 - a;
  px[o] = c[0] * a + px[o] * ia; px[o + 1] = c[1] * a + px[o + 1] * ia; px[o + 2] = c[2] * a + px[o + 2] * ia; px[o + 3] = a + px[o + 3] * ia;
}
function shape(px, test, bbox, c) {
  const [x0, y0, x1, y1] = bbox.map(v => v * SS);
  for (let y = Math.max(0, Math.floor(y0)); y < Math.min(W, Math.ceil(y1)); y++)
    for (let x = Math.max(0, Math.floor(x0)); x < Math.min(W, Math.ceil(x1)); x++)
      if (test((x + 0.5) / SS, (y + 0.5) / SS)) plot(px, y * W + x, c, 1);
}
function ellipse(px, cx, cy, rx, ry, c) { shape(px, (x, y) => ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1, [cx - rx, cy - ry, cx + rx, cy + ry], c); }
function rect(px, x0, y0, x1, y1, c) { shape(px, () => true, [x0, y0, x1, y1], c); }
function poly(px, pts, c) {
  let x0 = 1e9, y0 = 1e9, x1 = -1e9, y1 = -1e9;
  for (const [x, y] of pts) { x0 = Math.min(x0, x); y0 = Math.min(y0, y); x1 = Math.max(x1, x); y1 = Math.max(y1, y); }
  shape(px, (x, y) => { let ins = false; for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) { const [xi, yi] = pts[i], [xj, yj] = pts[j]; if ((yi > y) !== (yj > y) && x < xi + (y - yi) * (xj - xi) / (yj - yi)) ins = !ins; } return ins; }, [x0, y0, x1, y1], c);
}
// 그린 뒤 투명으로 구멍 뚫기
function clearRect(px, x0, y0, x1, y1) {
  for (let y = y0 * SS; y < y1 * SS; y++) for (let x = x0 * SS; x < x1 * SS; x++) { const o = (y * W + x) * 4; px[o] = px[o + 1] = px[o + 2] = px[o + 3] = 0; }
}

const OUTLINE = [74, 52, 24];

// 엽전: 비스듬히 겹친 동전 둘 (뒤 동전은 조금 어둡게), 가운데 네모 구멍
function coin(px, cx, cy, r, face, rim, dark) {
  ellipse(px, cx, cy + 5, r + 3, r * 0.92 + 3, OUTLINE);      // 두께 그림자 + 외곽
  ellipse(px, cx, cy + 5, r, r * 0.92, dark);                   // 옆면
  ellipse(px, cx, cy, r + 3, r * 0.92 + 3, OUTLINE);
  ellipse(px, cx, cy, r, r * 0.92, rim);
  ellipse(px, cx, cy, r * 0.8, r * 0.74, face);
  const h = r * 0.26;
  rect(px, cx - h - 4, cy - h - 4, cx + h + 4, cy + h + 4, rim);  // 구멍 테두리
  rect(px, cx - h - 2, cy - h - 2, cx + h + 2, cy + h + 2, OUTLINE);
}
function gold() {
  const px = canvas();
  coin(px, 50, 60, 36, [226, 178, 64], [196, 142, 40], [150, 104, 30]);
  clearRect(px, 50 - 9, 60 - 9, 50 + 10, 60 + 10);
  coin(px, 78, 72, 40, [240, 196, 80], [206, 152, 44], [160, 112, 32]);
  clearRect(px, 78 - 10, 72 - 10, 78 + 11, 72 + 11);
  // 글자 자리 점 (엽전 앞면 무늬 느낌): 구멍 위아래 · 좌우 짧은 획
  for (const [dx, dy, w, h] of [[-3, -30, 6, 10], [-3, 20, 6, 10], [-30, -3, 10, 6], [20, -3, 10, 6]]) rect(px, 78 + dx, 72 + dy, 78 + dx + w, 72 + dy + h, [176, 124, 36]);
  return px;
}

// 금괴: 사다리꼴 금덩이 (윗면 밝게, 앞면 · 옆면), 위에 작은 금괴 하나 더
function bar(px, x, y, w, h, d) {
  // 앞면 사다리꼴: 아래 폭 w, 위 폭 w - 2d, 높이 h. 윗면은 위로 d만큼 비스듬히
  const front = [[x, y + h], [x + w, y + h], [x + w - d, y], [x + d, y]];
  const top = [[x + d, y], [x + w - d, y], [x + w - d * 1.6, y - d * 0.7], [x + d * 1.6, y - d * 0.7]];
  const grow = (pts, k) => { const cx = pts.reduce((s, p) => s + p[0], 0) / pts.length, cy = pts.reduce((s, p) => s + p[1], 0) / pts.length; return pts.map(([px_, py]) => [cx + (px_ - cx) * k, cy + (py - cy) * k]); };
  poly(px, grow([[x, y + h], [x + w, y + h], [x + w - d * 1.6, y - d * 0.7], [x + d * 1.6, y - d * 0.7]], 1.06), OUTLINE);
  poly(px, front, [222, 164, 44]);
  poly(px, top, [252, 214, 104]);
  poly(px, [[x + d + 6, y + 6], [x + d + 14, y + 6], [x + d + 6, y + h - 8]], [246, 200, 92]); // 앞면 빛
}
function gem() {
  const px = canvas();
  bar(px, 14, 74, 100, 36, 14);
  bar(px, 36, 34, 60, 30, 10);
  return px;
}

function png(px) {
  const raw = Buffer.alloc((N * 4 + 1) * N);
  for (let y = 0; y < N; y++) {
    for (let x = 0; x < N; x++) {
      let r = 0, g = 0, b = 0, a = 0;
      for (let sy = 0; sy < SS; sy++) for (let sx = 0; sx < SS; sx++) { const o = ((y * SS + sy) * W + x * SS + sx) * 4; r += px[o]; g += px[o + 1]; b += px[o + 2]; a += px[o + 3]; }
      const o = y * (N * 4 + 1) + 1 + x * 4;
      raw[o] = a > 0 ? Math.round(r / a) : 0; raw[o + 1] = a > 0 ? Math.round(g / a) : 0; raw[o + 2] = a > 0 ? Math.round(b / a) : 0; raw[o + 3] = Math.round(a / (SS * SS) * 255);
    }
  }
  const t = []; for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; }
  const crc = buf => { let c = 0xffffffff; for (const v of buf) c = t[(c ^ v) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const chunk = (type, data) => { const ty = Buffer.from(type), len = Buffer.alloc(4), c = Buffer.alloc(4); len.writeUInt32BE(data.length); c.writeUInt32BE(crc(Buffer.concat([ty, data]))); return Buffer.concat([len, ty, data, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(N, 0); ihdr.writeUInt32BE(N, 4); ihdr[8] = 8; ihdr[9] = 6;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ihdr), chunk("IDAT", zlib.deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]);
}

fs.writeFileSync(path.join(OUT, "gold.png"), png(gold()));
fs.writeFileSync(path.join(OUT, "gem.png"), png(gem()));
console.log("wrote gold.png (엽전), gem.png (금괴)");
