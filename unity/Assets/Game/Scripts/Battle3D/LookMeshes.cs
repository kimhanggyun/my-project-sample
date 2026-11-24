using System.Collections.Generic;
using UnityEngine;

namespace Game.Unity.Battle3D
{
    /// <summary>
    /// 부품으로 사람 · 말 조립 (R-150). 원점은 발밑, 앞 = +Z, 병사 키 약 0.36 (장수는 ×<see cref="SoldierMeshes.GeneralScale"/>).
    /// 몸 (몸통 · 팔 · 손 · 목 · 얼굴 · 허리띠)과 말 몸은 여기서, 투구 · 갑옷 · 등 · 무기 · 방패 · 말 갖춤은 <see cref="LookPartLibrary"/> 부품.
    /// 색은 색 자리마다 하위 메시 (<see cref="ColorNames"/>, 그리는 쪽이 <see cref="SoldierLook.ColorFor"/>로 칠함).
    /// 말 (2026-11-24 사용자 "말 디자인 다시, 사람과 말이 떨어져 있는 것"): 둥근 몸통 · 가슴 · 엉덩이 · 굽은 목 · 주둥이 머리 · 귀 · 갈기 · 꼬리, 두 마디 다리 + 발굽.
    /// 탄 사람은 엉덩이가 안장 위에 닿게 (<see cref="RiderLift"/>) 다리를 말 옆구리로 벌려 내린다.
    /// 도형은 직접 만든 낮은 다각형 (상자 24 · 원기둥 52 · 공 117 꼭짓점 안팎 — 병사 수백이라 가볍게).
    /// </summary>
    public static class LookMeshes
    {
        /// <summary>색 자리 (하위 메시 번호 = 차례).</summary>
        public static readonly string[] ColorNames = { "main", "skin", "metal", "dark", "trim", "horse", "wood", "gold", "plume", "cape", "hat", "shield", "legs", "mane", "team", "white" };
        private static readonly Dictionary<string, int> ColorIndex = new Dictionary<string, int>();
        static LookMeshes() { for (int i = 0; i < ColorNames.Length; i++) ColorIndex[ColorNames[i]] = i; }

        /// <summary>부품이 붙는 자리 (사람: 선 자세 기준, 탄 사람은 <see cref="RiderLift"/>만큼 올림 · 말: 말 기준).</summary>
        public static readonly Dictionary<string, Vector3> Anchors = new Dictionary<string, Vector3>
        {
            ["head"] = new Vector3(0f, 0.33f, 0f),
            ["chest"] = new Vector3(0f, 0.235f, 0f),
            ["waist"] = new Vector3(0f, 0.15f, 0f),
            ["back"] = new Vector3(0f, 0.235f, -0.02f),
            ["rightHand"] = new Vector3(0.066f, 0.183f, 0.012f),
            ["leftHand"] = new Vector3(-0.066f, 0.183f, 0.012f),
            ["feet"] = Vector3.zero,
            ["saddle"] = new Vector3(0f, 0.218f, -0.01f),
            ["horseHead"] = new Vector3(0f, 0.29f, 0.19f),
            ["horseNeck"] = new Vector3(0f, 0.245f, 0.14f),
            ["horseBody"] = new Vector3(0f, 0.175f, 0f),
            ["horseRump"] = new Vector3(0f, 0.19f, -0.095f),
            ["horseChest"] = new Vector3(0f, 0.185f, 0.1f),
        };
        private static bool HorseAnchor(string at) => at == "saddle" || (at != null && at.StartsWith("horse"));

        /// <summary>탄 사람을 올리는 높이 (엉덩이 0.138 → 안장 위 0.23).</summary>
        public const float RiderLift = 0.092f;
        /// <summary>말 다리 위 끝 높이 · 길이 (땅까지).</summary>
        public const float HorseLegTop = 0.15f;

        public static readonly string[] Shapes = { "box", "sphere", "cylinder", "capsule", "cone", "dome" };

        private static readonly Dictionary<string, Mesh> Built = new Dictionary<string, Mesh>();
        private static readonly Dictionary<Mesh, int[]> SubSlots = new Dictionary<Mesh, int[]>();

        /// <summary>메시의 하위 메시마다 색 자리 이름.</summary>
        public static string[] ColorsOf(Mesh m)
        {
            if (m == null || !SubSlots.TryGetValue(m, out var s)) return new string[0];
            var o = new string[s.Length];
            for (int i = 0; i < s.Length; i++) o[i] = ColorNames[s[i]];
            return o;
        }

        /// <summary>말 다리 넷의 위 끝 자리 (0 앞왼 · 1 앞오른 · 2 뒤왼 · 3 뒤오른).</summary>
        public static Vector3 HorseLegAt(int i, bool general) =>
            new Vector3(i % 2 == 0 ? -0.024f : 0.024f, HorseLegTop, i < 2 ? 0.088f : -0.088f) * (general ? SoldierMeshes.GeneralScale : 1f);

        /// <summary>말 다리 한 짝 (위 끝 = 원점): 굵은 윗마디 · 가는 아랫마디 · 발굽 (horse · dark).</summary>
        public static Mesh HorseLeg(bool general)
        {
            string key = general ? "#look_horseLeg_g" : "#look_horseLeg";
            if (Built.TryGetValue(key, out var m) && m != null) return m;
            var b = new Builder(general ? SoldierMeshes.GeneralScale : 1f);
            HorseLegPieces(b, Vector3.zero);
            return Keep(key, b.Build("look_horse_leg"), b);
        }

        private static void HorseLegPieces(Builder b, Vector3 top)
        {
            b.Limb("horse", top, top + new Vector3(0f, -0.072f, 0.004f), 0.026f);
            b.Add("sphere", "horse", top + new Vector3(0f, -0.074f, 0.004f), new Vector3(0.022f, 0.024f, 0.022f));
            b.Limb("horse", top + new Vector3(0f, -0.074f, 0.004f), top + new Vector3(0f, -0.138f, 0f), 0.016f);
            b.Add("cylinder", "dark", top + new Vector3(0f, -0.144f, 0.002f), new Vector3(0.021f, 0.012f, 0.024f));
        }

        /// <summary>
        /// 겉모습 한 벌의 몸 (사람 + 탄 말). legs = true면 걷는 다리 · 말 다리까지 붙인 통째 (쓰러짐 · 그림), false면 빼고 (전투가 따로 흔든다).
        /// </summary>
        public static Mesh Body(SoldierLook look, bool legs)
        {
            string key = "#look|" + look.ShapeKey + (legs ? "|L" : "|-");
            if (Built.TryGetValue(key, out var m) && m != null) return m;
            var b = new Builder(look.General ? SoldierMeshes.GeneralScale : 1f);
            float lift = look.Mounted ? RiderLift : 0f;
            if (look.Mounted)
            {
                HorseBody(b);
                if (legs) for (int i = 0; i < 4; i++) HorseLegPieces(b, HorseLegAt(i, false));
            }
            Person(b, lift, look.Mounted, legs);
            var lib = look.Library;
            if (lib != null)
            {
                foreach (var slot in LookPartLibrary.Slots)
                {
                    if (slot == LookPartLibrary.SlotTack && !look.Mounted) continue;
                    AddPart(b, lib.Find(look.Of(slot)), lift);
                }
                if (look.Commander) AddPart(b, lib.Find(lib.CommanderExtra), lift);
            }
            return Keep(key, b.Build("look_" + look.ShapeKey), b);
        }

        /// <summary>부품 하나만 (꾸미기 탭 · 검사용, 원점 = 붙는 자리 그대로).</summary>
        public static void AddPart(Builder b, LookPart part, float lift)
        {
            if (part == null) return;
            foreach (var p in part.Pieces)
            {
                if (p.At == null || !Anchors.TryGetValue(p.At, out var at)) continue;
                if (!HorseAnchor(p.At)) at.y += lift;
                b.Add(p.Shape, p.Color, at + p.Pos, p.Size, p.Rot);
                if (p.Mirror) b.Add(p.Shape, p.Color, at + new Vector3(-p.Pos.x, p.Pos.y, p.Pos.z), p.Size, new Vector3(p.Rot.x, -p.Rot.y, -p.Rot.z));
            }
        }

        /// <summary>사람 몸 (갑옷 없는 웃옷 = main): 어깨 넓고 허리 좁은 몸통, 팔, 손, 목, 얼굴, 허리띠. 탄 사람은 다리를 말 옆구리로.</summary>
        private static void Person(Builder b, float lift, bool mounted, bool legs)
        {
            var up = new Vector3(0f, lift, 0f);
            b.Add("capsule", "main", up + new Vector3(0f, 0.235f, 0f), new Vector3(0.06f, 0.1f, 0.036f));
            b.Add("capsule", "main", up + new Vector3(0f, 0.175f, 0f), new Vector3(0.046f, 0.09f, 0.032f));
            for (int s = -1; s <= 1; s += 2)
            {
                b.Limb("main", up + new Vector3(0.034f * s, 0.262f, 0.004f), up + new Vector3(0.064f * s, 0.192f, 0.012f), 0.017f);
                b.Add("sphere", "skin", up + new Vector3(0.066f * s, 0.183f, 0.012f), new Vector3(0.02f, 0.02f, 0.02f));
            }
            b.Add("cylinder", "dark", up + new Vector3(0f, 0.15f, 0f), new Vector3(0.05f, 0.012f, 0.036f));
            b.Add("cylinder", "skin", up + new Vector3(0f, 0.293f, 0f), new Vector3(0.018f, 0.024f, 0.018f));
            b.Add("sphere", "skin", up + new Vector3(0f, 0.33f, 0f), new Vector3(0.062f, 0.064f, 0.062f));
            if (mounted)
            {
                // 말 탄 다리: 엉덩이 → 무릎 (앞 · 바깥으로) → 발 (아래로, 등자 자리)
                for (int s = -1; s <= 1; s += 2)
                {
                    var hip = up + new Vector3(0.026f * s, 0.14f, 0f);
                    var knee = new Vector3(0.05f * s, 0.2f, 0.042f);
                    var foot = new Vector3(0.05f * s, 0.142f, 0.03f);
                    b.Limb("legs", hip, knee, 0.024f);
                    b.Limb("legs", knee, foot, 0.021f);
                    b.Add("box", "dark", foot + new Vector3(0f, -0.006f, 0.008f), new Vector3(0.02f, 0.012f, 0.03f));
                }
            }
            else if (legs)
            {
                for (int s = -1; s <= 1; s += 2)
                    b.Limb("legs", new Vector3(SoldierMeshes.HipSide * s, SoldierMeshes.HipHeight, 0f), new Vector3(SoldierMeshes.HipSide * s * 1.1f, 0f, 0f), 0.026f);
            }
        }

        /// <summary>말 몸 (다리 빼고): 둥근 몸통 · 가슴 · 엉덩이 · 굽은 목 · 주둥이 머리 · 귀 · 눈 · 갈기 · 앞머리 · 꼬리.</summary>
        private static void HorseBody(Builder b)
        {
            b.Add("capsule", "horse", new Vector3(0f, 0.175f, 0f), new Vector3(0.075f, 0.26f, 0.085f), new Vector3(90f, 0f, 0f));
            b.Add("sphere", "horse", new Vector3(0f, 0.185f, 0.1f), new Vector3(0.072f, 0.09f, 0.075f));
            b.Add("sphere", "horse", new Vector3(0f, 0.19f, -0.095f), new Vector3(0.078f, 0.09f, 0.085f));
            b.Add("capsule", "horse", new Vector3(0f, 0.245f, 0.14f), new Vector3(0.042f, 0.11f, 0.05f), new Vector3(35f, 0f, 0f));
            b.Add("capsule", "horse", new Vector3(0f, 0.29f, 0.19f), new Vector3(0.036f, 0.075f, 0.042f), new Vector3(115f, 0f, 0f));
            b.Add("sphere", "horse", new Vector3(0f, 0.272f, 0.226f), new Vector3(0.032f, 0.03f, 0.03f));
            b.Add("sphere", "dark", new Vector3(0f, 0.27f, 0.24f), new Vector3(0.02f, 0.014f, 0.006f));
            for (int s = -1; s <= 1; s += 2)
            {
                b.Add("cone", "horse", new Vector3(0.011f * s, 0.324f, 0.166f), new Vector3(0.01f, 0.024f, 0.008f), new Vector3(-10f, 0f, -12f * s));
                b.Add("sphere", "dark", new Vector3(0.017f * s, 0.298f, 0.192f), new Vector3(0.007f, 0.007f, 0.007f));
            }
            b.Add("box", "mane", new Vector3(0f, 0.268f, 0.122f), new Vector3(0.012f, 0.1f, 0.026f), new Vector3(35f, 0f, 0f));
            b.Add("box", "mane", new Vector3(0f, 0.316f, 0.18f), new Vector3(0.012f, 0.014f, 0.02f), new Vector3(20f, 0f, 0f));
            b.Add("capsule", "mane", new Vector3(0f, 0.16f, -0.16f), new Vector3(0.024f, 0.11f, 0.024f), new Vector3(27f, 0f, 0f));
        }

        private static Mesh Keep(string key, Mesh m, Builder b)
        {
            SubSlots[m] = b.UsedSlots;
            return Built[key] = m;
        }

        /// <summary>도형을 색 자리별로 모아 하위 메시로 합침.</summary>
        public sealed class Builder
        {
            private readonly List<CombineInstance>[] _parts = new List<CombineInstance>[ColorNames.Length];
            private readonly float _k;
            public int[] UsedSlots { get; private set; } = new int[0];

            public Builder(float scale = 1f)
            {
                _k = scale;
                for (int i = 0; i < _parts.Length; i++) _parts[i] = new List<CombineInstance>();
            }

            /// <summary>도형 하나: pos = 가운데, size = 전체 크기, rot = 돌림 (도). 모르는 색 자리는 main.</summary>
            public void Add(string shape, string color, Vector3 pos, Vector3 size, Vector3 rot = default)
            {
                if (!ColorIndex.TryGetValue(color ?? "main", out int ci)) ci = 0;
                Mesh mesh;
                Vector3 scale = size;
                switch (shape)
                {
                    case "sphere": mesh = Prims.Sphere; break;
                    case "cylinder": mesh = Prims.Cylinder; break;
                    case "cone": mesh = Prims.Cone; break;
                    case "dome": mesh = Prims.Dome; break;
                    case "capsule":
                        // 반지름 = 가로의 절반, 길이 = 높이 (반구 포함). 가로로 맞춘 단위 캡슐을 가로 · 가로 · 깊이로 늘림
                        float w = Mathf.Max(0.0001f, size.x);
                        mesh = Prims.Capsule(size.y / w);
                        scale = new Vector3(size.x, size.x, size.z);
                        break;
                    default: mesh = Prims.Box; break;
                }
                _parts[ci].Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(pos * _k, Quaternion.Euler(rot), scale * _k) });
            }

            /// <summary>두 점을 잇는 원기둥 (팔 · 다리).</summary>
            public void Limb(string color, Vector3 a, Vector3 b, float thick)
            {
                var d = b - a;
                if (!ColorIndex.TryGetValue(color, out int ci)) ci = 0;
                var rot = Quaternion.FromToRotation(Vector3.up, d.normalized);
                _parts[ci].Add(new CombineInstance { mesh = Prims.Cylinder, transform = Matrix4x4.TRS((a + b) * 0.5f * _k, rot, new Vector3(thick, d.magnitude, thick) * _k) });
            }

            public Mesh Build(string name)
            {
                var subs = new List<CombineInstance>();
                var used = new List<int>();
                for (int i = 0; i < _parts.Length; i++)
                {
                    if (_parts[i].Count == 0) continue;
                    var pm = new Mesh();
                    pm.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    pm.CombineMeshes(_parts[i].ToArray(), true, true);
                    subs.Add(new CombineInstance { mesh = pm, transform = Matrix4x4.identity });
                    used.Add(i);
                }
                var m = new Mesh { name = name };
                m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.CombineMeshes(subs.ToArray(), false, false);
                foreach (var s in subs) { if (Application.isPlaying) Object.Destroy(s.mesh); else Object.DestroyImmediate(s.mesh); }
                m.RecalculateBounds();
                UsedSlots = used.ToArray();
                return m;
            }
        }

        /// <summary>낮은 다각형 단위 도형 (가운데 원점, 전체 크기 1).</summary>
        private static class Prims
        {
            private const int Seg = 12;
            private static Mesh _box, _sphere, _cyl, _cone, _dome;
            private static readonly Dictionary<int, Mesh> Caps = new Dictionary<int, Mesh>();

            public static Mesh Box => _box ? _box : (_box = MakeBox());
            public static Mesh Sphere => _sphere ? _sphere : (_sphere = MakeLathe(Ring(-0.5f, 0.5f, 8, 0f, 1f), "sphere"));
            public static Mesh Dome => _dome ? _dome : (_dome = MakeLathe(Ring(-0.5f, 0.5f, 4, 0.5f, 1f, true), "dome", capBottom: -0.5f));
            public static Mesh Cylinder => _cyl ? _cyl : (_cyl = MakeCylinder(0.5f, 0.5f, "cylinder"));
            public static Mesh Cone => _cone ? _cone : (_cone = MakeCylinder(0.5f, 0f, "cone"));

            /// <summary>길이 ÷ 지름 = ratio인 캡슐 (지름 1).</summary>
            public static Mesh Capsule(float ratio)
            {
                int q = Mathf.Clamp(Mathf.RoundToInt(ratio * 20f), 20, 400);
                if (Caps.TryGetValue(q, out var m) && m) return m;
                float h = q / 20f;
                float straight = (h - 1f) * 0.5f;
                var prof = new List<Vector2>();
                var nor = new List<Vector2>();
                for (int i = 0; i <= 4; i++)
                {
                    float a = -Mathf.PI / 2f + i / 4f * Mathf.PI / 2f;
                    prof.Add(new Vector2(Mathf.Cos(a) * 0.5f, -straight + Mathf.Sin(a) * 0.5f));
                    nor.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
                }
                for (int i = 0; i <= 4; i++)
                {
                    float a = i / 4f * Mathf.PI / 2f;
                    prof.Add(new Vector2(Mathf.Cos(a) * 0.5f, straight + Mathf.Sin(a) * 0.5f));
                    nor.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
                }
                m = MakeLathe((prof, nor), "capsule");
                // 높이 h를 1로 (가로 · 깊이로 늘리는 쪽이 size.y = 가로 × h가 되게): 그대로 두고 scale y = 가로 → 길이 = 가로 × h
                return Caps[q] = m;
            }

            /// <summary>반원 단면 (fromY~toY 사이 rings 칸): t0~t1 = 아래(0)~위(1) 중 쓸 몫, squash = 반구를 높이 1로 늘림.</summary>
            private static (List<Vector2>, List<Vector2>) Ring(float fromY, float toY, int rings, float t0, float t1, bool squash = false)
            {
                var prof = new List<Vector2>();
                var nor = new List<Vector2>();
                for (int i = 0; i <= rings; i++)
                {
                    float t = Mathf.Lerp(t0, t1, i / (float)rings);
                    float a = -Mathf.PI / 2f + t * Mathf.PI;
                    float r = Mathf.Cos(a) * 0.5f, y = Mathf.Sin(a) * 0.5f;
                    if (squash) y = -0.5f + y * 2f; // 반구 (y 0~0.5) → -0.5~0.5
                    prof.Add(new Vector2(r, y));
                    nor.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a) * (squash ? 0.5f : 1f)).normalized);
                }
                return (prof, nor);
            }

            private static Mesh MakeLathe((List<Vector2> prof, List<Vector2> nor) pn, string name, float? capBottom = null)
            {
                var v = new List<Vector3>();
                var n = new List<Vector3>();
                var t = new List<int>();
                int rows = pn.prof.Count;
                for (int r = 0; r < rows; r++)
                    for (int s = 0; s <= Seg; s++)
                    {
                        float a = s / (float)Seg * Mathf.PI * 2f;
                        float c = Mathf.Cos(a), si = Mathf.Sin(a);
                        v.Add(new Vector3(c * pn.prof[r].x, pn.prof[r].y, si * pn.prof[r].x));
                        n.Add(new Vector3(c * pn.nor[r].x, pn.nor[r].y, si * pn.nor[r].x).normalized);
                    }
                for (int r = 0; r < rows - 1; r++)
                    for (int s = 0; s < Seg; s++)
                    {
                        int a = r * (Seg + 1) + s, b2 = a + Seg + 1;
                        t.Add(a); t.Add(b2); t.Add(a + 1);
                        t.Add(a + 1); t.Add(b2); t.Add(b2 + 1);
                    }
                if (capBottom.HasValue) Disc(v, n, t, capBottom.Value, 0.5f, false);
                return Finish(name, v, n, t);
            }

            private static Mesh MakeCylinder(float rBottom, float rTop, string name)
            {
                var v = new List<Vector3>();
                var n = new List<Vector3>();
                var t = new List<int>();
                float slope = rBottom - rTop; // 옆면 법선의 위쪽 몫
                for (int r = 0; r < 2; r++)
                    for (int s = 0; s <= Seg; s++)
                    {
                        float a = s / (float)Seg * Mathf.PI * 2f;
                        float c = Mathf.Cos(a), si = Mathf.Sin(a);
                        float rad = r == 0 ? rBottom : rTop;
                        v.Add(new Vector3(c * rad, r == 0 ? -0.5f : 0.5f, si * rad));
                        n.Add(new Vector3(c, slope, si).normalized);
                    }
                for (int s = 0; s < Seg; s++)
                {
                    int a = s, b2 = s + Seg + 1;
                    t.Add(a); t.Add(b2); t.Add(a + 1);
                    t.Add(a + 1); t.Add(b2); t.Add(b2 + 1);
                }
                Disc(v, n, t, -0.5f, rBottom, false);
                if (rTop > 0f) Disc(v, n, t, 0.5f, rTop, true);
                return Finish(name, v, n, t);
            }

            private static void Disc(List<Vector3> v, List<Vector3> n, List<int> t, float y, float rad, bool up)
            {
                int c0 = v.Count;
                v.Add(new Vector3(0f, y, 0f));
                n.Add(up ? Vector3.up : Vector3.down);
                for (int s = 0; s <= Seg; s++)
                {
                    float a = s / (float)Seg * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad));
                    n.Add(up ? Vector3.up : Vector3.down);
                }
                for (int s = 0; s < Seg; s++)
                {
                    if (up) { t.Add(c0); t.Add(c0 + 2 + s); t.Add(c0 + 1 + s); }
                    else { t.Add(c0); t.Add(c0 + 1 + s); t.Add(c0 + 2 + s); }
                }
            }

            private static Mesh MakeBox()
            {
                var v = new List<Vector3>();
                var n = new List<Vector3>();
                var t = new List<int>();
                var dirs = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                foreach (var d in dirs)
                {
                    var u = d.y != 0f ? Vector3.right : Vector3.up;
                    var w = Vector3.Cross(d, u);
                    int b0 = v.Count;
                    foreach (var (x, y) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                    {
                        v.Add((d + u * x + w * y) * 0.5f);
                        n.Add(d);
                    }
                    t.Add(b0); t.Add(b0 + 1); t.Add(b0 + 2);
                    t.Add(b0); t.Add(b0 + 2); t.Add(b0 + 3);
                }
                return Finish("box", v, n, t);
            }

            private static Mesh Finish(string name, List<Vector3> v, List<Vector3> n, List<int> t)
            {
                // 감는 방향을 법선 쪽이 겉이 되게 맞춤 (Unity = 시계 방향이 앞면)
                for (int i = 0; i < t.Count; i += 3)
                {
                    var a = v[t[i]]; var b = v[t[i + 1]]; var c = v[t[i + 2]];
                    var face = Vector3.Cross(b - a, c - a);
                    var want = n[t[i]] + n[t[i + 1]] + n[t[i + 2]];
                    if (Vector3.Dot(face, want) < 0f) { int k = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = k; }
                }
                var m = new Mesh { name = "prim_" + name };
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
