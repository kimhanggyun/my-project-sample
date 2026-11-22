// 샘플전투2 맵 만들기 (R-135, 2026-11-22 사용자): 48 × 80칸 (1칸 = 1.25 전장 칸 → 60 × 100), 위 줄 = 적 쪽.
// 손으로 칸을 찍지 않고 모양(강 줄기 · 능선 · 길)을 식으로 그려 data/Maps/crowd_*.json으로 쓴다. 같은 식 = 같은 맵 (결정적).
// 실행: node tools/crowd_maps.js
// 지형 기호: . 평지 · g 풀숲 · r 길 · f 숲 · h 언덕 · m 산 · c 절벽 · p 고갯길 · ~ 강 · w 여울 · b 다리 · s 늪 · o 호수
// 공성 (R-140): W 성벽(못 지나감) · T 성벽 위(지키는 병사 자리) · S 계단 · G 성문(부서지기 전엔 닫힘). 성 안 = 지키는 편 시작 자리 (spawns)
// heights: 칸 높이 0~9 (3 = 언덕 한 단). 배치: 우리 = 아래 (spawn y 72), 적 = 위 (spawn y 8) — 그 둘레(위 · 아래 18줄)는 트인 땅.
const fs = require("fs");
const path = require("path");
const W = 48, H = 80;
const LEGEND = { ".": "plain", g: "grass", r: "road", f: "forest", h: "hill", m: "mountain", c: "cliff", p: "pass", "~": "river", w: "ford", b: "bridge", s: "marsh", o: "water", W: "wall", T: "wallTop", S: "stairs", G: "gate" };

// ---- 결정적 노이즈 ----
function hash(x, y, seed) {
  let h = (x * 374761393 + y * 668265263 + seed * 982451653) | 0;
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}
function smooth(t) { return t * t * (3 - 2 * t); }
function value(x, y, seed) {
  const x0 = Math.floor(x), y0 = Math.floor(y), fx = smooth(x - x0), fy = smooth(y - y0);
  const a = hash(x0, y0, seed), b = hash(x0 + 1, y0, seed), c = hash(x0, y0 + 1, seed), d = hash(x0 + 1, y0 + 1, seed);
  return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
}
/** 0 ~ 1 (여러 겹). scale = 칸 크기. */
function noise(x, y, seed, scale = 8) {
  let s = 0, amp = 1, tot = 0, f = 1 / scale;
  for (let o = 0; o < 3; o++) { s += value(x * f, y * f, seed + o * 17) * amp; tot += amp; amp *= 0.5; f *= 2; }
  return s / tot;
}

function blank(ch = ".") {
  return { t: Array.from({ length: H }, () => Array(W).fill(ch)), h: Array.from({ length: H }, () => Array(W).fill(0)) };
}
function inMap(x, y) { return x >= 0 && y >= 0 && x < W && y < H; }
function set(m, x, y, ch, ht) { if (!inMap(x, y)) return; m.t[y][x] = ch; if (ht !== undefined) m.h[y][x] = ht; }
/** 배치 자리 (위 · 아래 rows줄)는 트인 땅으로. */
function clearDeploy(m, rows = 17, ch = ".") {
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++)
    if (y < rows || y >= H - rows) { m.t[y][x] = ch; m.h[y][x] = 0; }
}
/** 곧은 선을 따라 굵기 width로 칠한다 (cb(x, y, d) — d = 선에서 거리). */
function stroke(points, width, cb) {
  for (let i = 0; i + 1 < points.length; i++) {
    const [x0, y0] = points[i], [x1, y1] = points[i + 1];
    const len = Math.hypot(x1 - x0, y1 - y0), steps = Math.max(1, Math.ceil(len * 2));
    for (let k = 0; k <= steps; k++) {
      const px = x0 + (x1 - x0) * k / steps, py = y0 + (y1 - y0) * k / steps;
      const r = Math.ceil(width / 2) + 1;
      for (let dy = -r; dy <= r; dy++) for (let dx = -r; dx <= r; dx++) {
        const x = Math.round(px) + dx, y = Math.round(py) + dy;
        const d = Math.hypot(x - px, y - py);
        if (d <= width / 2 && inMap(x, y)) cb(x, y, d);
      }
    }
  }
}
function disc(m, cx, cy, r, cb) {
  for (let y = Math.floor(cy - r); y <= Math.ceil(cy + r); y++) for (let x = Math.floor(cx - r); x <= Math.ceil(cx + r); x++)
    if (inMap(x, y) && Math.hypot(x - cx, y - cy) <= r) cb(x, y, Math.hypot(x - cx, y - cy));
}

function write(id, comment, m, spawns) {
  const doc = {
    _comment: comment,
    id, battleType: "field",
    legend: LEGEND,
    rows: m.t.map(r => r.join("")),
    heights: m.h.map(r => r.map(v => String(Math.max(0, Math.min(9, Math.round(v))))).join("")),
    spawns: spawns || { teamA: { x: 24, y: 72, facing: "N" }, teamB: { x: 24, y: 8, facing: "S" } },
    retreatEdges: { teamA: "south", teamB: "north" },
  };
  // 쓰는 기호만 범례에
  const used = new Set(doc.rows.join(""));
  doc.legend = Object.fromEntries(Object.entries(LEGEND).filter(([k]) => used.has(k)));
  const lines = JSON.stringify(doc, null, 2)
    .replace(/"rows": \[[^\]]*\]/s, '"rows": [\n' + doc.rows.map(r => '    "' + r + '"').join(",\n") + "\n  ]")
    .replace(/"heights": \[[^\]]*\]/s, '"heights": [\n' + doc.heights.map(r => '    "' + r + '"').join(",\n") + "\n  ]");
  const file = path.join(__dirname, "..", "data", "Maps", id + ".json");
  fs.writeFileSync(file, lines + "\n");
  console.log("wrote", file);
}

// ---- 1. 평원: 모두 평지 (진형 · 병종 비교용) ----
{
  const m = blank(".");
  write("crowd_plain", "샘플전투2 평원 (R-135): 모두 평지 — 진형 · 병종만 비교", m);
}

// ---- 2. 고지전: 적(위)이 높은 곳, 우리 쪽으로 내려오며 계속 낮아지는 긴 비탈 (둔덕 · 골 · 덤불 · 숲 조각) ----
{
  const m = blank(".");
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    // 높이: 아래(우리) 0 → 위(적) 9, 둔덕 · 골을 노이즈로
    const t = Math.min(1, Math.max(0, (64 - y) / 50));
    let h = 9 * smooth(t) + (noise(x, y, 11, 7) - 0.5) * 3.2 * (t > 0.05 && t < 0.98 ? 1 : 0.3);
    // 비탈을 가로지르는 골 두 줄 (쉬어 갈 곳)
    const gully = Math.abs(y - (38 + 4 * Math.sin(x / 6))) < 1.5 ? 1.2 : 0;
    h -= gully;
    h = Math.max(0, Math.min(9, h));
    m.h[y][x] = h;
    m.t[y][x] = h >= 0.8 ? "h" : ".";
    const n = noise(x, y, 23, 5);
    if (n > 0.66) m.t[y][x] = h >= 0.8 ? "g" : "g";
    if (noise(x, y, 37, 6) > 0.78 && y > 18 && y < 58) m.t[y][x] = "f"; // 숲 조각 (엄폐)
  }
  // 고지 위 (적 배치): 평평한 꼭대기
  for (let y = 0; y < 17; y++) for (let x = 0; x < W; x++) { m.h[y][x] = 9; m.t[y][x] = "h"; }
  for (let y = H - 17; y < H; y++) for (let x = 0; x < W; x++) { m.h[y][x] = 0; m.t[y][x] = "."; }
  write("crowd_highland", "샘플전투2 고지전 (R-135): 적이 높은 곳에 자리 잡고, 우리는 둔덕 · 골이 있는 긴 비탈을 오르며 화살을 맞는다", m);
}

// ---- 3. 협곡: 절벽 사이로 구불구불 좁은 통로 (넓은 터 둘 · 돌아가는 샛길 하나). 절벽은 못 오름 (나중에 산악병만) ----
{
  const m = blank("c");
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) m.h[y][x] = 7 + noise(x, y, 5, 5) * 2.5;
  const main = [], side = [];
  for (let y = H - 1; y >= 0; y--) main.push([24 + 9 * Math.sin(y / 9) + 3 * Math.sin(y / 4.3), y]);
  stroke(main, 6, (x, y) => set(m, x, y, "p", 0));
  // 넓은 터 둘 (싸움터)
  disc(m, 24 + 9 * Math.sin(30 / 9) + 3 * Math.sin(30 / 4.3), 30, 6, (x, y) => set(m, x, y, "p", 0));
  disc(m, 24 + 9 * Math.sin(52 / 9) + 3 * Math.sin(52 / 4.3), 52, 5, (x, y) => set(m, x, y, "p", 0));
  // 돌아가는 좁은 샛길 (아래 터 → 위 터, 반대편 벽을 끼고)
  for (let y = 52; y >= 30; y--) side.push([8 + 3 * Math.sin(y / 3), y]);
  stroke([[24 + 9 * Math.sin(52 / 9) + 3 * Math.sin(52 / 4.3), 52], ...side, [24 + 9 * Math.sin(30 / 9) + 3 * Math.sin(30 / 4.3), 30]], 2.5, (x, y) => set(m, x, y, "p", 0));
  clearDeploy(m, 15, ".");
  write("crowd_gorge", "샘플전투2 협곡 (R-135): 절벽 사이 구불구불 좁은 통로, 넓은 터 둘, 돌아가는 좁은 샛길 하나. 절벽은 못 오름", m);
}

// ---- 4. 큰 강: 넓게 굽이치는 강, 왼쪽 넓은 여울 · 가운데 모래톱 섬 · 오른쪽 다리, 다리 너머는 지류로 한 번 더 건너야 ----
{
  const m = blank(".");
  const yr = x => 40 + 6 * Math.sin(x / 7.5) + 2 * Math.sin(x / 3.1);
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    if (noise(x, y, 41, 6) > 0.64) m.t[y][x] = "g";
    if (noise(x, y, 43, 5) > 0.8) m.t[y][x] = "f"; // 강가 숲 (엄폐)
  }
  // 강 (폭 7칸 ≈ 9 전장 칸)
  const half = (x, y) => 3.5 + 1.6 * (noise(x, y, 45, 5) - 0.5) + 4.5 * Math.min(1, Math.max(0, (22 - x) / 12)); // 왼쪽은 두 배 넓게 (건너기 위험 vs 오른쪽 다리로 멀리 돌기, 2026-11-22 사용자) // 강폭이 고르지 않게 (곧은 둑이 네모로 보임, 캡처 41c)
  for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) if (Math.abs(y - yr(x)) <= half(x, y)) set(m, x, y, "~");
  // 지류: 강(가로 30쯤)에서 오른쪽 위로 맵 오른쪽 끝까지
  const trib = [];
  for (let k = 0; k <= 30; k++) { const x = 30 + k * 0.6, y = yr(30) - 2 - k * 0.7; trib.push([x + 1.2 * Math.sin(k / 3), y]); }
  stroke(trib, 3.5, (x, y) => set(m, x, y, "~"));
  // 건너는 곳
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) { // 넓은 여울: 비스듬한 여울톱 (가장자리가 곧지 않게)
    const a = 6 + 2.5 * noise(x, y, 47, 3) + (y - 40) * 0.35, b = 15 + 2.5 * noise(x, y, 49, 3) - (y - 40) * 0.35;
    if (m.t[y][x] === "~" && x >= a && x <= b) set(m, x, y, "w");
  }
  for (let x = 22; x <= 27; x++) for (let y = 0; y < H; y++) if (Math.abs(y - yr(x)) <= 1.5) set(m, x, y, "."); // 모래톱 섬
  for (let x = 23; x <= 25; x++) for (let y = 0; y < H; y++) if (m.t[y][x] === "~" && Math.abs(y - yr(x)) <= 5) set(m, x, y, "w"); // 섬 오가는 얕은 곳
  for (let x = 38; x <= 40; x++) for (let y = 0; y < H; y++) if (m.t[y][x] === "~" && Math.abs(y - yr(x)) <= 4.4) set(m, x, y, "b"); // 다리 (본류만)
  // 지류 여울 (다리 건너 한 번 더)
  stroke([[trib[16][0] - 3, trib[16][1]], [trib[16][0] + 3, trib[16][1]]], 2, (x, y) => { if (m.t[y][x] === "~") set(m, x, y, "w"); });
  clearDeploy(m, 15, ".");
  write("crowd_river", "샘플전투2 큰 강 (R-135): 넓게 굽이치는 강 — 왼쪽 넓은 여울(느림), 가운데 모래톱 섬, 오른쪽 좁은 다리(빠른 병목). 다리 너머는 지류를 한 번 더 건넌다", m);
}

// ---- 5. 늪지: 온통 늪 · 호수, 마른 섬 몇 곳 (먼저 차지하면 유리), 둑길로 일부만 이어짐 ----
{
  const m = blank("s");
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) if (noise(x, y, 51, 7) > 0.6) set(m, x, y, "o");
  const islands = [[12, 30, 4], [36, 28, 3.5], [24, 40, 4.5], [10, 52, 3.5], [37, 51, 4]];
  for (const [cx, cy, r] of islands) disc(m, cx, cy, r, (x, y, d) => set(m, x, y, d < r - 1 ? "." : "g", d < r - 1.5 ? 1 : 0));
  // 둑길 (좁은 길): 우리 쪽 → 왼쪽 아래 섬, 적 쪽 → 오른쪽 위 섬, 가운데 섬은 늪으로만
  stroke([[18, 63], [10, 52]], 2, (x, y) => { if (m.t[y][x] !== "." && m.t[y][x] !== "g") set(m, x, y, "r", 0); });
  stroke([[30, 16], [36, 28]], 2, (x, y) => { if (m.t[y][x] !== "." && m.t[y][x] !== "g") set(m, x, y, "r", 0); });
  stroke([[10, 52], [12, 30]], 1.6, (x, y) => { if (m.t[y][x] === "o") set(m, x, y, "s"); }); // 왼쪽 섬끼리는 늪으로 (호수는 비켜)
  clearDeploy(m, 16, ".");
  for (let x = 0; x < W; x++) { if (noise(x, 16, 53, 4) > 0.5) set(m, x, 16, "s"); if (noise(x, 63, 53, 4) > 0.5) set(m, x, 63, "s"); }
  write("crowd_marsh", "샘플전투2 늪지 (R-135): 온통 늪 · 호수(못 건넘), 마른 섬 다섯 — 먼저 차지하면 유리. 둑길은 양쪽에서 한 섬씩만", m);
}

// ---- 6. 숲길: 온통 숲, 가운데 숲길 + 왼쪽으로 크게 도는 오솔길 (우회 · 복병 자리) ----
{
  const m = blank("f");
  const road = [];
  for (let y = H - 1; y >= 0; y--) road.push([24 + 3 * Math.sin(y / 11), y]);
  stroke(road, 4, (x, y) => set(m, x, y, "r"));
  // 오솔길: 숲길(아래 60줄)에서 왼쪽으로 빠져 위로, 16줄에서 다시 합류
  const trail = [[24 + 3 * Math.sin(60 / 11), 60], [16, 58], [9, 52]];
  for (let y = 50; y >= 24; y -= 2) trail.push([7 + 2 * Math.sin(y / 5), y]);
  trail.push([12, 20], [24 + 3 * Math.sin(16 / 11), 16]);
  stroke(trail, 2, (x, y) => set(m, x, y, "r"));
  // 숲 속 빈터 (오솔길 가운데 — 복병이 기다리기 좋은 곳), 오른쪽 작은 빈터
  disc(m, 10, 38, 3, (x, y) => set(m, x, y, "g"));
  disc(m, 38, 44, 2.5, (x, y) => set(m, x, y, "g"));
  // 배치 빈터 (가장자리는 숲)
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++)
    if ((y < 13 || y >= H - 13) && x >= 6 && x < W - 6) set(m, x, y, noise(x, y, 61, 4) > 0.7 ? "g" : ".");
  write("crowd_forest", "샘플전투2 숲길 (R-135): 온통 숲, 가운데 숲길과 왼쪽으로 크게 도는 오솔길 (우회 · 복병 자리, 숨는 지형 규칙은 다음)", m);
}

// ---- 7. 산길: 산맥 사이로 구불구불한 고갯길, 넓은 분지 둘(궁병 둔덕), 산을 넘는 좁은 샛길 하나 ----
{
  const m = blank("m");
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) m.h[y][x] = 6 + noise(x, y, 71, 6) * 3.5;
  const xc = y => 24 + 10 * Math.sin(y / 12) + 3 * Math.sin(y / 5);
  const pass = [];
  for (let y = H - 1; y >= 0; y--) pass.push([xc(y), y]);
  // 분지 둘 (가장자리 둔덕) → 그 위에 고갯길 (길이 분지 테두리에 막히지 않게) → 궁병 둔덕
  for (const [cy, r] of [[28, 7], [52, 6.5]]) disc(m, xc(cy), cy, r, (x, y, d) => set(m, x, y, d > r - 1.5 ? "h" : "p", d > r - 1.5 ? 2 : 0));
  stroke(pass, 7, (x, y, d) => { if (m.t[y][x] !== "p") set(m, x, y, "p", d > 2.5 ? 1 : 0); else m.h[y][x] = Math.min(m.h[y][x], d > 2.5 ? 1 : 0); });
  for (const cy of [28, 52]) disc(m, xc(cy) + (cy < 40 ? 3 : -3), cy - 2, 1.8, (x, y) => set(m, x, y, "h", 3));
  // 산을 넘는 샛길 (언덕길, 기병도 다님 — 느림): 아래 분지에서 반대편으로 돌아 위 분지로
  const ridge = [];
  for (let y = 52; y >= 28; y--) ridge.push([xc(y) + (xc(y) > 24 ? -13 : 13) + 2 * Math.sin(y / 4), y]);
  stroke([[xc(52), 52], ...ridge, [xc(28), 28]], 2.2, (x, y) => { if (m.t[y][x] === "m") set(m, x, y, "h", 4); });
  clearDeploy(m, 15, ".");
  write("crowd_pass", "샘플전투2 산길 (R-135): 산맥 사이 구불구불한 고갯길(바닥엔 돌 없음), 넓은 분지 둘과 궁병 둔덕, 산을 넘는 좁은 언덕 샛길 하나", m);
}

// ---- 공성 시험 (R-140): 평지 위 네모 성 — 앞 성벽 가운데 성문 하나, 성벽 위 걷는 길, 계단 둘. 지키는 편(teamB)은 성 안 ----
// (공성전 맵 세트는 원격이 이 기호로 만듦: 평지 성곽 · 강가 성채 · 산성)
{
  const m = blank(".");
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) if (noise(x, y, 81, 6) > 0.66) m.t[y][x] = "g";
  const x0 = 8, x1 = 39, y0 = 4, y1 = 30; // 성벽 테두리 (y1 = 앞 성벽, 우리 쪽)
  for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) {
    const edge = y === y0 || y === y1 || x === x0 || x === x1;
    const top = !edge && (y >= y1 - 2 || y <= y0 + 1 || x <= x0 + 2 || x >= x1 - 2);
    if (edge) set(m, x, y, "W", 7);
    else if (top) set(m, x, y, "T", 6);
    else set(m, x, y, ".", 0);
  }
  for (let x = 22; x <= 25; x++) { set(m, x, y1, "G", 0); set(m, x, y1 - 1, ".", 0); set(m, x, y1 - 2, ".", 0); } // 성문 + 문 안 통로
  for (const sx of [13, 33]) for (let x = sx; x <= sx + 1; x++) { set(m, x, y1 - 3, "S", 4); set(m, x, y1 - 4, "S", 2); } // 계단 (마당 → 성벽 위)
  write("crowd_siege", "샘플전투2 공성 시험 (R-140): 평지 위 네모 성, 앞 성벽 가운데 성문 하나 · 성벽 위 걷는 길 · 계단 둘. 지키는 편(teamB)은 성 안", m,
    { teamA: { x: 24, y: 72, facing: "N" }, teamB: { x: 24, y: 14, facing: "S" } });
}
