// Natural Earth 1:10m (퍼블릭 도메인, naturalearthdata.com) 셰이프파일 → 영토 지도 바탕 타일 (data/Territory/geo/).
// 쓰는 법: node tools/naturalearth_tiles.js <ne_10m_land.shp> <ne_10m_lakes.shp> <ne_10m_rivers_lake_centerlines.shp> [출력 폴더]
// 타일 = 위도 · 경도 10도 칸. 파일 이름 ne_<위도칸>_<경도칸>.json (위도칸 = floor((lat+90)/10), 경도칸 = floor((lon+180)/10)).
// 좌표 = 1e-4도 정수, 고리 · 선마다 [x0, y0, dx1, dy1, ...] (x = 경도, y = 위도, 첫 점은 그대로, 다음부터 앞 점과의 차).
// 육지 · 호수 고리는 타일 경계로 잘라(Sutherland–Hodgman) 칠하기용, 강은 선을 타일 경계에서 끊어 넣는다.
const fs = require("fs"), path = require("path");
const [landShp, lakeShp, riverShp, outArg] = process.argv.slice(2);
if (!landShp || !lakeShp || !riverShp) { console.error("usage: node tools/naturalearth_tiles.js land.shp lakes.shp rivers.shp [out]"); process.exit(1); }
const OUT = outArg || path.join(__dirname, "..", "data", "Territory", "geo");
const TILE = 10, SCALE = 1e4;

function readShp(file) {
  const b = fs.readFileSync(file);
  const shapes = [];
  let o = 100;
  while (o + 8 <= b.length) {
    const len = b.readInt32BE(o + 4) * 2; o += 8;
    const type = b.readInt32LE(o);
    if (type === 5 || type === 3 || type === 15 || type === 13) {
      const nParts = b.readInt32LE(o + 36), nPts = b.readInt32LE(o + 40);
      const parts = []; for (let i = 0; i < nParts; i++) parts.push(b.readInt32LE(o + 44 + i * 4));
      const p0 = o + 44 + nParts * 4;
      for (let i = 0; i < nParts; i++) {
        const s = parts[i], e = i + 1 < nParts ? parts[i + 1] : nPts;
        const pts = [];
        for (let k = s; k < e; k++) pts.push([b.readDoubleLE(p0 + k * 16), b.readDoubleLE(p0 + k * 16 + 8)]);
        shapes.push({ poly: type === 5 || type === 15, pts });
      }
    }
    o += len;
  }
  return shapes;
}

function bbox(pts) { let a = [1e9, 1e9, -1e9, -1e9]; for (const [x, y] of pts) { if (x < a[0]) a[0] = x; if (y < a[1]) a[1] = y; if (x > a[2]) a[2] = x; if (y > a[3]) a[3] = y; } return a; }

// 다각형을 사각형으로 자르기 (변마다 Sutherland–Hodgman)
function clipPoly(pts, x0, y0, x1, y1) {
  const edges = [[p => p[0] >= x0, (a, b) => inter(a, b, 0, x0)], [p => p[0] <= x1, (a, b) => inter(a, b, 0, x1)],
                 [p => p[1] >= y0, (a, b) => inter(a, b, 1, y0)], [p => p[1] <= y1, (a, b) => inter(a, b, 1, y1)]];
  let out = pts;
  for (const [inside, cut] of edges) {
    const src = out; out = [];
    for (let i = 0; i < src.length; i++) {
      const a = src[(i + src.length - 1) % src.length], b = src[i];
      const ia = inside(a), ib = inside(b);
      if (ib) { if (!ia) out.push(cut(a, b)); out.push(b); } else if (ia) out.push(cut(a, b));
    }
    if (out.length === 0) break;
  }
  return out;
}
function inter(a, b, axis, v) { const t = (v - a[axis]) / (b[axis] - a[axis]); return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t]; }

// 선을 사각형 안 조각들로 (선분마다 Liang–Barsky)
function clipLine(pts, x0, y0, x1, y1) {
  const pieces = []; let cur = null;
  for (let i = 1; i < pts.length; i++) {
    const seg = clipSeg(pts[i - 1], pts[i], x0, y0, x1, y1);
    if (!seg) { if (cur) { pieces.push(cur); cur = null; } continue; }
    if (cur && cur[cur.length - 1][0] === seg[0][0] && cur[cur.length - 1][1] === seg[0][1]) cur.push(seg[1]);
    else { if (cur) pieces.push(cur); cur = [seg[0], seg[1]]; }
  }
  if (cur) pieces.push(cur);
  return pieces;
}
function clipSeg(a, b, x0, y0, x1, y1) {
  let t0 = 0, t1 = 1; const dx = b[0] - a[0], dy = b[1] - a[1];
  for (const [p, q] of [[-dx, a[0] - x0], [dx, x1 - a[0]], [-dy, a[1] - y0], [dy, y1 - a[1]]]) {
    if (p === 0) { if (q < 0) return null; continue; }
    const r = q / p;
    if (p < 0) { if (r > t1) return null; if (r > t0) t0 = r; } else { if (r < t0) return null; if (r < t1) t1 = r; }
  }
  return [[a[0] + dx * t0, a[1] + dy * t0], [a[0] + dx * t1, a[1] + dy * t1]];
}

function encode(pts) {
  const out = []; let px = 0, py = 0, n = 0;
  for (const [x, y] of pts) {
    const ix = Math.round(x * SCALE), iy = Math.round(y * SCALE);
    if (n > 0 && ix === px && iy === py) continue; // 같은 점(1e-4도 안) 빼기
    out.push(n === 0 ? ix : ix - px, n === 0 ? iy : iy - py); px = ix; py = iy; n++;
  }
  return out;
}

const tiles = new Map();
function tile(ti, tj) { const k = ti + "_" + tj; if (!tiles.has(k)) tiles.set(k, { land: [], lake: [], river: [] }); return tiles.get(k); }

function add(kind, shapes) {
  for (const s of shapes) {
    const [bx0, by0, bx1, by1] = bbox(s.pts);
    for (let ti = Math.max(0, Math.floor((by0 + 90) / TILE)); ti <= Math.min(17, Math.floor((by1 + 90) / TILE)); ti++)
      for (let tj = Math.max(0, Math.floor((bx0 + 180) / TILE)); tj <= Math.min(35, Math.floor((bx1 + 180) / TILE)); tj++) {
        const x0 = tj * TILE - 180, y0 = ti * TILE - 90, x1 = x0 + TILE, y1 = y0 + TILE;
        if (s.poly) {
          const c = clipPoly(s.pts, x0, y0, x1, y1);
          const e = encode(c);
          if (e.length >= 6) tile(ti, tj)[kind].push(e);
        } else
          for (const piece of clipLine(s.pts, x0, y0, x1, y1)) { const e = encode(piece); if (e.length >= 4) tile(ti, tj)[kind].push(e); }
      }
  }
}

add("land", readShp(landShp));
add("lake", readShp(lakeShp));
add("river", readShp(riverShp));
fs.mkdirSync(OUT, { recursive: true });
for (const f of fs.readdirSync(OUT)) if (/^ne_\d+_\d+\.json$/.test(f)) fs.unlinkSync(path.join(OUT, f));
let total = 0;
for (const [k, t] of tiles) {
  const s = JSON.stringify(t);
  fs.writeFileSync(path.join(OUT, "ne_" + k + ".json"), s + "\n");
  total += s.length;
}
fs.writeFileSync(path.join(OUT, "README.md"),
  "# 영토 지도 바탕 (Natural Earth 1:10m)\n\n" +
  "출처: Natural Earth (naturalearthdata.com), 퍼블릭 도메인 — 육지 `ne_10m_land`, 호수 `ne_10m_lakes`, 강 `ne_10m_rivers_lake_centerlines` (v" +
  (fs.existsSync(landShp.replace(/\.shp$/, ".VERSION.txt")) ? fs.readFileSync(landShp.replace(/\.shp$/, ".VERSION.txt"), "utf8").trim() : "?") + ").\n\n" +
  "만드는 법: `node tools/naturalearth_tiles.js <land.shp> <lakes.shp> <rivers.shp>` (셰이프파일은 저장소에 넣지 않는다).\n\n" +
  "형식: 10도 칸마다 `ne_<위도칸>_<경도칸>.json` = `{\"land\":[고리…],\"lake\":[고리…],\"river\":[선…]}`, 좌표는 1e-4도 정수, 첫 점 그대로 · 다음부터 차 (x = 경도, y = 위도).\n");
console.log("tiles", tiles.size, "bytes", total);
