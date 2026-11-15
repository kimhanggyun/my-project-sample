// 영토 지도 바탕에 세부 층 더하기 (Natural Earth 1:10m, 퍼블릭 도메인): 시도 경계선 · 도로 · 도시 지역 · 도시 이름.
// 쓰는 법: node tools/naturalearth_detail.js <폴더: 내려받아 푼 셰이프파일들> [위도칸0 위도칸1 경도칸0 경도칸1] [출력 폴더]
//   기본 범위 = 한국 근처 (위도 30~50도 · 경도 120~140도 = 칸 12~13 · 30~31). 기존 타일(ne_<위도칸>_<경도칸>.json)에 층을 더한다 (없으면 만든다).
// 필요한 셰이프파일: ne_10m_admin_1_states_provinces_lines, ne_10m_roads, ne_10m_urban_areas, ne_10m_populated_places_simple (각 폴더 안 같은 이름 .shp/.dbf).
// 층 (좌표는 기존과 같이 1e-4도 정수, 첫 점 그대로 · 다음부터 차):
//   admin1 = 시도 경계 선들, road = 큰 도로(고속도로 · 주요 도로) 선들, road2 = 그 밖 도로, urban = 도시 지역 고리,
//   place = [[경도, 위도, 등급(0 큰 도시 ~ 10 작은), "이름"], ...] (점은 차 없이 그대로)
const fs = require("fs"), path = require("path");
const [srcDir, a0 = "12", a1 = "13", b0 = "30", b1 = "31", outArg] = process.argv.slice(2);
if (!srcDir) { console.error("usage: node tools/naturalearth_detail.js <shapefile folder> [lat0 lat1 lon0 lon1] [out]"); process.exit(1); }
const OUT = outArg || path.join(__dirname, "..", "data", "Territory", "geo");
const TILE = 10, SCALE = 1e4;
const range = { lat0: +a0, lat1: +a1, lon0: +b0, lon1: +b1 };

function readShp(file) {
  const b = fs.readFileSync(file);
  const shapes = [];
  let o = 100, rec = 0;
  while (o + 8 <= b.length) {
    const len = b.readInt32BE(o + 4) * 2; o += 8;
    const type = b.readInt32LE(o);
    if (type === 1) shapes.push({ rec, point: [b.readDoubleLE(o + 4), b.readDoubleLE(o + 12)] });
    else if (type === 5 || type === 3 || type === 15 || type === 13) {
      const nParts = b.readInt32LE(o + 36), nPts = b.readInt32LE(o + 40);
      const parts = []; for (let i = 0; i < nParts; i++) parts.push(b.readInt32LE(o + 44 + i * 4));
      const p0 = o + 44 + nParts * 4;
      for (let i = 0; i < nParts; i++) {
        const s = parts[i], e = i + 1 < nParts ? parts[i + 1] : nPts;
        const pts = [];
        for (let k = s; k < e; k++) pts.push([b.readDoubleLE(p0 + k * 16), b.readDoubleLE(p0 + k * 16 + 8)]);
        shapes.push({ rec, poly: type === 5 || type === 15, pts });
      }
    }
    o += len; rec++;
  }
  return shapes;
}

function readDbf(file) {
  const b = fs.readFileSync(file);
  const n = b.readUInt32LE(4), hl = b.readUInt16LE(8), rl = b.readUInt16LE(10);
  const fields = [];
  for (let o = 32, off = 1; o < hl - 1 && b[o] !== 0x0d; o += 32) {
    const name = b.toString("latin1", o, o + 11).replace(/\0.*$/, ""), len = b[o + 16];
    fields.push({ name, off, len }); off += len;
  }
  const rows = [];
  for (let r = 0; r < n; r++) {
    const base = hl + r * rl, row = {};
    for (const f of fields) row[f.name] = b.toString("utf8", base + f.off, base + f.off + f.len).trim();
    rows.push(row);
  }
  return rows;
}

function inter(a, b, axis, v) { const t = (v - a[axis]) / (b[axis] - a[axis]); return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t]; }
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
function clipSeg(a, b, x0, y0, x1, y1) {
  let t0 = 0, t1 = 1; const dx = b[0] - a[0], dy = b[1] - a[1];
  for (const [p, q] of [[-dx, a[0] - x0], [dx, x1 - a[0]], [-dy, a[1] - y0], [dy, y1 - a[1]]]) {
    if (p === 0) { if (q < 0) return null; continue; }
    const r = q / p;
    if (p < 0) { if (r > t1) return null; if (r > t0) t0 = r; } else { if (r < t0) return null; if (r < t1) t1 = r; }
  }
  return [[a[0] + dx * t0, a[1] + dy * t0], [a[0] + dx * t1, a[1] + dy * t1]];
}
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
function encode(pts) {
  const out = []; let px = 0, py = 0, n = 0;
  for (const [x, y] of pts) {
    const ix = Math.round(x * SCALE), iy = Math.round(y * SCALE);
    if (n > 0 && ix === px && iy === py) continue;
    out.push(n === 0 ? ix : ix - px, n === 0 ? iy : iy - py); px = ix; py = iy; n++;
  }
  return out;
}

const tiles = new Map();
function tile(ti, tj) {
  const k = ti + "_" + tj;
  if (!tiles.has(k)) {
    const f = path.join(OUT, "ne_" + k + ".json");
    const t = fs.existsSync(f) ? JSON.parse(fs.readFileSync(f, "utf8")) : { land: [], lake: [], river: [] };
    for (const layer of ["admin1", "road", "road2", "urban", "place"]) t[layer] = [];
    tiles.set(k, t);
  }
  return tiles.get(k);
}
function eachTile(fn) {
  for (let ti = range.lat0; ti <= range.lat1; ti++)
    for (let tj = range.lon0; tj <= range.lon1; tj++) fn(ti, tj, tj * TILE - 180, ti * TILE - 90, tj * TILE - 170, ti * TILE - 80);
}
function addLines(layer, shapes, pick) {
  eachTile((ti, tj, x0, y0, x1, y1) => {
    for (const s of shapes) {
      if (s.poly || !s.pts) continue;
      const lay = pick ? pick(s) : layer;
      if (!lay) continue;
      for (const piece of clipLine(s.pts, x0, y0, x1, y1)) { const e = encode(piece); if (e.length >= 4) tile(ti, tj)[lay].push(e); }
    }
  });
}
function addPolys(layer, shapes) {
  eachTile((ti, tj, x0, y0, x1, y1) => {
    for (const s of shapes) {
      if (!s.poly) continue;
      const c = clipPoly(s.pts, x0, y0, x1, y1);
      const e = encode(c);
      if (e.length >= 6) tile(ti, tj)[layer].push(e);
    }
  });
}

const shp = n => path.join(srcDir, n, n + ".shp"), dbf = n => path.join(srcDir, n, n + ".dbf");
addLines("admin1", readShp(shp("ne_10m_admin_1_states_provinces_lines")));
const roadRows = readDbf(dbf("ne_10m_roads"));
addLines(null, readShp(shp("ne_10m_roads")), s => {
  const r = roadRows[s.rec] || {};
  if (r.type === "Ferry Route" || r.featurecla === "Ferry") return null;
  return r.expressway === "1" || r.type === "Major Highway" ? "road" : "road2";
});
addPolys("urban", readShp(shp("ne_10m_urban_areas")));
const placeRows = readDbf(dbf("ne_10m_populated_places_simple"));
for (const s of readShp(shp("ne_10m_populated_places_simple"))) {
  if (!s.point) continue;
  const [x, y] = s.point;
  const ti = Math.floor((y + 90) / TILE), tj = Math.floor((x + 180) / TILE);
  if (ti < range.lat0 || ti > range.lat1 || tj < range.lon0 || tj > range.lon1) continue;
  const r = placeRows[s.rec] || {};
  tile(ti, tj).place.push([Math.round(x * SCALE), Math.round(y * SCALE), +(r.scalerank || 10), r.nameascii || r.name || ""]);
}
let total = 0;
for (const [k, t] of tiles) {
  const s = JSON.stringify(t);
  fs.writeFileSync(path.join(OUT, "ne_" + k + ".json"), s + "\n");
  total += s.length;
  console.log("ne_" + k, "admin1", t.admin1.length, "road", t.road.length, "road2", t.road2.length, "urban", t.urban.length, "place", t.place.length, "bytes", s.length);
}
console.log("tiles", tiles.size, "bytes", total);
