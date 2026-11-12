using System.Collections.Generic;
using Game.Battle.Model.Control;
using Game.Battle.Model.Display;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Foundation.Math;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Unity.Battle3D
{
    /// <summary>
    /// 3D 전장 (단순 도형): 높이가 있는 지형 바닥 + 안개, 군단 깃발(기수), 병사(GPU 인스턴싱, 군단 방향으로 정렬), 선택 링,
    /// 이동 경로선·목적지, 스킬 사거리 원, 안 보이게 된 적의 마지막 목격 위치(흐린 깃발).
    /// 전투 상태는 읽기만 한다. 좌표: 전장 (x, y) 칸 → 월드 (x, 지형 높이, 깊이 − y).
    /// 임시 아트: 아트 목록(ArtCatalog)에 병종·지형 모델이 있으면 그것을, 없으면 단순 도형을 쓴다 (docs/impl/unity/art.md).
    /// </summary>
    public sealed class BattleField3D
    {
        private const float SoldierSpacing = 0.32f;
        /// <summary>지형 높이 1단계의 월드 높이.</summary>
        private const float HeightScale = 0.8f;
        private const int RangeSegments = 48;
        private static readonly Vector3 SoldierScale = new Vector3(0.18f, 0.36f, 0.18f);
        private static readonly Color AllyColor = new Color(0.30f, 0.50f, 0.95f);
        private static readonly Color EnemyColor = new Color(0.90f, 0.30f, 0.25f);
        private static readonly Color SelectedColor = new Color(1f, 0.85f, 0.25f);
        private static readonly Color ConfusedColor = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color LastSeenColor = new Color(0.55f, 0.35f, 0.35f);
        private static readonly Color PathColor = new Color(1f, 1f, 1f);
        private static readonly Color RangeColor = new Color(0.95f, 0.75f, 0.30f);
        private static readonly Color HitColor = new Color(1f, 0.95f, 0.85f);

        private readonly BattleControl _ctl;
        private readonly Light _sun;
        private readonly Camera _camera;
        private string _atmosphereKey;
        private readonly GameObject _root;
        private readonly FieldTexture _texture;
        private readonly long _troopsPerSoldier;
        private readonly float _depth;
        private readonly Mesh _cube;
        private readonly List<Object> _owned = new List<Object>();
        private const int Ally = 0, Enemy = 1, Selected = 2, Confused = 3, Hit = 4, AllyShaken = 5, EnemyShaken = 6, HiddenTone = 7;
        // 흔들리는 군단(사기 바닥 가까이)은 바랜 팀 색, 숨은 아군(복병 준비·은신)은 어두운 색 (D18)
        private static readonly Color[] SoldierColors = { AllyColor, EnemyColor, SelectedColor, ConfusedColor, HitColor,
            new Color(0.55f, 0.62f, 0.80f), new Color(0.78f, 0.55f, 0.52f), new Color(0.22f, 0.28f, 0.22f) };
        // 버프·디버프 테두리 (D18): 군단 발밑 원, 버프 = 금색, 디버프(혼란·도발) = 보라
        private readonly Dictionary<CorpsState, LineRenderer> _statusRings = new Dictionary<CorpsState, LineRenderer>();
        private static readonly Color BuffColor = new Color(1f, 0.82f, 0.30f);
        private static readonly Color DebuffColor = new Color(0.70f, 0.40f, 0.95f);
        private readonly ArtCatalog _art;
        /// <summary>(병종, 색) → 병사 묶음. 모델이 없는 병종은 색별 상자 묶음을 같이 쓴다.</summary>
        private readonly Dictionary<(string troop, int color), InstanceBatch> _soldierBatches = new Dictionary<(string, int), InstanceBatch>();
        private readonly InstanceBatch[] _cubeBatches = new InstanceBatch[SoldierColors.Length];
        private readonly List<InstanceBatch> _allSoldierBatches = new List<InstanceBatch>();
        /// <summary>지형 id → 소품 묶음(변형별) — 모델이 없으면 null.</summary>
        private readonly Dictionary<string, (List<InstanceBatch> variants, float perCell, float jitter)?> _propKinds = new Dictionary<string, (List<InstanceBatch>, float, float)?>();
        private readonly List<InstanceBatch> _allPropBatches = new List<InstanceBatch>();
        private string[] _propTerrain;
        private readonly Dictionary<CorpsState, Transform> _flags = new Dictionary<CorpsState, Transform>();
        private readonly Dictionary<CorpsState, Transform> _lastSeen = new Dictionary<CorpsState, Transform>();
        private readonly float[] _vertexHeights;
        private readonly LineRenderer _path;
        private readonly LineRenderer _range;
        // 스킬 자리 미리보기 (4.2): 효과 범위 테두리, 사거리 안이면 밝은 색, 밖이면 붉은 색 (다가가서 쓴다)
        // 맵 오브젝트 (R-78): 테두리 + 기둥, 차지한 팀 색 (아무도 없으면 회색)
        private readonly List<(Game.Battle.Model.Victory.MapObjectState state, LineRenderer ring, MeshRenderer pillar)> _objects = new List<(Game.Battle.Model.Victory.MapObjectState, LineRenderer, MeshRenderer)>();
        private Material _objAlly, _objEnemy, _objNone;
        private readonly LineRenderer _aimIn;
        private readonly LineRenderer _aimOut;
        private static readonly Color AimInColor = new Color(1f, 0.92f, 0.55f);
        private static readonly Color AimOutColor = new Color(0.9f, 0.35f, 0.25f);
        private readonly List<Vector3> _pathPoints = new List<Vector3>();
        private readonly List<Transform> _waypoints = new List<Transform>();
        private Mesh _cylinder;
        // 계략 표식 (R-80): 내 함정 = 땅의 갈색 고리, 위장 깃발 = 깃발 (내 것은 흐리게, 적 것은 보이면 진짜처럼)
        private readonly Dictionary<object, Transform> _stratagemMarks = new Dictionary<object, Transform>();
        private Material _trapMat, _decoyOwnMat, _decoyPoleMat, _enemyFlagMat;
        private Material _waypointMat;
        /// <summary>소부대 위치: 직전 틱 → 지금 틱 (틱 사이를 부드럽게 잇는다).</summary>
        private readonly Dictionary<SubUnitState, (Vector3 prev, Vector3 cur)> _unitPos = new Dictionary<SubUnitState, (Vector3, Vector3)>();
        private readonly Dictionary<Color, Material> _lineMats = new Dictionary<Color, Material>();
        private int _flashFrame;
        private readonly Transform _ring;
        /// <summary>선택 표시: 꽉 찬 원판 대신 테두리만 (병사가 가려지지 않게).</summary>
        private readonly LineRenderer _ringLine;
        private const int RingSegments = 40;
        private const float RingRadius = 1.8f;
        private readonly Transform _target;

        public BattleCameraRig Rig { get; }
        public int Width { get; }
        public int Height { get; }
        public Texture2D MapTexture => _texture.Texture;
        public BattleEffects3D Effects { get; }

        public BattleField3D(BattleControl ctl)
        {
            _ctl = ctl;
            var ctx = ctl.Context;
            Width = ctx.Field.Width;
            Height = ctx.Field.Height;
            _depth = Height;
            _root = new GameObject("BattleField3D");

            long total = 0;
            foreach (var c in ctx.Corps) total += c.StartTroops;
            _troopsPerSoldier = SoldierRatio.TroopsPerSoldier(System.Math.Max(1, total), ctx.Balance.SoldierBudget);

            // 카메라·빛
            var camGo = new GameObject("BattleCamera");
            camGo.transform.SetParent(_root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.10f);
            cam.depth = 1;
            cam.cullingMask = ~(1 << 5); // UI 층 제외
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 300f;
            cam.fieldOfView = 40f;
            Rig = new BattleCameraRig(cam, Width, _depth);

            var lightGo = new GameObject("Sun");
            lightGo.transform.SetParent(_root.transform, false);
            lightGo.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            _sun = light;
            _camera = cam;

            // 바닥
            _texture = new FieldTexture(ctx, ctl.Team) { RevealAll = ctl.RevealAll };
            _texture.Refresh();
            var ground = new GameObject("Ground");
            ground.transform.SetParent(_root.transform, false);
            _vertexHeights = VertexHeights(ctx);
            ground.AddComponent<MeshFilter>().sharedMesh = Own(GroundMesh(Width, Height, _vertexHeights));
            var groundMat = Own(NewMaterial(Color.white));
            groundMat.mainTexture = _texture.Texture;
            ground.AddComponent<MeshRenderer>().sharedMaterial = groundMat;

            _cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            _art = new ArtCatalog(NewMaterial);
            RefreshProps();
            var cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");

            // 깃발 (군단마다 기수 하나)
            var allyFlag = Own(NewMaterial(AllyColor));
            var enemyFlag = Own(NewMaterial(EnemyColor));
            _enemyFlagMat = enemyFlag;
            _trapMat = Own(NewMaterial(new Color(0.45f, 0.3f, 0.18f)));
            _decoyOwnMat = Own(NewMaterial(new Color(AllyColor.r, AllyColor.g, AllyColor.b, 0.45f)));
            _decoyPoleMat = Own(NewMaterial(new Color(0.35f, 0.3f, 0.25f)));
            var poleMat = Own(NewMaterial(new Color(0.35f, 0.25f, 0.15f)));
            var lastSeenMat = Own(NewMaterial(LastSeenColor));
            foreach (var c in ctx.Corps)
            {
                _flags[c] = Flag("Flag " + c.Id, cylinder, poleMat, c.Team == ctl.Team ? allyFlag : enemyFlag);
                if (c.Team != ctl.Team) _lastSeen[c] = Flag("LastSeen " + c.Id, cylinder, lastSeenMat, lastSeenMat);
            }
            _path = Line("Path", PathColor, 0.12f, false);
            _range = Line("Range", RangeColor, 0.12f, true);
            _aimIn = Line("AimIn", AimInColor, 0.16f, true);
            _aimOut = Line("AimOut", AimOutColor, 0.16f, true);

            _ring = Part(_root.transform, cylinder, Own(NewMaterial(SelectedColor)), Vector3.zero, new Vector3(3.2f, 0.01f, 3.2f));
            _ring.gameObject.SetActive(false); // 원판은 쓰지 않는다 (테두리 _ringLine)
            _ringLine = Line("SelectRing", SelectedColor, 0.12f, true);
            _ringLine.positionCount = RingSegments;
            _cylinder = cylinder;
            _waypointMat = Own(NewMaterial(new Color(0.75f, 0.85f, 1f)));
            _objAlly = Own(NewMaterial(AllyColor));
            _objEnemy = Own(NewMaterial(EnemyColor));
            _objNone = Own(NewMaterial(new Color(0.75f, 0.72f, 0.65f)));
            foreach (var o in ctx.Objects)
            {
                var ring = Line("Object " + o.Setup.Id, new Color(0.75f, 0.72f, 0.65f), 0.14f, true);
                var center = ToWorld(o.Center);
                float r = (float)o.Setup.Radius.ToDisplayDouble();
                ring.positionCount = RangeSegments;
                for (int i = 0; i < RangeSegments; i++)
                {
                    float a = i * Mathf.PI * 2f / RangeSegments;
                    float x = center.x + Mathf.Cos(a) * r, z = center.z + Mathf.Sin(a) * r;
                    ring.SetPosition(i, new Vector3(x, GroundHeight(x, z) + 0.12f, z));
                }
                // 기둥 높이로 종류 구분: 망루 높게, 보급소 낮고 넓게, 점령 구역 깃대
                var size = o.Setup.Type == Game.Battle.Model.Victory.MapObjectSetup.Watchtower ? new Vector3(0.6f, 2.2f, 0.6f)
                    : o.Setup.Type == Game.Battle.Model.Victory.MapObjectSetup.Depot ? new Vector3(1.2f, 0.4f, 1.2f) : new Vector3(0.2f, 1.6f, 0.2f);
                var pillar = Part(_root.transform, cylinder, _objNone, center + new Vector3(0f, size.y, 0f), size);
                _objects.Add((o, ring, pillar.GetComponent<MeshRenderer>()));
            }
            _target = Part(_root.transform, cylinder, Own(NewMaterial(Color.white)), Vector3.zero, new Vector3(0.5f, 0.03f, 0.5f));

            Effects = new BattleEffects3D(this, ctl, _cube, col => Own(NewMaterial(col)), col =>
            {
                var l = Line("Fx", col, 0.2f, true);
                return l;
            });
            OnTicked();
        }

        /// <summary>병종·색의 병사 묶음: 아트 목록의 soldier.&lt;병종&gt; → soldier.default → 상자.</summary>
        private InstanceBatch SoldierBatch(string troop, int color)
        {
            if (_soldierBatches.TryGetValue((troop, color), out var b)) return b;
            // 그 병종 모델이 없으면 부모 병종(T3 → T2 → T1) 모델, 그래도 없으면 기본 병사
            string key = "soldier." + troop;
            ArtModel model;
            var types = _ctl.Context.Defs.TroopTypes;
            string t = troop;
            while (!_art.TryGet(key, out model) && t != null && types.TryGetValue(t, out var def) && def.Parent != null)
            {
                t = def.Parent;
                key = "soldier." + t;
            }
            if (model == null) { key = "soldier.default"; _art.TryGet(key, out model); }
            if (model != null)
            {
                b = new InstanceBatch(model.Mesh, _art.Tinted(model, SoldierColors[color], _art.TintOf(key)), model.Local);
                _allSoldierBatches.Add(b);
            }
            else
            {
                // 모델이 없으면 코드로 만든 병종 인형 (SoldierMeshes): 1단계 병종 모양
                string baseType = troop;
                while (baseType != null && types.TryGetValue(baseType, out var d) && d.Parent != null) baseType = d.Parent;
                var mat = Own(NewMaterial(SoldierColors[color]));
                mat.enableInstancing = true;
                // 부위별 재질: 갑옷(팀 색) · 얼굴 · 무기 · 다리·말
                b = new InstanceBatch(SoldierMeshes.For(baseType), new[] { mat, PartMaterial(1, SoldierMeshes.SkinColor), PartMaterial(2, SoldierMeshes.MetalColor), PartMaterial(3, SoldierMeshes.DarkColor) }, Matrix4x4.identity);
                _allSoldierBatches.Add(b);
            }
            return _soldierBatches[(troop, color)] = b;
        }

        /// <summary>장수 묶음: (총대장 여부, 색) → 인형 (SoldierMeshes.General).</summary>
        private readonly Dictionary<(bool commander, int color), InstanceBatch> _generalBatches = new Dictionary<(bool, int), InstanceBatch>();
        private InstanceBatch GeneralBatch(bool commander, int color)
        {
            if (_generalBatches.TryGetValue((commander, color), out var b)) return b;
            var team = Own(NewMaterial(SoldierColors[color]));
            team.enableInstancing = true;
            b = new InstanceBatch(SoldierMeshes.General(commander),
                new[] { team, PartMaterial(1, SoldierMeshes.SkinColor), PartMaterial(4, SoldierMeshes.GoldColor), PartMaterial(5, SoldierMeshes.GeneralHorseColor) }, Matrix4x4.identity);
            _allSoldierBatches.Add(b);
            return _generalBatches[(commander, color)] = b;
        }

        private readonly Material[] _partMaterials = new Material[6];
        private Material PartMaterial(int i, Color c)
        {
            if (_partMaterials[i] != null) return _partMaterials[i];
            var m = Own(NewMaterial(c));
            m.enableInstancing = true;
            return _partMaterials[i] = m;
        }

        /// <summary>쓰러진 병사 (병력이 줄어 화면 병사가 빠진 자리에 남는다, 전투가 끝날 때까지). 최대 개수까지만.</summary>
        private const int FallenMax = 4000;
        private readonly Dictionary<Game.Battle.Model.Simulation.SubUnitState, long> _shownSoldiers = new Dictionary<Game.Battle.Model.Simulation.SubUnitState, long>();
        private InstanceBatch _fallen;
        private InstanceBatch Fallen
        {
            get
            {
                if (_fallen != null) return _fallen;
                var m = Own(NewMaterial(new Color(0.18f, 0.16f, 0.14f)));
                m.enableInstancing = true;
                return _fallen = new InstanceBatch(_cube, new[] { m }, Matrix4x4.TRS(new Vector3(0f, SoldierMeshes.FallenScale.y / 2f, 0f), Quaternion.identity, SoldierMeshes.FallenScale));
            }
        }

        private void AddFallen(Vector3 p, int seed)
        {
            if (Fallen.Matrices.Count >= FallenMax) return;
            p.y = GroundHeight(p.x, p.z);
            Fallen.Add(p, Quaternion.Euler(0f, (seed * 73) % 360, 0f));
        }

        private InstanceBatch CubeBatch(int color)
        {
            if (_cubeBatches[color] != null) return _cubeBatches[color];
            var m = Own(NewMaterial(SoldierColors[color]));
            m.enableInstancing = true;
            var b = new InstanceBatch(_cube, new[] { m }, Matrix4x4.TRS(new Vector3(0f, SoldierScale.y / 2f, 0f), Quaternion.identity, SoldierScale));
            _allSoldierBatches.Add(b);
            return _cubeBatches[color] = b;
        }

        /// <summary>지형 소품 (prop.&lt;지형 id&gt;): 칸마다 정해진 자리·방향·크기 (칸 번호로 정하므로 늘 같다). 지형이 바뀌면(불탐) 다시 놓는다.</summary>
        private void RefreshProps()
        {
            var f = _ctl.Context.Field;
            int w = f.Width, h = f.Height;
            if (_propTerrain == null) _propTerrain = new string[w * h];
            bool changed = false;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var t = f.TerrainAt(new Cell(x, y));
                    if (_propTerrain[y * w + x] != t) { _propTerrain[y * w + x] = t; changed = true; }
                }
            if (!changed) return;
            foreach (var b in _allPropBatches) b.Matrices.Clear();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    var kind = PropKind(_propTerrain[i]);
                    if (kind == null) continue;
                    var (variants, perCell, jitter) = kind.Value;
                    int count = (int)perCell + (Rand(i, 99) < perCell - (int)perCell ? 1 : 0);
                    for (int k = 0; k < count; k++)
                    {
                        float px = x + 0.15f + 0.7f * Rand(i, k * 4), pz = _depth - (y + 0.15f + 0.7f * Rand(i, k * 4 + 1));
                        var batch = variants[(int)(Rand(i, k * 4 + 2) * variants.Count) % variants.Count];
                        float scale = 1f + jitter * (Rand(i, k * 4 + 3) * 2f - 1f);
                        batch.Add(new Vector3(px, GroundHeight(px, pz), pz), Quaternion.Euler(0f, Rand(i, k * 4 + 5) * 360f, 0f), scale);
                    }
                }
        }

        private (List<InstanceBatch>, float, float)? PropKind(string terrain)
        {
            if (terrain == null) return null;
            if (_propKinds.TryGetValue(terrain, out var kind)) return kind;
            kind = null;
            if (_art.TryGet("prop." + terrain, out var models, out var perCell, out var jitter))
            {
                var list = new List<InstanceBatch>();
                foreach (var m in models) list.Add(new InstanceBatch(m.Mesh, m.Materials, m.Local));
                _allPropBatches.AddRange(list);
                kind = (list, perCell, jitter);
            }
            return _propKinds[terrain] = kind;
        }

        /// <summary>칸 번호·순번 → 0~1 (보이기 전용 흩뿌림, 규칙 계산과 무관).</summary>
        private static float Rand(int cell, int salt)
        {
            uint v = (uint)cell * 2654435761u ^ (uint)(salt + 1) * 40503u;
            v ^= v >> 13; v *= 0x5bd1e995u; v ^= v >> 15;
            return (v & 0xFFFFFF) / (float)0x1000000;
        }

        /// <summary>전장 좌표 → 월드.</summary>
        public Vector3 ToWorld(Vec p)
        {
            float x = (float)p.X.ToDisplayDouble(), z = _depth - (float)p.Y.ToDisplayDouble();
            return new Vector3(x, GroundHeight(x, z), z);
        }

        /// <summary>바닥 메시와 같은 높이 (꼭짓점 높이의 쌍선형 보간).</summary>
        public float GroundHeight(float x, float z)
        {
            x = Mathf.Clamp(x, 0f, Width);
            z = Mathf.Clamp(z, 0f, Height);
            int i = Mathf.Min((int)x, Width - 1), j = Mathf.Min((int)z, Height - 1);
            float fx = x - i, fz = z - j;
            float h00 = _vertexHeights[j * (Width + 1) + i], h10 = _vertexHeights[j * (Width + 1) + i + 1];
            float h01 = _vertexHeights[(j + 1) * (Width + 1) + i], h11 = _vertexHeights[(j + 1) * (Width + 1) + i + 1];
            return Mathf.Lerp(Mathf.Lerp(h00, h10, fx), Mathf.Lerp(h01, h11, fx), fz);
        }

        /// <summary>
        /// 화면 위치 → 전장 좌표. 입력 경계에서 한 번만 고정소수점으로 바꾸고, 명령 기록에는 그 값이 남는다 (리플레이 결정성).
        /// </summary>
        public bool ScreenToField(Vector2 screen, out Vec point)
        {
            point = default;
            if (!Rig.Ground(screen, 0f, out var w)) return false;
            // 높이가 있는 땅: 그 자리 높이의 평면과 다시 만나 보며 가까워진다 (몇 번이면 충분)
            for (int k = 0; k < 4; k++)
                if (!Rig.Ground(screen, GroundHeight(w.x, w.z), out w)) return false;
            point = new Vec(Fix.FromRaw((long)Mathf.Round(w.x * Fix.One)), Fix.FromRaw((long)Mathf.Round((_depth - w.z) * Fix.One)));
            return true;
        }

        /// <summary>카메라가 그릴 화면 영역 (픽셀).</summary>
        public void SetViewport(Rect pixels)
        {
            if (pixels.width <= 1 || pixels.height <= 1 || Rig.Camera.pixelRect == pixels) return;
            Rig.Camera.pixelRect = pixels;
            Rig.Refresh(); // 화면 영역이 바뀌면 전장 밖이 보이지 않게 다시 맞춘다
        }

        public void LookAt(CorpsState c) { if (c != null) Rig.LookAt(ToWorld(c.Anchor)); }

        /// <summary>틱이 지난 뒤: 안개·지형 텍스처, 깃발·표식 위치.</summary>
        public void Refresh()
        {
            _texture.Refresh();
            RefreshProps();
            var ctx = _ctl.Context;
            float flagScale = Mathf.Clamp(Rig.Distance / 22f, 0.8f, 2.5f); // 멀리서도 깃발이 보이게
            foreach (var kv in _flags)
            {
                var c = kv.Key;
                bool shown = c.InBattle && _ctl.Sees(c);
                kv.Value.gameObject.SetActive(shown);
                if (!shown) continue;
                Place(kv.Value, c.Anchor, flagScale);
            }
            ctx.Vision.TryGetValue(_ctl.Team, out var vision);
            foreach (var kv in _lastSeen)
            {
                var c = kv.Key;
                bool show = !_ctl.Sees(c) && vision != null && vision.LastSeen.TryGetValue(c.Id, out var seen) && Place(kv.Value, seen, flagScale * 0.8f);
                kv.Value.gameObject.SetActive(show);
            }

            RefreshStratagems(flagScale);

            var sel = _ctl.Selected;
            bool ring = sel != null && sel.InBattle;
            _ringLine.gameObject.SetActive(ring);
            if (ring)
            {
                var center = ToWorld(sel.Anchor) + new Vector3(0f, 0.08f, 0f);
                for (int i = 0; i < RingSegments; i++)
                {
                    float a = i * Mathf.PI * 2f / RingSegments;
                    _ringLine.SetPosition(i, center + new Vector3(Mathf.Cos(a) * RingRadius, 0f, Mathf.Sin(a) * RingRadius));
                }
            }
            bool target = ring && sel.Order.Kind == OrderKinds.MoveTo;
            _target.gameObject.SetActive(target);
            if (target) _target.position = ToWorld(sel.Order.Point) + new Vector3(0f, 0.04f, 0f);

            // 남은 이동 경로
            _pathPoints.Clear();
            if (ring && sel.Path != null && sel.PathIndex < sel.Path.Count)
            {
                _pathPoints.Add(ToWorld(sel.Anchor) + Vector3.up * 0.1f);
                for (int i = sel.PathIndex; i < sel.Path.Count; i++) _pathPoints.Add(ToWorld(sel.Path[i].Center) + Vector3.up * 0.1f);
            }
            // 예약된 지점 (경유): 지금 길 끝에서 차례로 잇고, 지점마다 표식
            int wi = 0;
            if (ring)
                foreach (var q in sel.Queue)
                {
                    if (q.Kind != Game.Battle.Model.Commands.CommandKinds.MoveTo) continue;
                    var p = ToWorld(new Vec(q.X, q.Y)) + Vector3.up * 0.1f;
                    _pathPoints.Add(p);
                    if (wi >= _waypoints.Count) _waypoints.Add(Part(_root.transform, _cylinder, _waypointMat, Vector3.zero, new Vector3(0.4f, 0.03f, 0.4f)));
                    _waypoints[wi].gameObject.SetActive(true);
                    _waypoints[wi].position = p;
                    wi++;
                }
            for (; wi < _waypoints.Count; wi++) _waypoints[wi].gameObject.SetActive(false);
            _path.positionCount = _pathPoints.Count;
            for (int i = 0; i < _pathPoints.Count; i++) _path.SetPosition(i, _pathPoints[i]);

            // 지정 중인 스킬의 사거리
            Fix? range = ring && _ctl.TargetingSkill != null && ctx.Defs.Skills.TryGetValue(_ctl.TargetingSkill, out var skill) ? skill.Range : null;
            _range.gameObject.SetActive(range.HasValue);
            if (range.HasValue)
            {
                var center = ToWorld(sel.Anchor);
                float r = (float)range.Value.ToDisplayDouble();
                _range.positionCount = RangeSegments;
                for (int i = 0; i < RangeSegments; i++)
                {
                    float a = i * Mathf.PI * 2f / RangeSegments;
                    float x = center.x + Mathf.Cos(a) * r, z = center.z + Mathf.Sin(a) * r;
                    _range.SetPosition(i, new Vector3(x, GroundHeight(x, z) + 0.1f, z));
                }
            }

            ApplyAtmosphere(ctx);

            // 버프·디버프 테두리 (D18)
            foreach (var c in ctx.Corps)
            {
                bool buff = false, debuff = false;
                foreach (var st in c.Statuses)
                {
                    if (st.AttackPp > Fix.Zero || st.DefensePp > Fix.Zero || st.TargetCorpsPp > Fix.Zero) buff = true;
                    if (st.Id == StatusIds.Taunted || st.AttackPp < Fix.Zero) debuff = true;
                }
                foreach (var u in c.Units) if (u.Alive && u.Confused) { debuff = true; break; }
                bool show = c.InBattle && _ctl.Sees(c) && (buff || debuff);
                if (!_statusRings.TryGetValue(c, out var line))
                {
                    if (!show) continue;
                    line = _statusRings[c] = Line("Status " + c.Id, BuffColor, 0.1f, true);
                    line.positionCount = 32;
                }
                line.gameObject.SetActive(show);
                if (!show) continue;
                line.sharedMaterial = LineMaterial(debuff ? DebuffColor : BuffColor);
                var center = ToWorld(c.Anchor);
                const float r = 2.4f;
                for (int i = 0; i < 32; i++)
                {
                    float a = i * Mathf.PI * 2f / 32;
                    float x = center.x + Mathf.Cos(a) * r, z = center.z + Mathf.Sin(a) * r;
                    line.SetPosition(i, new Vector3(x, GroundHeight(x, z) + 0.06f, z));
                }
            }

            foreach (var (state, ringLine, pillar) in _objects)
            {
                var mat = state.Owner == null ? _objNone : state.Owner == _ctl.Team ? _objAlly : _objEnemy;
                ringLine.sharedMaterial = mat;
                if (pillar != null) pillar.sharedMaterial = mat;
            }

            // 고른 자리의 효과 범위 (확인 전 미리보기)
            var aim = ring && _ctl.TargetingSkill != null ? _ctl.AimPoint : null;
            bool inRange = aim.HasValue && _ctl.AimInRange();
            _aimIn.gameObject.SetActive(aim.HasValue && inRange);
            _aimOut.gameObject.SetActive(aim.HasValue && !inRange);
            if (aim.HasValue)
            {
                var line = inRange ? _aimIn : _aimOut;
                var center = ToWorld(aim.Value);
                float r = Mathf.Max(0.8f, (float)_ctl.PreviewRadius(_ctl.TargetingSkill).ToDisplayDouble()); // 한 점 스킬도 보이게
                line.positionCount = RangeSegments;
                for (int i = 0; i < RangeSegments; i++)
                {
                    float a = i * Mathf.PI * 2f / RangeSegments;
                    float x = center.x + Mathf.Cos(a) * r, z = center.z + Mathf.Sin(a) * r;
                    line.SetPosition(i, new Vector3(x, GroundHeight(x, z) + 0.14f, z));
                }
            }
        }

        /// <summary>
        /// 시간대·날씨 빛 (D19): 낮 = 흰 햇빛, 저녁 = 주황 낮은 해, 밤 = 푸르고 어둡게. 비 = 어둡게 + 옅은 안개, 안개 = 짙은 안개,
        /// 무더위 = 노란 기운, 혹한 = 푸른 기운. 바뀔 때만 다시 정한다 (이벤트로 날씨가 바뀌면 따라간다).
        /// </summary>
        private void ApplyAtmosphere(BattleContext ctx)
        {
            var env = ctx.Environment;
            string weather = env.EffectiveWeather.Id ?? "clear", time = env.EffectiveTime.Id ?? "day";
            string key = weather + "|" + time;
            if (key == _atmosphereKey) return;
            _atmosphereKey = key;
            Color sun = Color.white, sky = new Color(0.08f, 0.09f, 0.10f), ambient = new Color(0.55f, 0.55f, 0.58f);
            float intensity = 1.1f, pitch = 55f;
            if (time == "dusk") { sun = new Color(1f, 0.72f, 0.45f); intensity = 0.8f; pitch = 18f; sky = new Color(0.20f, 0.12f, 0.10f); ambient = new Color(0.45f, 0.36f, 0.32f); }
            if (time == "night") { sun = new Color(0.55f, 0.65f, 0.95f); intensity = 0.35f; pitch = 40f; sky = new Color(0.02f, 0.03f, 0.06f); ambient = new Color(0.16f, 0.18f, 0.26f); }
            bool fog = false;
            Color fogColor = sky;
            float density = 0f;
            switch (weather)
            {
                case "rain": intensity *= 0.75f; ambient *= 0.85f; fog = true; density = 0.012f; fogColor = new Color(0.35f, 0.38f, 0.42f); break;
                case "fog": fog = true; density = 0.035f; fogColor = new Color(0.62f, 0.64f, 0.66f); break;
                case "heat": sun = Color.Lerp(sun, new Color(1f, 0.85f, 0.55f), 0.5f); ambient = Color.Lerp(ambient, new Color(0.65f, 0.55f, 0.40f), 0.4f); break;
                case "cold": sun = Color.Lerp(sun, new Color(0.75f, 0.85f, 1f), 0.5f); ambient = Color.Lerp(ambient, new Color(0.50f, 0.58f, 0.70f), 0.4f); break;
                case "gale": intensity *= 0.9f; break;
            }
            _sun.color = sun;
            _sun.intensity = intensity;
            _sun.transform.rotation = Quaternion.Euler(pitch, -30f, 0f);
            _camera.backgroundColor = fog ? fogColor : sky;
            RenderSettings.ambientLight = ambient;
            RenderSettings.fog = fog;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = density;
        }

        /// <summary>함정·위장 깃발 표식을 지금 상태에 맞춘다 (없어진 것은 지운다).</summary>
        private void RefreshStratagems(float flagScale)
        {
            var ctx = _ctl.Context;
            var alive = new HashSet<object>();
            foreach (var t in ctx.Traps)
            {
                if (t.Team != _ctl.Team) continue; // 상대 함정은 보이지 않는다
                alive.Add(t);
                if (!_stratagemMarks.TryGetValue(t, out var m))
                {
                    float r = (float)t.Radius.ToDisplayDouble();
                    m = Part(_root.transform, _cylinder, _trapMat, Vector3.zero, new Vector3(r * 2f, 0.02f, r * 2f));
                    _stratagemMarks[t] = m;
                }
                m.position = ToWorld(t.Point) + new Vector3(0f, 0.04f, 0f);
                // 준비 중이면 반쯤 (설치 중)
                float k = ctx.Tick >= t.ArmedTick ? 1f : 0.5f;
                float rr = (float)t.Radius.ToDisplayDouble() * 2f * k;
                m.localScale = new Vector3(rr, 0.02f, rr);
            }
            foreach (var d in ctx.Decoys)
            {
                bool mine = d.Team == _ctl.Team;
                if (!mine && !ctx.IsPointVisibleTo(_ctl.Team, d.Point)) continue;
                alive.Add(d);
                if (!_stratagemMarks.TryGetValue(d, out var f))
                {
                    f = Flag("Decoy " + d.Id, _cylinder, _decoyPoleMat, mine ? _decoyOwnMat : _enemyFlagMat);
                    _stratagemMarks[d] = f;
                }
                Place(f, d.Point, flagScale);
            }
            var gone = new List<object>();
            foreach (var kv in _stratagemMarks) if (!alive.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var g in gone)
            {
                if (_stratagemMarks[g] != null) Object.Destroy(_stratagemMarks[g].gameObject);
                _stratagemMarks.Remove(g);
            }
        }

        private bool Place(Transform t, Vec at, float scale)
        {
            t.position = ToWorld(at);
            t.localScale = Vector3.one * scale;
            return true;
        }

        /// <summary>틱을 돌린 뒤 부른다: 소부대 위치를 한 칸 넘기고, 연출(화살)을 만든다.</summary>
        public void OnTicked()
        {
            var ctx = _ctl.Context;
            foreach (var c in ctx.Corps)
                foreach (var u in c.Units)
                {
                    var now = ToWorld(u.Position);
                    _unitPos[u] = _unitPos.TryGetValue(u, out var p) ? (p.cur, now) : (now, now);
                }
            Effects?.OnTicked(ctx);
        }

        /// <summary>
        /// 그리기. 매 프레임 부른다 (인스턴싱은 그 프레임에만 그려진다).
        /// </summary>
        /// <param name="alpha">직전 틱과 지금 틱 사이 위치 (0~1).</param>
        /// <param name="fxDelta">연출 시계를 얼마나 진행할지 (일시정지면 0).</param>
        public void Draw(float alpha, float fxDelta)
        {
            var ctx = _ctl.Context;
            Effects.Advance(fxDelta);
            float clock = Effects.Clock;
            if (fxDelta > 0f) _flashFrame++;
            foreach (var b in _allSoldierBatches) b.Matrices.Clear();
            foreach (var c in ctx.Corps)
            {
                if (!c.InBattle || !_ctl.Sees(c)) continue;
                int teamColor = c == _ctl.Selected ? Selected : c.Team == _ctl.Team ? Ally : Enemy;
                // 몸 상태 (D18): 사기가 퇴각 기준 + 15 밑이면 바랜 색·떨림, 숨은 아군은 어둡고 성기게
                bool shaken = c.Temperament != null && c.Morale <= c.Temperament.RetreatMorale + Fix.FromInt(15);
                bool hidden = c.Team == _ctl.Team && (c.Hidden || c.HasStatus(StatusIds.AmbushSetup));
                if (c != _ctl.Selected && shaken) teamColor = c.Team == _ctl.Team ? AllyShaken : EnemyShaken;
                if (hidden) teamColor = HiddenTone;
                var facing = new Vector3((float)c.Facing.X.ToDisplayDouble(), 0f, -(float)c.Facing.Y.ToDisplayDouble());
                var rot = facing.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(facing) : Quaternion.identity;
                foreach (var u in c.Units)
                {
                    if (!u.Alive)
                    {
                        // 방금 전멸한 부대: 남아 있던 화면 병사가 모두 쓰러진다
                        if (_shownSoldiers.TryGetValue(u, out var was) && was > 0 && _unitPos.TryGetValue(u, out var lastPos))
                        {
                            int sd = Mathf.CeilToInt(Mathf.Sqrt(was));
                            float hd = (sd - 1) * SoldierSpacing / 2f;
                            for (int i = 0; i < was; i++) AddFallen(lastPos.cur + rot * new Vector3(i % sd * SoldierSpacing - hd, 0f, hd - i / sd * SoldierSpacing), i + u.GetHashCode());
                        }
                        _shownSoldiers[u] = 0;
                        continue;
                    }
                    var batch = SoldierBatch(u.Troop.Id, u.Confused ? Confused : teamColor);
                    // 맞을 때: 상자는 밝은 색으로, 모델은 (색을 밝힐 수 없어) 살짝 튀어 오른다
                    bool isCube = batch.Mesh == _cube;
                    var hitBatch = isCube ? CubeBatch(Hit) : batch;
                    long n = SoldierRatio.VisibleSoldiers(u.Troops, _troopsPerSoldier);
                    int side = Mathf.CeilToInt(Mathf.Sqrt(n));
                    var (prev, cur) = _unitPos.TryGetValue(u, out var pc) ? pc : (ToWorld(u.Position), ToWorld(u.Position));
                    var center = Vector3.Lerp(prev, cur, alpha);
                    bool walking = (cur - prev).sqrMagnitude > 1e-5f;
                    // 근접 공격: 대상 쪽으로 찌르기 (병사마다 박자를 어긋나게)
                    Vector3 lunge = Vector3.zero;
                    if (u.AttackTarget != null && u.Troop.HasTag("melee") && _unitPos.TryGetValue(u.AttackTarget, out var tp))
                    {
                        lunge = tp.cur - cur;
                        lunge.y = 0f;
                        lunge = lunge.sqrMagnitude > 1e-6f ? lunge.normalized * 0.12f : Vector3.zero;
                    }
                    float half = (side - 1) * SoldierSpacing / 2f;
                    // 병력이 줄어 화면 병사가 빠지면 그 자리에 쓰러진 병사를 남긴다 (손실이 눈에 보이게)
                    if (_shownSoldiers.TryGetValue(u, out var before) && before > n)
                    {
                        int sb = Mathf.CeilToInt(Mathf.Sqrt(before));
                        float hb = (sb - 1) * SoldierSpacing / 2f;
                        for (long i = n; i < before; i++) AddFallen(cur + rot * new Vector3(i % sb * SoldierSpacing - hb, 0f, hb - i / sb * SoldierSpacing), (int)i + u.GetHashCode());
                    }
                    _shownSoldiers[u] = n;
                    for (int i = 0; i < n; i++)
                    {
                        var p = center + rot * new Vector3(i % side * SoldierSpacing - half, 0f, half - i / side * SoldierSpacing);
                        float phase = i * 1.7f + u.GetHashCode() % 7;
                        p += lunge * Mathf.Max(0f, Mathf.Sin(clock * 11f + phase));
                        float bob = walking ? Mathf.Abs(Mathf.Sin(clock * 12f + phase)) * 0.06f : 0f;
                        if (hidden && i % 2 == 1) continue; // 숨은 군단: 반만 그려 흐릿하게
                        if (u.Confused) p += new Vector3(Mathf.Sin(clock * 23f + phase) * 0.07f, 0f, Mathf.Cos(clock * 19f + phase * 1.3f) * 0.07f); // 혼란: 갈팡질팡
                        else if (shaken) p += new Vector3(Mathf.Sin(clock * 31f + phase) * 0.025f, 0f, 0f); // 사기 바닥: 떨림
                        // 맞고 있는 군단: 몇 명씩 번갈아 깜빡인다
                        bool flash = c.LostThisTick > 0 && (i + _flashFrame / 3) % 6 == 0;
                        p.y = GroundHeight(p.x, p.z) + bob + (flash && !isCube ? 0.08f : 0f);
                        (flash ? hitBatch : batch).Add(p, rot);
                    }
                }
                // 장수: 군단 한가운데 조금 뒤, 부대원보다 크게 (총대장은 군기)
                Vector3 sum = Vector3.zero;
                int alive = 0;
                foreach (var u in c.Units)
                    if (u.Alive && _unitPos.TryGetValue(u, out var up)) { sum += Vector3.Lerp(up.prev, up.cur, alpha); alive++; }
                if (alive > 0)
                {
                    var gp = sum / alive + rot * new Vector3(0f, 0f, -0.9f);
                    gp.y = GroundHeight(gp.x, gp.z);
                    GeneralBatch(c.Setup.Role == Game.Battle.Model.Simulation.CorpsRoles.Commander, teamColor).Add(gp, rot);
                }
            }
            foreach (var b in _allSoldierBatches) b.Draw(Rig.Camera);
            _fallen?.Draw(Rig.Camera);
            foreach (var b in _allPropBatches) b.Draw(Rig.Camera);
            Effects.Draw(ctx, Rig.Camera);
        }

        public void Destroy()
        {
            RenderSettings.fog = false; // 날씨 안개는 전장에서만
            Object.Destroy(_root);
            _texture.Destroy();
            _art.Destroy();
            foreach (var o in _owned) if (o != null) Object.Destroy(o);
        }

        private T Own<T>(T o) where T : Object { _owned.Add(o); return o; }

        private static Transform Part(Transform parent, Mesh mesh, Material mat, Vector3 localPos, Vector3 scale)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        private static Material _template;

        /// <summary>
        /// 기본 재질(Resources/Materials/GameLit, URP Lit)을 복사해 색만 바꾼다. 재질 에셋을 Resources에 두어 빌드에 셰이더(인스턴싱 변형 포함)가 들어간다.
        /// 없으면 렌더 파이프라인 기본 재질 → 셰이더 이름 순으로 찾는다.
        /// </summary>
        private static Material NewMaterial(Color color)
        {
            if (_template == null) _template = Resources.Load<Material>("Materials/GameLit");
            if (_template == null && GraphicsSettings.defaultRenderPipeline != null) _template = GraphicsSettings.defaultRenderPipeline.defaultMaterial;
            var m = _template != null ? new Material(_template) : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            m.color = color;
            m.mainTexture = null;
            return m;
        }

        private Transform Flag(string name, Mesh cylinder, Material pole, Material cloth)
        {
            var flag = new GameObject(name).transform;
            flag.SetParent(_root.transform, false);
            Part(flag, cylinder, pole, new Vector3(0f, 0.9f, 0f), new Vector3(0.06f, 0.9f, 0.06f));
            Part(flag, _cube, cloth, new Vector3(0.35f, 1.55f, 0f), new Vector3(0.7f, 0.45f, 0.04f));
            return flag;
        }

        private Material LineMaterial(Color color)
        {
            if (!_lineMats.TryGetValue(color, out var mat)) _lineMats[color] = mat = Own(NewMaterial(color));
            return mat;
        }

        private LineRenderer Line(string name, Color color, float width, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var line = go.AddComponent<LineRenderer>();
            if (!_lineMats.TryGetValue(color, out var mat)) _lineMats[color] = mat = Own(NewMaterial(color));
            line.sharedMaterial = mat;
            line.widthMultiplier = width;
            line.loop = loop;
            line.positionCount = 0;
            line.useWorldSpace = true;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        /// <summary>꼭짓점 (W+1)×(H+1) 높이: 맞닿은 칸(최대 4개) 높이의 평균 → 언덕·산이 완만한 경사가 된다.</summary>
        private static float[] VertexHeights(BattleContext ctx)
        {
            var f = ctx.Field;
            int w = f.Width, h = f.Height;
            var heights = new float[(w + 1) * (h + 1)];
            for (int j = 0; j <= h; j++)
                for (int i = 0; i <= w; i++)
                {
                    float sum = 0f;
                    int n = 0;
                    for (int dx = -1; dx <= 0; dx++)
                        for (int dz = -1; dz <= 0; dz++)
                        {
                            int cx = i + dx, wz = j + dz; // 월드 z 줄 → 전장 y = 높이 − 1 − wz
                            if (cx < 0 || cx >= w || wz < 0 || wz >= h) continue;
                            sum += f.RuleAt(new Cell(cx, h - 1 - wz)).Height;
                            n++;
                        }
                    heights[j * (w + 1) + i] = n == 0 ? 0f : sum / n * HeightScale;
                }
            return heights;
        }

        /// <summary>높이가 있는 바닥 격자. UV (0,0) = 월드 (0, ·, 0) = 전장 왼쪽 아래.</summary>
        private static Mesh GroundMesh(int w, int d, float[] heights)
        {
            var verts = new Vector3[(w + 1) * (d + 1)];
            var uv = new Vector2[verts.Length];
            for (int j = 0; j <= d; j++)
                for (int i = 0; i <= w; i++)
                {
                    int k = j * (w + 1) + i;
                    verts[k] = new Vector3(i, heights[k], j);
                    uv[k] = new Vector2((float)i / w, (float)j / d);
                }
            var tris = new int[w * d * 6];
            int t = 0;
            for (int j = 0; j < d; j++)
                for (int i = 0; i < w; i++)
                {
                    int a = j * (w + 1) + i, b = a + (w + 1);
                    tris[t++] = a; tris[t++] = b; tris[t++] = b + 1;
                    tris[t++] = a; tris[t++] = b + 1; tris[t++] = a + 1;
                }
            var mesh = new Mesh { name = "Ground" };
            if (verts.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
