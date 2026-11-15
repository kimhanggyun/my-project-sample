// 병종 그림 (unity/Assets/Game/Resources/Ui/troops/<병종>.png, 256×256): 전투 병사와 같은 "가는 실루엣" (시안 C, 2026-11-15)을 평면으로 그린다.
// 쓰는 법: node tools/troop_pictures.js [출력 폴더]. 투석기(catapult.png)는 그대로 둔다 (모델 렌더).
// 색: 몸통 · 팔 = 아군 색, 얼굴 살색, 다리 · 투구 챙 · 방패 · 활 · 말 = 갈색, 칼 · 창날 = 쇠 (SoldierMeshes.cs와 같은 짝).
const fs = require("fs"), path = require("path"), zlib = require("zlib");
const OUT = process.argv[2] || path.join(__dirname, "..", "unity", "Assets", "Game", "Resources", "Ui", "troops");
const N = 256, SS = 4, W = N * SS;

const C = {
  team: [51, 92, 158], teamDark: [33, 60, 106], skin: [237, 199, 158], skinDark: [196, 150, 110],
  dark: [92, 66, 43], darkLine: [60, 42, 27], wood: [139, 94, 52], metal: [176, 182, 192], metalDark: [120, 126, 138], string: [230, 222, 205],
};

function canvas() { return new Float32Array(W * W * 4); }
function plot(px, i, c, a) {
  const o = i * 4, ia = 1 - a;
  px[o] = c[0] * a + px[o] * ia; px[o + 1] = c[1] * a + px[o + 1] * ia; px[o + 2] = c[2] * a + px[o + 2] * ia;
  px[o + 3] = a + px[o + 3] * ia;
}
// 모든 좌표는 256 기준
function shape(px, test, bbox, c, a = 1) {
  const [x0, y0, x1, y1] = bbox.map(v => v * SS);
  for (let y = Math.max(0, Math.floor(y0)); y < Math.min(W, Math.ceil(y1)); y++)
    for (let x = Math.max(0, Math.floor(x0)); x < Math.min(W, Math.ceil(x1)); x++)
      if (test((x + 0.5) / SS, (y + 0.5) / SS)) plot(px, y * W + x, c, a);
}
function circle(px, cx, cy, r, c) { shape(px, (x, y) => (x - cx) ** 2 + (y - cy) ** 2 <= r * r, [cx - r, cy - r, cx + r, cy + r], c); }
function ellipse(px, cx, cy, rx, ry, c, rot = 0) {
  const cs = Math.cos(rot), sn = Math.sin(rot), m = Math.max(rx, ry);
  shape(px, (x, y) => { const dx = x - cx, dy = y - cy, u = dx * cs + dy * sn, v = -dx * sn + dy * cs; return (u / rx) ** 2 + (v / ry) ** 2 <= 1; }, [cx - m, cy - m, cx + m, cy + m], c);
}
function line(px, ax, ay, bx, by, w, c) {
  const r = w / 2, dx = bx - ax, dy = by - ay, L = dx * dx + dy * dy || 1;
  shape(px, (x, y) => { let t = ((x - ax) * dx + (y - ay) * dy) / L; t = Math.max(0, Math.min(1, t)); const qx = ax + dx * t - x, qy = ay + dy * t - y; return qx * qx + qy * qy <= r * r; },
    [Math.min(ax, bx) - r, Math.min(ay, by) - r, Math.max(ax, bx) + r, Math.max(ay, by) + r], c);
}
function poly(px, pts, c) {
  let x0 = 1e9, y0 = 1e9, x1 = -1e9, y1 = -1e9;
  for (const [x, y] of pts) { x0 = Math.min(x0, x); y0 = Math.min(y0, y); x1 = Math.max(x1, x); y1 = Math.max(y1, y); }
  shape(px, (x, y) => { let inside = false; for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) { const [xi, yi] = pts[i], [xj, yj] = pts[j]; if ((yi > y) !== (yj > y) && x < xi + (y - yi) * (xj - xi) / (yj - yi)) inside = !inside; } return inside; }, [x0, y0, x1, y1], c);
}
function arc(px, cx, cy, r, a0, a1, w, c) {
  const n = 24; for (let i = 0; i < n; i++) { const t0 = a0 + (a1 - a0) * i / n, t1 = a0 + (a1 - a0) * (i + 1) / n; line(px, cx + r * Math.cos(t0), cy + r * Math.sin(t0), cx + r * Math.cos(t1), cy + r * Math.sin(t1), w, c); }
}

// 사람: (x, y) = 발밑 가운데, k = 크기, opts: helmet, leftArm/rightArm 끝, legs(false면 말 탄 자세)
function person(px, x, y, k, o = {}) {
  const P = (dx, dy) => [x + dx * k, y + dy * k];
  if (o.legs !== false) {
    line(px, ...P(-9, -76), ...P(-13, -4), 16 * k, C.darkLine);
    line(px, ...P(9, -76), ...P(13, -4), 16 * k, C.darkLine);
    line(px, ...P(-9, -76), ...P(-13, -4), 12 * k, C.dark);
    line(px, ...P(9, -76), ...P(13, -4), 12 * k, C.dark);
  } else {
    line(px, ...P(-6, -70), ...P(-20, -40), 12 * k, C.dark);
    line(px, ...P(-20, -40), ...P(-16, -14), 10 * k, C.dark);
  }
  if (o.behind) o.behind(P);
  const torso = [P(-25, -150), P(25, -150), P(17, -70), P(-17, -70)];
  const torsoOut = [P(-28, -153), P(28, -153), P(20, -67), P(-20, -67)];
  poly(px, torsoOut, C.teamDark); poly(px, torso, C.team);
  line(px, ...P(-18, -82), ...P(18, -82), 7 * k, C.dark); // 허리띠
  const la = o.leftArm || [-36, -96], ra = o.rightArm || [38, -100];
  line(px, ...P(-22, -144), ...P(la[0], la[1]), 14 * k, C.teamDark); line(px, ...P(-22, -144), ...P(la[0], la[1]), 10 * k, C.team);
  line(px, ...P(22, -144), ...P(ra[0], ra[1]), 14 * k, C.teamDark); line(px, ...P(22, -144), ...P(ra[0], ra[1]), 10 * k, C.team);
  circle(px, ...P(la[0], la[1]), 6 * k, C.skin); circle(px, ...P(ra[0], ra[1]), 6 * k, C.skin);
  line(px, ...P(0, -152), ...P(0, -162), 9 * k, C.skinDark); // 목
  circle(px, ...P(0, -180), 21 * k, C.skinDark); circle(px, ...P(0, -180), 19 * k, C.skin);
  if (o.helmet !== false) {
    ellipse(px, ...P(0, -194), 29 * k, 6 * k, C.darkLine);
    poly(px, [P(-16, -195), P(16, -195), P(0, -218)], C.dark); // 뾰족 투구
  } else line(px, ...P(-19, -188), ...P(19, -188), 6 * k, C.team); // 정찰병 머리띠
  if (o.front) o.front(P);
}

const pics = {
  infantry(px) {
    person(px, 128, 244, 1, {
      leftArm: [-34, -110], rightArm: [36, -104],
      front: P => {
        circle(px, ...P(-38, -110), 36, C.darkLine); circle(px, ...P(-38, -110), 32, C.wood);
        circle(px, ...P(-38, -110), 22, C.dark); circle(px, ...P(-38, -110), 8, C.metal);
        line(px, ...P(36, -104), ...P(56, -168), 7, C.metalDark); line(px, ...P(36, -104), ...P(56, -168), 4, C.metal);
        line(px, ...P(28, -112), ...P(44, -98), 6, C.dark);
      },
    });
  },
  spearman(px) {
    person(px, 120, 244, 1, {
      rightArm: [44, -112],
      behind: P => { line(px, ...P(46, -2), ...P(46, -226), 7, C.darkLine); line(px, ...P(46, -2), ...P(46, -226), 5, C.wood); },
      front: P => { poly(px, [P(46, -244), P(38, -220), P(54, -220)], C.metal); line(px, ...P(38, -218), ...P(54, -218), 4, C.metalDark); },
    });
  },
  archer(px) {
    person(px, 136, 244, 1, {
      leftArm: [-50, -124], rightArm: [24, -118],
      behind: P => { poly(px, [P(10, -170), P(26, -176), P(36, -110), P(20, -104)], C.darkLine); for (const dx of [16, 22, 28]) line(px, ...P(dx, -172), ...P(dx + 2, -188), 3, C.string); },
      front: P => {
        arc(px, ...P(-10, -124), 66, Math.PI * 0.68, Math.PI * 1.32, 7, C.darkLine);
        arc(px, ...P(-10, -124), 66, Math.PI * 0.68, Math.PI * 1.32, 5, C.wood);
        const a = P(-10 + 66 * Math.cos(Math.PI * 0.68), -124 + 66 * Math.sin(Math.PI * 0.68)), b = P(-10 + 66 * Math.cos(Math.PI * 1.32), -124 + 66 * Math.sin(Math.PI * 1.32));
        line(px, ...a, ...b, 1.6, C.string);
      },
    });
  },
  scout(px) {
    person(px, 128, 244, 1, {
      helmet: false, rightArm: [36, -100],
      front: P => { line(px, ...P(36, -100), ...P(46, -136), 6, C.metalDark); line(px, ...P(36, -100), ...P(46, -136), 3, C.metal); },
    });
  },
  engineer(px) {
    person(px, 124, 244, 1, {
      rightArm: [40, -110],
      behind: P => { line(px, ...P(-46, -170), ...P(40, -132), 20, C.darkLine); line(px, ...P(-46, -170), ...P(40, -132), 16, C.wood); },
      front: P => { line(px, ...P(40, -96), ...P(52, -170), 7, C.darkLine); line(px, ...P(40, -96), ...P(52, -170), 5, C.wood); poly(px, [P(46, -176), P(70, -186), P(72, -158), P(50, -156)], C.metal); },
    });
  },
  cavalry(px) {
    const H = C.dark, HL = C.darkLine;
    for (const [lx, ox] of [[86, -4], [100, 6], [160, -6], [174, 6]]) { line(px, lx, 168, lx + ox, 238, 13, HL); line(px, lx, 168, lx + ox, 238, 9, H); }
    line(px, 60, 150, 40, 196, 10, HL); // 꼬리
    ellipse(px, 128, 160, 72, 30, HL); ellipse(px, 128, 160, 69, 27, H);
    poly(px, [[168, 146], [192, 92], [212, 98], [196, 160]], HL); poly(px, [[171, 146], [193, 96], [208, 100], [194, 156]], H);
    ellipse(px, 214, 104, 25, 12, HL, 0.45); ellipse(px, 214, 104, 22, 10, H, 0.45);
    line(px, 196, 94, 202, 82, 6, H); // 귀
    ellipse(px, 124, 140, 30, 9, C.team); // 안장 덮개
    person(px, 122, 172, 0.62, {
      legs: false, rightArm: [40, -104],
      front: P => { line(px, ...P(40, -104), 246, 52, 5, C.darkLine); line(px, ...P(40, -104), 246, 52, 3, C.wood); poly(px, [[252, 46], [236, 50], [244, 60]], C.metal); },
    });
  },
};

function png(px) {
  const raw = Buffer.alloc((N * 4 + 1) * N);
  for (let y = 0; y < N; y++) {
    raw[y * (N * 4 + 1)] = 0;
    for (let x = 0; x < N; x++) {
      let r = 0, g = 0, b = 0, a = 0;
      for (let sy = 0; sy < SS; sy++) for (let sx = 0; sx < SS; sx++) { const o = ((y * SS + sy) * W + x * SS + sx) * 4; r += px[o]; g += px[o + 1]; b += px[o + 2]; a += px[o + 3]; }
      const n = SS * SS, o = y * (N * 4 + 1) + 1 + x * 4;
      // 색은 덮인 비율로 나눠 (투명 바탕과 섞이지 않게)
      raw[o] = a > 0 ? Math.round(r / a) : 0; raw[o + 1] = a > 0 ? Math.round(g / a) : 0; raw[o + 2] = a > 0 ? Math.round(b / a) : 0; raw[o + 3] = Math.round(a / n * 255);
    }
  }
  const crcTable = []; for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; crcTable[n] = c >>> 0; }
  const crc = buf => { let c = 0xffffffff; for (const v of buf) c = crcTable[(c ^ v) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const chunk = (type, data) => { const t = Buffer.from(type), len = Buffer.alloc(4), c = Buffer.alloc(4); len.writeUInt32BE(data.length); c.writeUInt32BE(crc(Buffer.concat([t, data]))); return Buffer.concat([len, t, data, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(N, 0); ihdr.writeUInt32BE(N, 4); ihdr[8] = 8; ihdr[9] = 6;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ihdr), chunk("IDAT", zlib.deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]);
}

for (const [id, draw] of Object.entries(pics)) {
  const px = canvas();
  draw(px);
  fs.writeFileSync(path.join(OUT, id + ".png"), png(px));
  console.log("wrote", id);
}
