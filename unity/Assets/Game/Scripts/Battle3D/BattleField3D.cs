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
        private const float SoldierSpacing = 0.24f; // 2026-11-13: 병사 예산 900 → 2000에 맞춰 촘촘히 (대군 느낌, 부대 덩어리 크기는 비슷)
        /// <summary>지형 높이 1단계의 월드 높이.</summary>
        private const float HeightScale = 0.8f;
        private const int RangeSegments = 48;
        private static readonly Vector3 SoldierScale = new Vector3(0.18f, 0.36f, 0.18f);
        /// <summary>정적 색은 처음 쓸 때 정한다 (화면 스타일을 읽은 뒤). 명시적 정적 생성자가 있어야 초기화 시점이 보장된다.</summary>
        static BattleField3D() { }

        // 진영 색 (art-direction.md 2장: 아군 쪽빛, 적군 붉은색 — 수묵 스타일이면 먹을 섞은 짙은 색)
        private static readonly Color AllyColor = Game.Unity.UI.UiStyle.Paper ? new Color(0.20f, 0.36f, 0.62f) : new Color(0.30f, 0.50f, 0.95f);
        private static readonly Color EnemyColor = Game.Unity.UI.UiStyle.Paper ? new Color(0.70f, 0.20f, 0.16f) : new Color(0.90f, 0.30f, 0.25f);
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
        // 화면용 방향 · 위치 (PR C 2026-11-20: 덜덜거림 없애기). 규칙 값(틱마다 바뀜)을 그대로 쓰지 않고 천천히 따라간다 — 규칙 계산(src/)은 그대로
        private readonly Dictionary<CorpsState, float> _dispYaw = new Dictionary<CorpsState, float>();
        private readonly Dictionary<CorpsState, Quaternion> _dispRot = new Dictionary<CorpsState, Quaternion>();
        private readonly Dictionary<SubUnitState, (Vector3 pos, Vector3 vel)> _disp = new Dictionary<SubUnitState, (Vector3, Vector3)>();
        private readonly HashSet<long> _engagedPairs = new HashSet<long>();
        /// <summary>군단이 도는 최대 빠르기 (도/초)와 무시할 작은 변화 (도).</summary>
        private const float TurnDegPerSec = 180f, TurnDeadDeg = 1.5f;
        /// <summary>소부대 화면 위치가 따라가는 시간 (초), 이보다 멀면 바로 그 자리로 (순간이동).</summary>
        private const float PosSmooth = 0.15f, SnapDist = 2f;
        /// <summary>맞붙은 적 소부대와 앞줄끼리 띄우는 간격 (경계선).</summary>
        private const float EngageGap = 0.3f;
        private readonly Dictionary<Color, Material> _lineMats = new Dictionary<Color, Material>();
        private int _flashFrame;
        /// <summary>걷는 병사의 앞 기울기 (튀는 대신).</summary>
        private static readonly Quaternion WalkLean = Quaternion.Euler(7f, 0f, 0f);

        // ---- 병종별 공격 동작 (H3 나머지, 2026-11-21): 모두 부드러운 곡선, 병사마다 박자 어긋남, 앞줄 위주 ----
        private enum AttackStyle { None, Spear, Sword, Horse, Ranged }
        /// <summary>창: 앞으로 길게 찌르기 (폭 · 빠르기).</summary>
        private const float SpearReach = 0.11f, SpearBeat = 4f;
        /// <summary>칼 · 방패: 제자리에서 좌우로 휘두르듯 몸 돌리기 (도 · 빠르기), 앞으로는 아주 조금.</summary>
        private const float SwordSwingDeg = 15f, SwordBeat = 4.5f, SwordStep = 0.02f;
        /// <summary>기병: 맞붙는 순간 앞으로 짧게 돌진했다가 돌아옴 (거리 · 걸리는 초).</summary>
        private const float HorseCharge = 0.25f, HorseChargeTime = 0.8f;
        /// <summary>원거리: 쏘는 순간 뒤로 살짝 (거리 · 돌아오는 초).</summary>
        private const float RangedRecoil = 0.03f, RangedRecoilTime = 0.35f;
        private readonly Dictionary<SubUnitState, float> _meleeSince = new Dictionary<SubUnitState, float>();

        private static AttackStyle StyleOf(SubUnitState u)
        {
            var t = u.Troop;
            if (t.HasTag("ranged")) return AttackStyle.Ranged;
            if (!t.HasTag("melee")) return AttackStyle.None;
            if (t.HasTag("mounted")) return AttackStyle.Horse;
            if (t.HasTag("polearm")) return AttackStyle.Spear;
            return AttackStyle.Sword; // 보병 · 방패병 · 친위 · 공병 · 정찰 · 배(근접)
        }
        /// <summary>
        /// 소부대 바닥 테두리 (H1, 2026-11-20 사용자 "아군 · 적 구분"): 병사 덩어리를 감싸는 편 색 사각형.
        /// 서로 맞붙어 섞여도 어느 덩어리가 어느 편인지 바닥 선으로 갈린다. 빛 영향 없는 재질 (밤에도 선명).
        /// </summary>
        private readonly Dictionary<SubUnitState, (LineRenderer line, int frame)> _unitOutlines = new Dictionary<SubUnitState, (LineRenderer, int)>();
        private int _outlineFrame;
        private const float OutlineMargin = 0.2f;
        private readonly Vector2[] _outlineCorners = new Vector2[4];
        private const int OutlinePerSide = 4; // 한 변을 4토막 → 언덕에서도 땅을 따라감

        private void DrawUnitOutline(SubUnitState u, int tone, Vector3 center, Quaternion rot, float halfW, float front, float back)
        {
            if (!_unitOutlines.TryGetValue(u, out var o) || o.line == null)
                o.line = Line("UnitOutline", AllyColor, 0.06f, true, true);
            var mat = UnlitMaterial(tone == Selected ? SelectedColor : tone == Ally ? AllyColor : EnemyColor);
            if (o.line.sharedMaterial != mat) o.line.sharedMaterial = mat;
            o.line.widthMultiplier = tone == Selected ? 0.09f : 0.06f;
            o.frame = _outlineFrame;
            _unitOutlines[u] = o;
            var line = o.line;
            if (!line.gameObject.activeSelf) line.gameObject.SetActive(true);
            float x0 = -halfW - OutlineMargin, x1 = halfW + OutlineMargin, z0 = back - OutlineMargin, z1 = front + OutlineMargin;
            var corners = _outlineCorners;
            corners[0] = new Vector2(x0, z1); corners[1] = new Vector2(x1, z1); corners[2] = new Vector2(x1, z0); corners[3] = new Vector2(x0, z0);
            line.positionCount = 4 * OutlinePerSide;
            int k = 0;
            for (int s = 0; s < 4; s++)
            {
                var a = corners[s];
                var b = corners[(s + 1) % 4];
                for (int t = 0; t < OutlinePerSide; t++)
                {
                    var q = Vector2.Lerp(a, b, t / (float)OutlinePerSide);
                    var p = center + rot * new Vector3(q.x, 0f, q.y);
                    p.y = GroundHeight(p.x, p.z) + 0.04f;
                    line.SetPosition(k++, p);
                }
            }
        }

        /// <summary>이번 화면에서 그리지 않은 소부대(전멸 · 안 보임)의 테두리는 숨긴다.</summary>
        private void HideStaleOutlines()
        {
            foreach (var kv in _unitOutlines)
                if (kv.Value.frame != _outlineFrame && kv.Value.line != null && kv.Value.line.gameObject.activeSelf)
                    kv.Value.line.gameObject.SetActive(false);
        }
        private readonly Transform _ring;
        /// <summary>선택 표시: 꽉 찬 원판 대신 테두리만 (병사가 가려지지 않게).</summary>
        private readonly LineRenderer _ringLine;
        private const int RingSegments = 40;
        private const float RingRadius = 1.8f;
        // 바닥 링 게이지 (18·19 시안): 고른 군단 = 바깥 병력(초록 남음 · 붉은 잃음) + 안 사기(주황), 다른 우리 군단 = 가는 병력 고리
        private static readonly Color GaugeTroops = new Color(0.3f, 0.78f, 0.36f);
        private static readonly Color GaugeLost = new Color(0.78f, 0.27f, 0.2f);
        private static readonly Color GaugeMorale = new Color(0.97f, 0.66f, 0.2f);
        private static readonly Color GaugeBack = new Color(0.16f, 0.17f, 0.15f);
        /// <summary>이만큼 멀리 축소하면 링 대신 깃발 옆 작은 병력 막대 (18·19 시안 "많이 축소했을 때").</summary>
        public const float FarDistance = 42f;
        /// <summary>많이 축소한 상태 (화면이 깃발 막대를 그린다).</summary>
        public bool Far => Rig != null && Rig.Distance >= FarDistance;
        /// <summary>고른 군단의 남은 길 끝 (월드, 없으면 null) — 화면이 경로 끝 꼬리표를 단다.</summary>
        public Vector3? PathEnd { get; private set; }
        private LineRenderer _gaugeLost, _gaugeTroops, _gaugeMoraleBack, _gaugeMorale;
        private readonly Dictionary<CorpsState, (LineRenderer lost, LineRenderer troops)> _allyGauges = new Dictionary<CorpsState, (LineRenderer, LineRenderer)>();
        // 남은 이동 경로 = 파란 점선 (18·19 시안 mock_path)
        private static readonly Color DashColor = new Color(0.22f, 0.4f, 0.78f);
        private readonly List<LineRenderer> _dashes = new List<LineRenderer>();
        private const float DashLen = 0.55f, DashGap = 0.35f;
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
            cam.backgroundColor = Game.Unity.UI.UiStyle.Paper ? new Color(0.91f, 0.87f, 0.80f) : new Color(0.08f, 0.09f, 0.10f);
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
            _ringLine = Line("SelectRing", SelectedColor, 0.12f, true, true, overlay: true);
            _ringLine.positionCount = RingSegments;
            _ringLine.gameObject.SetActive(false); // 바닥 링 게이지로 바꿈
            _gaugeLost = Line("GaugeLost", GaugeLost, 0.2f, false, true, overlay: true);
            _gaugeTroops = Line("GaugeTroops", GaugeTroops, 0.2f, false, true, overlay: true);
            _gaugeMoraleBack = Line("GaugeMoraleBack", GaugeBack, 0.14f, false, true, overlay: true);
            _gaugeMorale = Line("GaugeMorale", GaugeMorale, 0.14f, false, true, overlay: true);
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
                    : o.Setup.Type == Game.Battle.Model.Victory.MapObjectSetup.Depot ? new Vector3(1.2f, 0.4f, 1.2f)
                    : o.Setup.Type == Game.Battle.Model.Victory.MapObjectSetup.Dam ? new Vector3(2.4f, 0.5f, 0.6f) // 둑: 넓고 낮은 둑방
                    : new Vector3(0.2f, 1.6f, 0.2f);
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
            string cacheTroop = troop;
            string[] tk = troop.Split('|');
            troop = tk[0];
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
                // 모델이 없으면 코드로 만든 병종 인형 (SoldierMeshes): 1단계 병종 모양 + 치장 (troop 키 = 병종 id|갑옷 id|말 id)
                var armorLook = tk.Length > 1 && tk[1].Length > 0 ? CosmeticLooks.Of(_ctl.Context.Defs, tk[1]) : null;
                var horseLook = tk.Length > 2 && tk[2].Length > 0 ? CosmeticLooks.Of(_ctl.Context.Defs, tk[2]) : null;
                string baseType = troop;
                while (baseType != null && types.TryGetValue(baseType, out var d) && d.Parent != null && !d.HasTag("engineer")) baseType = d.Parent;
                if (types.TryGetValue(troop, out var td) && td.HasTag("engineer")) baseType = "engineer"; // 공병은 보병에서 나왔어도 따로 (R-83)
                var mat = Own(NewMaterial(SoldierColors[color]));
                mat.enableInstancing = true;
                // 부위별 재질: 몸통(팀 색) · 얼굴 · 무기 · 다리 · 치장 갑옷 색(팀 색과 가까우면 흐리게, 9장 3) · 말 색
                var mesh = SoldierMeshes.For(baseType, armorLook != null);
                var parts = SoldierMeshes.PartsOf(mesh);
                var mats = new Material[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    mats[i] = parts[i] == SoldierMeshes.Team ? mat
                        : parts[i] == SoldierMeshes.Armor ? ColorMaterial(CosmeticLooks.AwayFromTeams(CosmeticLooks.ColorOf(armorLook, "trim", SoldierMeshes.DarkColor), AllyColor, EnemyColor))
                        : parts[i] == SoldierMeshes.Horse ? ColorMaterial(horseLook != null ? CosmeticLooks.ColorOf(horseLook, "horse", SoldierMeshes.DarkColor) : SoldierMeshes.DarkColor)
                        : PartMaterial(parts[i], parts[i] == SoldierMeshes.Skin ? SoldierMeshes.SkinColor : parts[i] == SoldierMeshes.Metal ? SoldierMeshes.MetalColor : SoldierMeshes.DarkColor);
                b = new InstanceBatch(mesh, mats, Matrix4x4.identity);
                _allSoldierBatches.Add(b);
            }
            return _soldierBatches[(cacheTroop, color)] = b;
        }

        /// <summary>
        /// 장수 묶음 (R-90 탈것): (총대장 여부, 색, 탈것) → 그릴 묶음들.
        /// 1) 아트 목록 "general.commander.&lt;탈것&gt;" / "general.&lt;탈것&gt;" (탄 모습 통째) → 그것만.
        /// 2) 아트 장수 모델(general.commander → general.default)이 있으면 탈것(아트 "mount.&lt;탈것&gt;", 없으면 코드 탈것) 위에 riderY만큼 올려 태운다.
        /// 3) 둘 다 없으면 코드로 만든 탄 인형 통째.
        /// </summary>
        private readonly Dictionary<(bool commander, int color, string mount), InstanceBatch[]> _generalBatches = new Dictionary<(bool, int, string), InstanceBatch[]>();
        private InstanceBatch[] GeneralBatches(bool commander, int color, string mount)
        {
            if (_generalBatches.TryGetValue((commander, color, mount), out var bs)) return bs;
            var list = new List<InstanceBatch>();
            Material MountBody() => mount == Game.Data.Defs.MountRules.Palanquin
                ? PartMaterial(6, SoldierMeshes.PalanquinWoodColor) : PartMaterial(5, SoldierMeshes.GeneralHorseColor);
            Material TeamMat()
            {
                var t = Own(NewMaterial(SoldierColors[color]));
                t.enableInstancing = true;
                return t;
            }
            string whole = commander && _art.TryGet("general.commander." + mount, out ArtModel _) ? "general.commander." + mount : "general." + mount;
            string riderKey = commander && _art.TryGet("general.commander", out ArtModel _) ? "general.commander" : "general.default";
            if (_art.TryGet(whole, out ArtModel wm))
                list.Add(new InstanceBatch(wm.Mesh, _art.Tinted(wm, SoldierColors[color], _art.TintOf(whole)), wm.Local));
            else if (_art.TryGet(riderKey, out ArtModel gm))
            {
                float riderY = _art.RiderYOf("mount." + mount, SoldierMeshes.RiderLift(mount));
                list.Add(new InstanceBatch(gm.Mesh, _art.Tinted(gm, SoldierColors[color], _art.TintOf(riderKey)), Matrix4x4.Translate(new Vector3(0f, riderY, 0f)) * gm.Local));
                if (_art.TryGet("mount." + mount, out ArtModel mm))
                    list.Add(new InstanceBatch(mm.Mesh, _art.Tinted(mm, SoldierColors[color], _art.TintOf("mount." + mount)), mm.Local));
                else
                    list.Add(new InstanceBatch(SoldierMeshes.General(commander, mount, rider: false),
                        new[] { TeamMat(), PartMaterial(1, SoldierMeshes.SkinColor), PartMaterial(4, SoldierMeshes.GoldColor), MountBody() }, Matrix4x4.identity));
            }
            else
                list.Add(new InstanceBatch(SoldierMeshes.General(commander, mount),
                    new[] { TeamMat(), PartMaterial(1, SoldierMeshes.SkinColor), PartMaterial(4, SoldierMeshes.GoldColor), MountBody() }, Matrix4x4.identity));
            _allSoldierBatches.AddRange(list);
            return _generalBatches[(commander, color, mount)] = list.ToArray();
        }

        private readonly Material[] _partMaterials = new Material[8];
        private readonly Dictionary<Color32, Material> _colorMaterials = new Dictionary<Color32, Material>();

        /// <summary>색마다 재질 하나 (치장 색).</summary>
        private Material ColorMaterial(Color c)
        {
            Color32 k = c;
            if (_colorMaterials.TryGetValue(k, out var m)) return m;
            m = Own(NewMaterial(c));
            m.enableInstancing = true;
            return _colorMaterials[k] = m;
        }

        /// <summary>소부대 → 병사 묶음 키 (병종 id|갑옷 id|말 id). 겉모습은 출진 설정(UnitSetup.Armor · Horse, R-108)에서.</summary>
        private string SoldierKey(Game.Battle.Model.Simulation.SubUnitState u)
        {
            if (_lookKeys.TryGetValue(u, out var k)) return k;
            string armor = null, horse = null;
            foreach (var c in _ctl.Context.Corps)
            {
                if (c.Setup?.Units == null) continue;
                foreach (var us in c.Setup.Units) if (us.UnitId == u.UnitId) { armor = us.Armor; horse = us.Horse; break; }
                if (armor != null || horse != null) break;
            }
            return _lookKeys[u] = armor == null && horse == null ? u.Troop.Id : u.Troop.Id + "|" + armor + "|" + horse;
        }
        private readonly Dictionary<Game.Battle.Model.Simulation.SubUnitState, string> _lookKeys = new Dictionary<Game.Battle.Model.Simulation.SubUnitState, string>();

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

        // ---- 쓰러지는 병사 (2026-11-14): 앞줄에서 넘어지는 동작 뒤 시체가 된다 ----
        private const float DyingSeconds = 0.45f;
        private const int DyingMax = 400;
        private struct Dying { public Vector3 Pos; public Quaternion Rot; public float Start; public string Troop; public int Color; public float Dir; public int Seed; }
        private readonly List<Dying> _dying = new List<Dying>();

        private void AddDying(Vector3 p, Quaternion rot, string troop, int color, int seed, float clock)
        {
            if (_dying.Count >= DyingMax) { AddFallen(p, seed); return; }
            _dying.Add(new Dying { Pos = p, Rot = rot, Start = clock, Troop = troop, Color = color, Dir = (seed & 1) == 0 ? 1f : -1f, Seed = seed });
        }

        /// <summary>쓰러지는 병사를 그린다: 뒤(또는 앞)로 넘어가며 가라앉고, 다 넘어지면 시체로.</summary>
        private void DrawDying(float clock)
        {
            for (int i = _dying.Count - 1; i >= 0; i--)
            {
                var d = _dying[i];
                float t = (clock - d.Start) / DyingSeconds;
                if (t >= 1f) { AddFallen(d.Pos, d.Seed); _dying.RemoveAt(i); continue; }
                t = Mathf.Clamp01(t);
                float ease = t * t; // 처음엔 천천히, 끝에 털썩
                var p = d.Pos;
                p.y = GroundHeight(p.x, p.z) - ease * 0.05f;
                var r = d.Rot * Quaternion.Euler(-85f * ease * d.Dir, 0f, 0f);
                SoldierBatch(d.Troop, d.Color).Add(p, r);
            }
        }

        /// <summary>병사 하나의 고정된 작은 흐트러짐 (바둑판처럼 반듯하지 않게).</summary>
        private static Vector3 Jitter(int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u;
                float a = ((h & 0xFFFF) / 65535f - 0.5f) * 0.08f;
                float b = (((h >> 16) & 0xFFFF) / 65535f - 0.5f) * 0.08f;
                return new Vector3(a, 0f, b);
            }
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

        /// <summary>
        /// 이름표·사기 막대를 붙일 곳 (B7): 장수가 서는 곳(군단 가운데에서 0.9 뒤) 위로, 탄 장수·깃대보다 높게.
        /// 장수는 그리기에서 Anchor + 뒤 0.9에 선다.
        /// </summary>
        public Vector3 FlagPoint(CorpsState c)
        {
            var facing = new Vector3((float)c.Facing.X.ToDisplayDouble(), 0f, -(float)c.Facing.Y.ToDisplayDouble());
            var back = facing.sqrMagnitude > 1e-6f ? -facing.normalized * 0.9f : Vector3.zero;
            var p = ToWorld(c.Anchor) + back;
            p.y = GroundHeight(p.x, p.z) + 2.9f;
            return p;
        }

        /// <summary>여러 군단이 한 화면에 들어오게: 가운데를 보고, 가장 먼 둘의 거리만큼 물러선다 (전투 시작 때 아군 전체).</summary>
        public void Frame(IEnumerable<CorpsState> corps)
        {
            var min = new Vector3(float.MaxValue, 0f, float.MaxValue);
            var max = new Vector3(float.MinValue, 0f, float.MinValue);
            int n = 0;
            foreach (var c in corps)
            {
                if (c == null || !c.InBattle) continue;
                var p = ToWorld(c.Anchor);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                n++;
            }
            if (n == 0) return;
            float spread = Mathf.Max(max.x - min.x, max.z - min.z);
            Rig.SetDistance(Mathf.Max(Rig.Distance, spread * 1.6f + 10f)); // 세로 화면이라 가로 폭이 좁다: 넉넉히
            Rig.LookAt((min + max) / 2f);
        }

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
            // 바닥 링 게이지: 고른 군단 (바깥 병력 · 안 사기)
            bool gauge = ring && !Far; // 많이 축소하면 링 대신 깃발 막대 (화면)
            foreach (var l in new[] { _gaugeLost, _gaugeTroops, _gaugeMoraleBack, _gaugeMorale }) l.gameObject.SetActive(gauge);
            if (gauge)
            {
                var center = ToWorld(sel.Anchor) + new Vector3(0f, 0.08f, 0f);
                float troops = sel.StartTroops > 0 ? Mathf.Clamp01((float)sel.Troops / sel.StartTroops) : 0f;
                float morale = Mathf.Clamp01((float)sel.Morale.ToDisplayDouble() / 100f);
                Arc(_gaugeLost, center, RingRadius, 0f, 1f);
                Arc(_gaugeTroops, center + Vector3.up * 0.01f, RingRadius, 0f, troops);
                Arc(_gaugeMoraleBack, center, RingRadius - 0.32f, 0f, 1f);
                Arc(_gaugeMorale, center + Vector3.up * 0.01f, RingRadius - 0.32f, 0f, morale);
            }
            // 다른 우리 군단: 가는 병력 고리 (남음 초록 · 잃음 붉은)
            foreach (var c in ctx.Corps)
            {
                bool show = c.InBattle && c.Team == _ctl.Team && c != sel && !Far;
                if (!_allyGauges.TryGetValue(c, out var g))
                {
                    if (!show) continue;
                    g = _allyGauges[c] = (Line("AllyLost " + c.Id, GaugeLost, 0.1f, false, true, overlay: true), Line("AllyTroops " + c.Id, GaugeTroops, 0.1f, false, true, overlay: true));
                }
                g.lost.gameObject.SetActive(show);
                g.troops.gameObject.SetActive(show);
                if (!show) continue;
                var center = ToWorld(c.Anchor) + new Vector3(0f, 0.06f, 0f);
                float t = c.StartTroops > 0 ? Mathf.Clamp01((float)c.Troops / c.StartTroops) : 0f;
                Arc(g.lost, center, RingRadius * 0.85f, 0f, 1f);
                Arc(g.troops, center + Vector3.up * 0.01f, RingRadius * 0.85f, 0f, t);
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
            _path.positionCount = 0; // 실선 대신 점선 조각들
            Dashes(_pathPoints);
            PathEnd = _pathPoints.Count >= 2 ? _pathPoints[_pathPoints.Count - 1] : (Vector3?)null;

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
            bool ink = Game.Unity.UI.UiStyle.Paper; // 수묵 스타일: 전장 밖·하늘도 한지색
            Color sun = Color.white, sky = ink ? new Color(0.91f, 0.87f, 0.80f) : new Color(0.08f, 0.09f, 0.10f), ambient = ink ? new Color(0.66f, 0.66f, 0.62f) : new Color(0.55f, 0.55f, 0.58f);
            float intensity = 1.1f, pitch = 55f;
            if (time == "dusk") { sun = new Color(1f, 0.72f, 0.45f); intensity = 0.8f; pitch = 18f; sky = ink ? new Color(0.80f, 0.68f, 0.58f) : new Color(0.20f, 0.12f, 0.10f); ambient = ink ? new Color(0.58f, 0.54f, 0.48f) : new Color(0.45f, 0.36f, 0.32f); } // 수묵: 저녁에도 땅이 보랏빛으로 죽지 않게
            if (time == "night") { sun = new Color(0.55f, 0.65f, 0.95f); intensity = 0.35f; pitch = 40f; sky = ink ? new Color(0.30f, 0.31f, 0.36f) : new Color(0.02f, 0.03f, 0.06f); ambient = new Color(0.16f, 0.18f, 0.26f); }
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
            // 목책 (R-83): 둘레에 나무 말뚝. 내 것이거나 보이는 곳이면 보인다. 세우는 중이면 낮게
            foreach (var pal in ctx.Palisades)
            {
                if (pal.Team != _ctl.Team && !ctx.IsPointVisibleTo(_ctl.Team, pal.Point)) continue;
                alive.Add(pal);
                if (!_stratagemMarks.TryGetValue(pal, out var root))
                {
                    root = new GameObject("Palisade " + pal.Id).transform;
                    root.SetParent(_root.transform, false);
                    float r = (float)pal.Radius.ToDisplayDouble();
                    const int posts = 12;
                    for (int i = 0; i < posts; i++)
                    {
                        float a = i * Mathf.PI * 2f / posts;
                        Part(root, _cylinder, _decoyPoleMat, new Vector3(Mathf.Cos(a) * r, 0.25f, Mathf.Sin(a) * r), new Vector3(0.12f, 0.25f, 0.12f));
                    }
                    _stratagemMarks[pal] = root;
                }
                root.position = ToWorld(pal.Point);
                root.localScale = new Vector3(1f, pal.Ready(ctx) ? 1f : 0.4f, 1f);
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

        /// <summary>소부대 화면 대열의 반 폭 · 반 깊이 (출진 때 병사 수로 고정, 바닥 테두리 여백 포함).</summary>
        private (float halfW, float halfD) Extent(SubUnitState u)
        {
            long n0 = System.Math.Max(1, SoldierRatio.VisibleSoldiers(u.StartTroops, _troopsPerSoldier));
            int side = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(n0)));
            int rows0 = (int)((n0 + side - 1) / side);
            return ((side - 1) * SoldierSpacing / 2f + OutlineMargin, (rows0 - 1) * SoldierSpacing / 2f + OutlineMargin);
        }

        /// <summary>
        /// 화면용 방향 · 위치 (PR C, 2026-11-20 사용자 녹화: 덜덜거림).
        /// ① 군단 방향은 규칙 값 쪽으로 초당 최대 180°만 돈다 (1.5° 안의 작은 변화는 무시) — 틱마다 덩어리가 통째로 튀지 않게.
        /// ② 소부대 위치는 틱 사이 직선 위치를 0.15초로 따라간다 (칸 중심을 지나는 꺾임 없앰), 2 이상 떨어지면 바로 그 자리로.
        /// ③ 같은 군단 소부대의 바닥 테두리가 겹치지 않게 서로 밀고, 맞붙은 적과는 앞줄끼리 0.3 띄워 경계선을 만든다 (H2).
        /// 규칙(src/, R-92) 계산은 그대로 — 그리는 자리만.
        /// </summary>
        private void UpdateDisplay(BattleContext ctx, float alpha, float dt)
        {
            foreach (var c in ctx.Corps)
            {
                if (!c.InBattle) continue;
                var facing = new Vector3((float)c.Facing.X.ToDisplayDouble(), 0f, -(float)c.Facing.Y.ToDisplayDouble());
                bool had = _dispYaw.TryGetValue(c, out float yaw);
                if (facing.sqrMagnitude > 1e-6f)
                {
                    float target = Quaternion.LookRotation(facing).eulerAngles.y;
                    if (!had) yaw = target;
                    else if (Mathf.Abs(Mathf.DeltaAngle(yaw, target)) > TurnDeadDeg) yaw = Mathf.MoveTowardsAngle(yaw, target, TurnDegPerSec * dt);
                }
                _dispYaw[c] = yaw;
                _dispRot[c] = Quaternion.Euler(0f, yaw, 0f);
                foreach (var u in c.Units)
                {
                    if (!u.Alive) continue;
                    var (prev, cur) = _unitPos.TryGetValue(u, out var pc) ? pc : (ToWorld(u.Position), ToWorld(u.Position));
                    var target = Vector3.Lerp(prev, cur, alpha);
                    if (!_disp.TryGetValue(u, out var d) || (d.pos - target).sqrMagnitude >= SnapDist * SnapDist) { _disp[u] = (target, Vector3.zero); continue; }
                    var vel = d.vel;
                    var pos = Vector3.SmoothDamp(d.pos, target, ref vel, PosSmooth, Mathf.Infinity, dt);
                    _disp[u] = (pos, vel);
                }
            }
            // 같은 군단 소부대끼리: 바닥 테두리(군단 방향 사각형)가 겹치면 덜 겹친 축으로 반씩 민다 (두 번 돌림)
            for (int pass = 0; pass < 2; pass++)
                foreach (var c in ctx.Corps)
                {
                    if (!c.InBattle || !_dispRot.TryGetValue(c, out var rot)) continue;
                    var inv = Quaternion.Inverse(rot);
                    for (int i = 0; i < c.Units.Count; i++)
                    {
                        var a = c.Units[i];
                        if (!a.Alive || !_disp.TryGetValue(a, out var da)) continue;
                        var ea = Extent(a);
                        for (int j = i + 1; j < c.Units.Count; j++)
                        {
                            var b = c.Units[j];
                            if (!b.Alive || !_disp.TryGetValue(b, out var db)) continue;
                            var eb = Extent(b);
                            var local = inv * (db.pos - da.pos);
                            float ox = ea.halfW + eb.halfW - Mathf.Abs(local.x), oz = ea.halfD + eb.halfD - Mathf.Abs(local.z);
                            if (ox <= 0f || oz <= 0f) continue;
                            Vector3 push = ox < oz ? new Vector3((local.x >= 0f ? 1f : -1f) * ox / 2f, 0f, 0f) : new Vector3(0f, 0f, (local.z >= 0f ? 1f : -1f) * oz / 2f);
                            var w = rot * push;
                            _disp[a] = (da.pos - w, da.vel);
                            _disp[b] = (db.pos + w, db.vel);
                            da = _disp[a];
                        }
                    }
                }
            // 맞붙은 적 소부대: 앞줄끼리 EngageGap만큼 띄운다 (가운데 거리 ≥ 두 반 깊이 + 간격), 모자란 만큼 반씩 뒤로
            _engagedPairs.Clear();
            foreach (var c in ctx.Corps)
            {
                if (!c.InBattle) continue;
                foreach (var u in c.Units)
                {
                    var t = u.AttackTarget;
                    if (!u.Alive || t == null || !t.Alive || !u.Troop.HasTag("melee")) continue;
                    if (!_disp.TryGetValue(u, out var du) || !_disp.TryGetValue(t, out var dt2)) continue;
                    long key = u.GetHashCode() < t.GetHashCode() ? ((long)u.GetHashCode() << 32) ^ (uint)t.GetHashCode() : ((long)t.GetHashCode() << 32) ^ (uint)u.GetHashCode();
                    if (!_engagedPairs.Add(key)) continue;
                    var dir = dt2.pos - du.pos;
                    dir.y = 0f;
                    float dist = dir.magnitude;
                    if (dist < 1e-3f) continue;
                    float need = Extent(u).halfD + Extent(t).halfD - 2f * OutlineMargin + EngageGap; // 앞줄(병사)끼리 간격 — 테두리 여백은 빼고
                    if (dist >= need) continue;
                    var w = dir / dist * ((need - dist) / 2f);
                    _disp[u] = (du.pos - w, du.vel);
                    _disp[t] = (dt2.pos + w, dt2.vel);
                }
            }
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
            _outlineFrame++;
            UpdateDisplay(ctx, alpha, Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            foreach (var c in ctx.Corps)
            {
                if (!c.InBattle || !_ctl.Sees(c)) continue;
                // 고른 군단도 병사는 편 색 그대로 (투구 색으로 편 구분), 고름 표시는 노란 바닥 테두리 · 링 (H1 2026-11-20)
                int teamColor = c.Team == _ctl.Team ? Ally : Enemy;
                int outlineColor = c == _ctl.Selected ? Selected : teamColor;
                // 몸 상태 (D18): 사기가 퇴각 기준 + 15 밑이면 바랜 색 (떨림은 PR C에서 없앰), 숨은 아군은 어둡고 성기게
                bool shaken = c.Temperament != null && c.Morale <= c.Temperament.RetreatMorale + Fix.FromInt(15);
                bool hidden = c.Team == _ctl.Team && (c.Hidden || c.HasStatus(StatusIds.AmbushSetup));
                if (c != _ctl.Selected && shaken) teamColor = c.Team == _ctl.Team ? AllyShaken : EnemyShaken;
                if (hidden) teamColor = HiddenTone;
                var rot = _dispRot.TryGetValue(c, out var dr) ? dr : Quaternion.identity; // 화면용 방향 (천천히 돎)
                foreach (var u in c.Units)
                {
                    // 대열 (2026-11-14): 폭은 출진 때 병사 수로 고정, 앞줄 = 적 쪽. 병력이 줄면 앞줄에서 쓰러지고 뒷줄이 메워 깊이가 준다
                    long n0 = System.Math.Max(1, SoldierRatio.VisibleSoldiers(u.StartTroops, _troopsPerSoldier));
                    int side = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(n0)));
                    int rows0 = (int)((n0 + side - 1) / side);
                    float halfW = (side - 1) * SoldierSpacing / 2f;
                    float frontZ = (rows0 - 1) * SoldierSpacing / 2f;
                    int seedBase = u.GetHashCode();
                    if (!u.Alive)
                    {
                        // 방금 전멸한 부대: 남아 있던 화면 병사가 모두 쓰러진다
                        if (_shownSoldiers.TryGetValue(u, out var was) && was > 0 && _disp.TryGetValue(u, out var lastPos))
                            for (int i = 0; i < was; i++)
                                AddDying(lastPos.pos + rot * (new Vector3(i % side * SoldierSpacing - halfW, 0f, frontZ - i / side * SoldierSpacing) + Jitter(seedBase + i)), rot, SoldierKey(u), teamColor, seedBase + i, clock);
                        _shownSoldiers[u] = 0;
                        continue;
                    }
                    var batch = SoldierBatch(SoldierKey(u), u.Confused ? Confused : teamColor);
                    bool isCube = batch.Mesh == _cube;
                    long n = SoldierRatio.VisibleSoldiers(u.Troops, _troopsPerSoldier);
                    var (prev, cur) = _unitPos.TryGetValue(u, out var pc) ? pc : (ToWorld(u.Position), ToWorld(u.Position));
                    var center = _disp.TryGetValue(u, out var dp) ? dp.pos : Vector3.Lerp(prev, cur, alpha); // 화면용 위치 (부드럽게 · 밀어냄)
                    bool walking = (cur - prev).sqrMagnitude > 1e-5f;
                    // 근접 공격: 대상 쪽으로 찌르기 (병사마다 박자를 어긋나게). 앞줄일수록 크게
                    // 공격 방향 (맞붙은 적 쪽, 원거리는 군단 앞) · 병종별 동작 (H3)
                    var style = StyleOf(u);
                    Vector3 toward = Vector3.zero;
                    bool melee = u.AttackTarget != null && style != AttackStyle.Ranged && style != AttackStyle.None
                        && _disp.TryGetValue(u.AttackTarget, out var tdisp) && (toward = tdisp.pos - center) != Vector3.zero;
                    if (melee) { toward.y = 0f; toward = toward.sqrMagnitude > 1e-6f ? toward.normalized : Vector3.zero; }
                    if (melee) { if (!_meleeSince.ContainsKey(u)) _meleeSince[u] = clock; } else _meleeSince.Remove(u);
                    float chargeAge = melee && _meleeSince.TryGetValue(u, out var since) ? clock - since : -1f;
                    float shotAge = style == AttackStyle.Ranged && u.AttackTarget != null ? clock - Effects.LastShot(u) : -1f;
                    var forward = rot * Vector3.forward;
                    // 병력이 줄어 화면 병사가 빠지면 앞줄 자리에서 쓰러진다 (쓰러지는 동작 → 시체). 뒷줄은 앞으로 메운다
                    if (_shownSoldiers.TryGetValue(u, out var before) && before > n)
                    {
                        for (long k = n; k < before; k++)
                        {
                            int s = seedBase + (int)k * 31 + _flashFrame;
                            int col = (int)((uint)(s * 2654435761u) % (uint)side);
                            var local = new Vector3(col * SoldierSpacing - halfW, 0f, frontZ + 0.05f) + Jitter(s);
                            AddDying(center + rot * local, rot, SoldierKey(u), teamColor, s, clock);
                        }
                    }
                    _shownSoldiers[u] = n;
                    int rowsNow = (int)((n + side - 1) / side);
                    if (!hidden && n > 0)
                        DrawUnitOutline(u, outlineColor, center, rot, rowsNow > 1 ? halfW : (n - 1) * SoldierSpacing / 2f, frontZ, frontZ - (rowsNow - 1) * SoldierSpacing);
                    for (int i = 0; i < n; i++)
                    {
                        int row = i / side, col = i % side;
                        // 맨 뒷줄이 덜 찼으면 가운데로 모은다
                        float rowOffset = row == rowsNow - 1 ? (side - (int)(n - row * side)) * SoldierSpacing / 2f : 0f;
                        var local = new Vector3(col * SoldierSpacing - halfW + rowOffset, 0f, frontZ - row * SoldierSpacing) + Jitter(seedBase + i);
                        var p = center + rot * local;
                        float phase = i * 1.7f + seedBase % 7;
                        float frontWeight = row == 0 ? 1f : row == 1 ? 0.5f : 0.15f; // 공격 동작은 앞줄 위주
                        float swingYaw = 0f;
                        switch (style)
                        {
                            case AttackStyle.Spear when melee: // 앞으로 길게 찌르기, 느린 박자 (부드럽게: 0~1 사이 반파 제곱)
                                {
                                    float s = Mathf.Max(0f, Mathf.Sin(clock * SpearBeat + phase));
                                    p += toward * (SpearReach * frontWeight * s * s);
                                    break;
                                }
                            case AttackStyle.Sword when melee: // 제자리에서 좌우로 휘두르듯 몸 돌리기
                                swingYaw = SwordSwingDeg * frontWeight * Mathf.Sin(clock * SwordBeat + phase);
                                p += toward * (SwordStep * frontWeight * Mathf.Max(0f, Mathf.Sin(clock * SwordBeat * 2f + phase)));
                                break;
                            case AttackStyle.Horse when melee && chargeAge >= 0f && chargeAge < HorseChargeTime: // 맞붙는 순간 짧게 돌진 → 돌아옴
                                {
                                    float t = chargeAge / HorseChargeTime + (phase % 1f) * 0.08f;
                                    if (t < 1f) p += toward * (HorseCharge * (row <= 1 ? 1f : 0.6f) * Mathf.Sin(Mathf.PI * t));
                                    break;
                                }
                            case AttackStyle.Ranged when shotAge >= 0f: // 쏘는 순간 뒤로 살짝, 병사마다 조금 늦게
                                {
                                    float a = shotAge - (phase % 1f) * 0.06f;
                                    if (a >= 0f && a < RangedRecoilTime) { float k = 1f - a / RangedRecoilTime; p -= forward * (RangedRecoil * k * k); }
                                    break;
                                }
                        }
                        // 2026-11-20 사용자 (참고 영상): 통통 튀지 말고 미끄러지듯 — 위아래 튀기 없이 걸을 때 몸만 앞으로 살짝 기울인다
                        if (hidden && i % 2 == 1) continue; // 숨은 군단: 반만 그려 흐릿하게
                        if (u.Confused) p += new Vector3(Mathf.Sin(clock * 23f + phase) * 0.07f, 0f, Mathf.Cos(clock * 19f + phase * 1.3f) * 0.07f); // 혼란: 갈팡질팡
                        // 사기 바닥은 떨지 않고 색만 바랜다 (PR C: 빠른 흔들림 없앰)
                        // 맞으면 앞줄이 뒤로 살짝 밀린다 (H3 #254). 번갈아 깜빡이던 것은 없앰 — 죽는 병사(PR D)가 대신
                        if (c.LostThisTick > 0 && row <= 1 && !isCube) p -= rot * new Vector3(0f, 0f, 0.04f);
                        p.y = GroundHeight(p.x, p.z);
                        var look = swingYaw != 0f ? rot * Quaternion.Euler(0f, swingYaw, 0f) : rot;
                        batch.Add(p, walking ? look * WalkLean : look);
                    }
                }
                // 장수: 군단 한가운데 조금 뒤, 부대원보다 크게 (총대장은 군기)
                Vector3 sum = Vector3.zero;
                int alive = 0;
                foreach (var u in c.Units)
                    if (u.Alive && _disp.TryGetValue(u, out var up)) { sum += up.pos; alive++; }
                if (alive > 0)
                {
                    var gp = sum / alive + rot * new Vector3(0f, 0f, -0.9f);
                    gp.y = GroundHeight(gp.x, gp.z);
                    var gs = c.Setup.General;
                    string mount = gs == null ? Game.Data.Defs.MountRules.Horse : ctx.Defs.Mounts.Resolve(ctx.Defs, gs.Id, gs.Might, gs.Intellect);
                    foreach (var gb in GeneralBatches(c.Setup.Role == Game.Battle.Model.Simulation.CorpsRoles.Commander, teamColor, mount)) gb.Add(gp, rot);
                }
            }
            HideStaleOutlines();
            DrawDying(clock);
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
        private static Material _unlitTemplate;
        private readonly Dictionary<Color, Material> _unlitMats = new Dictionary<Color, Material>();

        /// <summary>빛을 받지 않는 재질 (Resources/Materials/GameUnlit = URP Unlit). 없으면 보통 재질.</summary>
        private Material UnlitMaterial(Color color)
        {
            if (_unlitMats.TryGetValue(color, out var m)) return m;
            if (_unlitTemplate == null) _unlitTemplate = Resources.Load<Material>("Materials/GameUnlit");
            if (_unlitTemplate == null) return LineMaterial(color);
            m = Own(new Material(_unlitTemplate));
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            _unlitMats[color] = m;
            return m;
        }

        private readonly Dictionary<Color, Material> _overlayMats = new Dictionary<Color, Material>();

        /// <summary>
        /// 병사 위에 늘 보이는 표시 재질 (PR C: 고른 군단 링 · 게이지가 병사에 가리던 것). Unlit 복사본에 Overlay 큐 + 깊이 검사 끔.
        /// GameUnlit.mat 파일은 그대로 (복사본만 바꿈).
        /// </summary>
        private Material OverlayMaterial(Color color)
        {
            if (_overlayMats.TryGetValue(color, out var m)) return m;
            m = Own(new Material(UnlitMaterial(color)));
            m.renderQueue = (int)RenderQueue.Overlay;
            if (m.HasProperty("_ZTest")) m.SetFloat("_ZTest", (float)CompareFunction.Always);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            _overlayMats[color] = m;
            return m;
        }

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

        /// <summary>호: 위(12시)에서 시계 방향으로 전체의 frac만큼 (0이면 숨김).</summary>
        private static void Arc(LineRenderer line, Vector3 center, float r, float from, float frac)
        {
            frac = Mathf.Clamp01(frac);
            int n = Mathf.Max(2, Mathf.CeilToInt(RingSegments * frac) + 1);
            if (frac <= 0.001f) { line.positionCount = 0; return; }
            line.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.PI * 0.5f - (from + frac * i / (n - 1)) * Mathf.PI * 2f;
                line.SetPosition(i, center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
        }

        /// <summary>꺾은선을 같은 길이 점선 조각으로 (조각마다 선 하나, 모자라면 더 만든다).</summary>
        private void Dashes(List<Vector3> pts)
        {
            int used = 0;
            float carry = 0f; // 앞 꼭짓점에서 넘어온 자리 (조각+틈 주기 안의 위치)
            for (int i = 1; i < pts.Count; i++)
            {
                Vector3 a = pts[i - 1], b = pts[i];
                float len = Vector3.Distance(a, b);
                float s = 0f;
                while (s < len)
                {
                    float period = DashLen + DashGap;
                    float phase = carry % period;
                    if (phase < DashLen)
                    {
                        float d = Mathf.Min(DashLen - phase, len - s);
                        if (used >= _dashes.Count) _dashes.Add(Line("Dash", DashColor, 0.16f, false, true));
                        var line = _dashes[used++];
                        line.gameObject.SetActive(true);
                        line.positionCount = 2;
                        line.SetPosition(0, Vector3.Lerp(a, b, s / len));
                        line.SetPosition(1, Vector3.Lerp(a, b, (s + d) / len));
                        s += d; carry += d;
                    }
                    else
                    {
                        float d = Mathf.Min(period - phase, len - s);
                        s += d; carry += d;
                    }
                    if (used > 400) break; // 아주 긴 길 막기
                }
            }
            for (int i = used; i < _dashes.Count; i++) _dashes[i].gameObject.SetActive(false);
        }

        private Material LineMaterial(Color color)
        {
            if (!_lineMats.TryGetValue(color, out var mat)) _lineMats[color] = mat = Own(NewMaterial(color));
            return mat;
        }

        /// <param name="unlit">빛 영향 없이 늘 같은 밝기 (표시용 게이지 · 경로, 저녁 · 밤에도 선명하게).</param>
        /// <param name="overlay">병사에 가리지 않게 늘 위에 (고른 군단 링 · 게이지, PR C).</param>
        private LineRenderer Line(string name, Color color, float width, bool loop, bool unlit = false, bool overlay = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var line = go.AddComponent<LineRenderer>();
            Material mat;
            if (overlay) mat = OverlayMaterial(color);
            else if (unlit) mat = UnlitMaterial(color);
            else if (!_lineMats.TryGetValue(color, out mat)) _lineMats[color] = mat = Own(NewMaterial(color));
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
