using System.Collections.Generic;
using Game.Battle.Model.Crowd;
using Game.Battle.Model.Geometry;
using Game.Foundation.Math;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Unity.Battle3D
{
    /// <summary>
    /// 샘플전투2 땅 그리기 (R-133, battle-crowd.md 3.12): 지형 맵을 자연스러운 땅으로.
    /// - 높이: 칸 높이(언덕 1 · 산 2)를 둥글게 번지게 (언덕이 계단이 아니라 둔덕), 강 바닥 · 여울은 파이고, 잔물결 같은 높낮이.
    /// - 색: 칸 색을 칸 가운데끼리 부드럽게 섞고, 경계를 노이즈로 흔들어 (곧은 칸 모양이 안 보이게) 얼룩을 더함.
    /// - 물: 한 판 (물 높이 아래로 파인 땅만 물로 보임 = 강 · 여울 물가가 자연스러움), 깊을수록 진하게.
    /// - 다리: 나무 판 + 난간. 소품: 숲 = 나무, 풀숲 = 풀, 언덕 = 덤불, 산 = 바위, 늪 = 갈대 (art catalog prop.*).
    /// 보이기 전용 (규칙 계산과 무관). 지형이 없으면 풀색 판 하나.
    /// </summary>
    public sealed class CrowdGround
    {
        private const float Margin = 12f;       // 전장 밖으로 더 까는 땅
        private const float LevelHeight = 2.4f; // 높이 1단 = 월드 (캡처 41a: 1.3이면 위에서 보면 언덕이 안 보임)
        private const float RiverBed = -0.55f, FordBed = -0.2f, WaterLevel = -0.1f, DeckTop = 0.22f;
        private const int TexPerUnit = 4;

        private readonly CrowdTerrain _t;
        /// <summary>막는 소품 설정 · 자리 (규칙이 정함, R-134).</summary>
        private readonly IReadOnlyDictionary<string, CrowdObstacleSpec> _obstacleSpecs;
        private readonly IReadOnlyList<CrowdObstacle> _obstacles;
        private readonly float _w, _d;
        private readonly int _vx, _vz;          // 꼭짓점 수
        private readonly float[] _h;            // 꼭짓점 높이 (x = -Margin + i, z = -Margin + j)
        private readonly List<Object> _owned = new List<Object>();
        private readonly List<InstanceBatch> _props = new List<InstanceBatch>();
        private readonly GameObject _root;
        private ArtCatalog _art;

        /// <summary>미니맵 바탕 (전장 1칸 = 1픽셀, 아래 줄 = 우리 쪽). 지형이 없으면 null.</summary>
        public Color32[] MiniColors { get; private set; }
        public int MiniW { get; private set; }
        public int MiniH { get; private set; }

        public CrowdGround(Transform parent, CrowdBattle battle, System.Func<Color, Material> newMaterial)
            : this(parent, battle.Terrain, battle.Rules.FieldWidth, battle.Rules.FieldDepth, battle.Rules.Obstacles, battle.Obstacles, newMaterial) { }

        /// <summary>전투 없이 땅만 (맵 보기, 2026-11-22 사용자): 지형 · 전장 크기 · 막는 소품.</summary>
        public CrowdGround(Transform parent, CrowdTerrain terrain, Fix width, Fix depth, IReadOnlyDictionary<string, CrowdObstacleSpec> obstacleSpecs,
            IReadOnlyList<CrowdObstacle> obstacles, System.Func<Color, Material> newMaterial)
        {
            _t = terrain;
            _obstacleSpecs = obstacleSpecs ?? new Dictionary<string, CrowdObstacleSpec>();
            _obstacles = obstacles ?? new List<CrowdObstacle>();
            _w = (float)width.ToDisplayDouble();
            _d = (float)depth.ToDisplayDouble();
            _root = new GameObject("CrowdGround");
            _root.transform.SetParent(parent, false);
            _vx = Mathf.CeilToInt(_w + Margin * 2) + 1;
            _vz = Mathf.CeilToInt(_d + Margin * 2) + 1;
            _h = new float[_vx * _vz];
            if (_t != null) BuildHeights();

            var ground = new GameObject("Ground");
            ground.transform.SetParent(_root.transform, false);
            ground.AddComponent<MeshFilter>().sharedMesh = Own(GridMesh("crowd_ground", _h));
            var gm = Own(newMaterial(Color.white));
            if (_t != null) gm.mainTexture = Own(GroundTexture());
            else gm.color = PlainColor;
            if (gm.HasProperty("_BaseColor")) gm.SetColor("_BaseColor", _t != null ? Color.white : PlainColor);
            if (gm.HasProperty("_Smoothness")) gm.SetFloat("_Smoothness", 0.05f);
            ground.AddComponent<MeshRenderer>().sharedMaterial = gm;
            if (_t == null) return;

            BuildWater(newMaterial);
            BuildBridges(newMaterial);
            BuildProps(newMaterial);
            BuildMini();
        }

        // ---- 높이 ----

        private static readonly Color PlainColor = new Color(0.45f, 0.58f, 0.3f);

        /// <summary>칸 높이 (언덕 · 산), 물 바닥 깊이.</summary>
        private float LevelAt(float x, float z) => _t.HeightAt(Field(x, z)) * LevelHeight;

        private float BedAt(float x, float z)
        {
            switch (_t.TerrainAt(Field(x, z)))
            {
                case "river": case "bridge": case "water": case "flood": return RiverBed;
                case "ford": return FordBed;
                case "marsh": return -0.14f; // 늪: 땅결 노이즈로 군데군데 물웅덩이가 생기게 (캡처 41d: 어두운 풀밭으로만 보임)
                default: return 0f;
            }
        }

        /// <summary>해 쪽 (CrowdField3D의 해 Euler(55, −30)와 같은 방향).</summary>
        private static readonly Vector3 ToSun = -(Quaternion.Euler(55f, -30f, 0f) * Vector3.forward);

        private static Vec Field(float x, float z) =>
            new Vec(Fix.FromRaw((long)(Mathf.Clamp(x, 0.01f, 1e5f) * Fix.One)), Fix.FromRaw((long)(Mathf.Clamp(z, 0.01f, 1e5f) * Fix.One)));

        private void BuildHeights()
        {
            // 높이는 반경 3.5칸으로 둥글게, 물 바닥은 반경 1.2칸으로 (물가는 조금 가파르게)
            for (int j = 0; j < _vz; j++)
                for (int i = 0; i < _vx; i++)
                {
                    float x = -Margin + i, z = -Margin + j;
                    float hs = 0f, hw = 0f, bs = 0f, bw = 0f;
                    for (int dz = -4; dz <= 4; dz++)
                        for (int dx = -4; dx <= 4; dx++)
                        {
                            float r = Mathf.Sqrt(dx * dx + dz * dz);
                            float sx = x + dx * 0.9f, sz = z + dz * 0.9f;
                            if (r <= 4f) { float k = Mathf.Cos(r / 4f * Mathf.PI * 0.5f); k *= k; hs += LevelAt(sx, sz) * k; hw += k; }
                            if (r <= 1.5f) { float k = 1.6f - r; bs += BedAt(sx, sz) * k; bw += k; }
                        }
                    float h = hs / hw + bs / bw;
                    // 땅 결: 언덕 · 산일수록 더 울퉁불퉁 (물 위로 솟지 않게 평지는 0 이상)
                    float rough = 0.06f + 0.18f * Mathf.Clamp01(hs / hw / LevelHeight);
                    h += rough * (Mathf.PerlinNoise(x * 0.11f + 3.1f, z * 0.11f + 7.7f) - 0.3f) + 0.03f * Mathf.PerlinNoise(x * 0.5f, z * 0.5f);
                    _h[j * _vx + i] = h;
                }
        }

        /// <summary>그 자리 땅 높이 (꼭짓점 쌍선형).</summary>
        public float GroundHeight(float x, float z)
        {
            float fx = Mathf.Clamp(x + Margin, 0f, _vx - 1.001f), fz = Mathf.Clamp(z + Margin, 0f, _vz - 1.001f);
            int i = (int)fx, j = (int)fz;
            float u = fx - i, v = fz - j;
            float a = _h[j * _vx + i], b = _h[j * _vx + i + 1], c = _h[(j + 1) * _vx + i], d = _h[(j + 1) * _vx + i + 1];
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        /// <summary>병사가 서는 높이: 다리 위면 다리 판, 아니면 땅 (여울은 물에 발이 잠김).</summary>
        public float StandHeight(float x, float z)
        {
            if (_t != null && x >= 0f && z >= 0f && x <= _w && z <= _d && _t.TerrainAt(Field(x, z)) == "bridge") return DeckTop;
            return GroundHeight(x, z);
        }

        private Mesh GridMesh(string name, float[] heights)
        {
            var verts = new Vector3[_vx * _vz];
            var uv = new Vector2[verts.Length];
            for (int j = 0; j < _vz; j++)
                for (int i = 0; i < _vx; i++)
                {
                    int k = j * _vx + i;
                    float x = -Margin + i, z = -Margin + j;
                    verts[k] = new Vector3(x, heights[k], z);
                    uv[k] = new Vector2((x + Margin) / (_vx - 1), (z + Margin) / (_vz - 1));
                }
            var tris = new int[(_vx - 1) * (_vz - 1) * 6];
            int t = 0;
            for (int j = 0; j < _vz - 1; j++)
                for (int i = 0; i < _vx - 1; i++)
                {
                    int a = j * _vx + i, b = a + _vx;
                    tris[t++] = a; tris[t++] = b; tris[t++] = b + 1;
                    tris[t++] = a; tris[t++] = b + 1; tris[t++] = a + 1;
                }
            var mesh = new Mesh { name = name };
            if (verts.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---- 색 ----

        /// <summary>자연스러운 땅 색 (보이기 전용).</summary>
        private static Color TerrainColor(string id)
        {
            switch (id)
            {
                case "grass": return new Color(0.50f, 0.60f, 0.28f);
                case "road": return new Color(0.63f, 0.54f, 0.38f);
                case "forest": return new Color(0.26f, 0.37f, 0.19f);
                case "burnt": return new Color(0.22f, 0.19f, 0.16f);
                case "hill": return new Color(0.52f, 0.55f, 0.31f);
                case "mountain": return new Color(0.50f, 0.47f, 0.42f);
                case "cliff": return new Color(0.42f, 0.39f, 0.35f);
                case "pass": return new Color(0.58f, 0.52f, 0.40f);
                case "river": case "bridge": case "water": case "flood": return new Color(0.36f, 0.33f, 0.26f);
                case "ford": return new Color(0.56f, 0.51f, 0.40f);
                case "marsh": return new Color(0.38f, 0.37f, 0.24f);
                case "mud": return new Color(0.42f, 0.35f, 0.25f);
                default: return PlainColor;
            }
        }

        /// <summary>
        /// 땅 텍스처 (전장 1칸 = TexPerUnit 픽셀): 자리를 노이즈로 조금 흔든 뒤 맵 칸 가운데끼리 섞은 색 × 얼룩.
        /// </summary>
        private Texture2D GroundTexture()
        {
            int tw = (_vx - 1) * TexPerUnit, th = (_vz - 1) * TexPerUnit;
            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            var px = new Color32[tw * th];
            float cell = (float)_t.CellSize.ToDisplayDouble();
            int mw = _t.Map.Width, mh = _t.Map.Height;
            var cellCol = new Color[mw * mh];
            for (int r = 0; r < mh; r++)
                for (int c = 0; c < mw; c++)
                    cellCol[r * mw + c] = TerrainColor(_t.Map.TerrainAt(c, r));
            Color At(int c, int up)
            {
                c = Mathf.Clamp(c, 0, mw - 1);
                up = Mathf.Clamp(up, 0, mh - 1);
                return cellCol[(mh - 1 - up) * mw + c];
            }
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                {
                    float wx = -Margin + (x + 0.5f) / TexPerUnit, wz = -Margin + (y + 0.5f) / TexPerUnit;
                    // 경계 흔들기 (칸 모양이 보이지 않게)
                    float ox = (Mathf.PerlinNoise(wx * 0.35f, wz * 0.35f) - 0.5f) * 2.2f + (Mathf.PerlinNoise(wx * 1.3f + 9f, wz * 1.3f) - 0.5f) * 0.6f;
                    float oz = (Mathf.PerlinNoise(wx * 0.35f + 31f, wz * 0.35f + 17f) - 0.5f) * 2.2f + (Mathf.PerlinNoise(wx * 1.3f, wz * 1.3f + 5f) - 0.5f) * 0.6f;
                    float gx = (wx + ox) / cell - 0.5f, gz = (wz + oz) / cell - 0.5f;
                    int c0 = Mathf.FloorToInt(gx), u0 = Mathf.FloorToInt(gz);
                    float fx = Mathf.SmoothStep(0f, 1f, gx - c0), fz = Mathf.SmoothStep(0f, 1f, gz - u0);
                    // 섞는 폭을 좁혀 (가운데는 그 칸 색) 경계만 부드럽게
                    fx = Mathf.Clamp01((fx - 0.5f) * 1.8f + 0.5f);
                    fz = Mathf.Clamp01((fz - 0.5f) * 1.8f + 0.5f);
                    var col = Color.Lerp(Color.Lerp(At(c0, u0), At(c0 + 1, u0), fx), Color.Lerp(At(c0, u0 + 1), At(c0 + 1, u0 + 1), fx), fz);
                    // 얼룩: 큰 얼룩 + 잔 얼룩, 높은 곳은 조금 마르고 밝게
                    float big = Mathf.PerlinNoise(wx * 0.08f + 50f, wz * 0.08f + 20f);
                    float small = Mathf.PerlinNoise(wx * 0.9f + 3f, wz * 0.9f + 11f);
                    float fine = Mathf.PerlinNoise(wx * 3.1f + 7f, wz * 3.1f + 2f); // 풀 결 (가까이서 흐릿하지 않게)
                    float shade = 0.84f + 0.16f * big + 0.1f * small + 0.1f * (fine - 0.5f);
                    float hgt = GroundHeight(wx, wz);
                    // 비탈: 가파를수록 흙 · 돌빛 (산 · 언덕 옆구리)
                    float gx2 = GroundHeight(wx + 0.5f, wz) - GroundHeight(wx - 0.5f, wz), gz2 = GroundHeight(wx, wz + 0.5f) - GroundHeight(wx, wz - 0.5f);
                    var n = new Vector3(-gx2, 1f, -gz2).normalized;
                    float steep = 1f - n.y;
                    col = Color.Lerp(col, new Color(0.55f, 0.50f, 0.40f), Mathf.Clamp01(steep * 3.5f) * 0.6f);
                    col = Color.Lerp(col, new Color(0.64f, 0.60f, 0.46f), Mathf.Clamp01(hgt / (LevelHeight * 2.5f)) * 0.3f);
                    // 산그늘 (햇빛 쪽 밝게 · 반대쪽 어둡게): 위에서 내려다봐도 높낮이가 보이게
                    float lit = Vector3.Dot(n, ToSun);
                    shade *= Mathf.Clamp(0.55f + 0.55f * lit / ToSun.y, 0.6f, 1.25f);
                    col *= shade;
                    col.a = 1f;
                    px[y * tw + x] = col;
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // ---- 물 · 다리 ----

        private void BuildWater(System.Func<Color, Material> newMaterial)
        {
            bool any = false;
            for (int k = 0; k < _h.Length && !any; k++) if (_h[k] < WaterLevel) any = true;
            if (!any) return;
            // 물 한 판: 물 높이 아래로 파인 땅만 보인다. 색은 깊이로 (얕은 여울 = 밝은 청록, 깊은 강 = 진한 청)
            var flat = new float[_h.Length];
            for (int k = 0; k < flat.Length; k++) flat[k] = WaterLevel;
            var go = new GameObject("Water");
            go.transform.SetParent(_root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Own(GridMesh("crowd_water", flat));
            int tw = _vx - 1, th = _vz - 1;
            var tex = new Texture2D(tw * 2, th * 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[tex.width * tex.height];
            var shallow = new Color(0.42f, 0.58f, 0.60f);
            var deep = new Color(0.16f, 0.30f, 0.42f);
            for (int y = 0; y < tex.height; y++)
                for (int x = 0; x < tex.width; x++)
                {
                    float wx = -Margin + (x + 0.5f) / 2f, wz = -Margin + (y + 0.5f) / 2f;
                    float depth = Mathf.Clamp01((WaterLevel - GroundHeight(wx, wz)) / 0.4f);
                    var c = Color.Lerp(shallow, deep, depth) * (0.94f + 0.1f * Mathf.PerlinNoise(wx * 0.6f, wz * 0.25f));
                    c.a = 1f;
                    px[y * tex.width + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply();
            var m = Own(newMaterial(Color.white));
            m.mainTexture = Own(tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.85f);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        private void BuildBridges(System.Func<Color, Material> newMaterial)
        {
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var plank = Own(newMaterial(new Color(0.48f, 0.34f, 0.2f)));
            var rail = Own(newMaterial(new Color(0.36f, 0.25f, 0.15f)));
            float cell = (float)_t.CellSize.ToDisplayDouble();
            int mw = _t.Map.Width, mh = _t.Map.Height;
            bool Bridge(int c, int r) => c >= 0 && r >= 0 && c < mw && r < mh && _t.Map.TerrainAt(c, r) == "bridge";
            for (int r = 0; r < mh; r++)
                for (int c = 0; c < mw; c++)
                {
                    if (!Bridge(c, r)) continue;
                    float x0 = c * cell, z0 = (mh - 1 - r) * cell;
                    var center = new Vector3(x0 + cell / 2f, DeckTop - 0.08f, z0 + cell / 2f);
                    Part(cube, plank, center, new Vector3(cell + 0.02f, 0.16f, cell + 0.4f)); // 판 (강가로 조금 더)
                    // 판 사이 줄눈 (가로 판자 느낌)
                    for (int k = 1; k < 5; k++) Part(cube, rail, new Vector3(center.x, DeckTop + 0.005f, z0 + cell * k / 5f), new Vector3(cell, 0.01f, 0.05f));
                    if (!Bridge(c - 1, r)) Rail(cube, rail, x0 + 0.08f, z0, cell);
                    if (!Bridge(c + 1, r)) Rail(cube, rail, x0 + cell - 0.08f, z0, cell);
                }
        }

        private void Rail(Mesh cube, Material m, float x, float z0, float len)
        {
            Part(cube, m, new Vector3(x, DeckTop + 0.35f, z0 + len / 2f), new Vector3(0.1f, 0.08f, len + 0.4f));
            for (int k = 0; k <= 2; k++) Part(cube, m, new Vector3(x, DeckTop + 0.17f, z0 + len * k / 2f), new Vector3(0.1f, 0.36f, 0.1f));
        }

        private void Part(Mesh mesh, Material m, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject("Bridge");
            go.transform.SetParent(_root.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
        }

        // ---- 소품 ----

        /// <summary>지형마다 소품: 카탈로그 키 · 맵 칸 하나에 몇 개 · 크기 배수.</summary>
        private static (string key, float perCell, float scale)? PropOf(string terrain)
        {
            switch (terrain)
            {
                // 캡처 41c: 숲이 빽빽하고 나무가 커서 병사가 안 보임 → 작고 성기게. 41e: 산 바위가 블록 더미 → 바위는 드문드문, 산은 높이로
                case "forest": return ("prop.forest", 2.2f, 1.7f);
                case "grass": return ("prop.grass", 6f, 1.5f);
                case "plain": return ("prop.grass", 0.7f, 0.9f);
                case "hill": return ("prop.hill", 1.0f, 1.5f);
                case "mountain": return ("prop.mountain", 0.8f, 1.5f);
                case "marsh": return ("prop.marsh", 7f, 1.6f);
                case "burnt": return ("prop.burnt", 1.5f, 1.7f);
                default: return null;
            }
        }

        private void BuildProps(System.Func<Color, Material> newMaterial)
        {
            var art = _art = new ArtCatalog(newMaterial);
            var kinds = new Dictionary<string, (List<InstanceBatch> batches, float perCell, float scale)>();
            float cell = (float)_t.CellSize.ToDisplayDouble();
            int mw = _t.Map.Width, mh = _t.Map.Height;
            for (int r = 0; r < mh; r++)
                for (int c = 0; c < mw; c++)
                {
                    string id = _t.Map.TerrainAt(c, r);
                    if (!kinds.TryGetValue(id, out var kind))
                    {
                        var p = PropOf(id);
                        var list = new List<InstanceBatch>();
                        if (p != null && art.TryGet(p.Value.key, out var models, out _, out _))
                            foreach (var m in models) list.Add(new InstanceBatch(m.Mesh, m.Materials, m.Local));
                        _props.AddRange(list);
                        kinds[id] = kind = (list, p?.perCell ?? 0f, p?.scale ?? 1f);
                    }
                    if (kind.batches.Count == 0) continue;
                    if (_obstacleSpecs.ContainsKey(id)) continue; // 막는 소품(나무 · 바위)은 규칙이 정한 자리에 (아래, R-134)
                    int i = r * mw + c;
                    int count = (int)kind.perCell + (Rand(i, 99) < kind.perCell - (int)kind.perCell ? 1 : 0);
                    for (int k = 0; k < count; k++)
                    {
                        // 칸 안 흩뿌림 (칸 경계를 조금 넘어 섞이게), 같은 칸은 늘 같은 자리
                        float x = (c - 0.15f + 1.3f * Rand(i, k * 4)) * cell, z = (mh - 1 - r - 0.15f + 1.3f * Rand(i, k * 4 + 1)) * cell;
                        var batch = kind.batches[(int)(Rand(i, k * 4 + 2) * kind.batches.Count) % kind.batches.Count];
                        float s = kind.scale * (0.75f + 0.5f * Rand(i, k * 4 + 3));
                        batch.Add(new Vector3(x, GroundHeight(x, z) - 0.05f, z), Quaternion.Euler(0f, Rand(i, k * 4 + 5) * 360f, 0f), s);
                    }
                }
            PlaceObstacles(kinds);
        }

        /// <summary>막는 소품 (R-134): 규칙의 나무 기둥 · 바위 자리 그대로 그린다 (병사가 비켜 가는 자리와 그림이 같음).</summary>
        private void PlaceObstacles(Dictionary<string, (List<InstanceBatch> batches, float perCell, float scale)> kinds)
        {
            foreach (var o in _obstacles)
            {
                if (!kinds.TryGetValue(o.Terrain, out var kind) || kind.batches.Count == 0) continue;
                float x = (float)o.Pos.X.ToDisplayDouble(), z = (float)o.Pos.Y.ToDisplayDouble();
                var batch = kind.batches[o.Variant % kind.batches.Count];
                batch.Add(new Vector3(x, GroundHeight(x, z) - 0.05f, z), Quaternion.Euler(0f, o.YawDeg, 0f), kind.scale * (float)o.Size.ToDisplayDouble());
            }
        }

        private static float Rand(int cell, int salt)
        {
            uint v = (uint)cell * 2654435761u ^ (uint)(salt + 1) * 40503u;
            v ^= v >> 13; v *= 0x5bd1e995u; v ^= v >> 15;
            return (v & 0xFFFFFF) / (float)0x1000000;
        }

        // ---- 미니맵 ----

        private void BuildMini()
        {
            MiniW = Mathf.CeilToInt(_w);
            MiniH = Mathf.CeilToInt(_d);
            MiniColors = new Color32[MiniW * MiniH];
            var water = new Color(0.25f, 0.42f, 0.55f);
            for (int y = 0; y < MiniH; y++)
                for (int x = 0; x < MiniW; x++)
                {
                    float wx = x + 0.5f, wz = y + 0.5f;
                    var c = GroundHeight(wx, wz) < WaterLevel ? water : TerrainColor(_t.TerrainAt(Field(wx, wz))) * 0.85f;
                    c.a = 1f;
                    MiniColors[y * MiniW + x] = c;
                }
        }

        public void Draw(Camera cam)
        {
            foreach (var b in _props) b.Draw(cam);
        }

        public void Destroy()
        {
            Object.Destroy(_root);
            foreach (var o in _owned) if (o != null) Object.Destroy(o);
            _art?.Destroy();
        }

        private T Own<T>(T o) where T : Object { _owned.Add(o); return o; }
    }
}
