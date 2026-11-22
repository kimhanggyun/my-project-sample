using System.Collections.Generic;
using Game.Battle.Model.Crowd;
using Game.Battle.Model.Geometry;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Unity.Battle3D
{
    /// <summary>
    /// 샘플전투2 그리기 (R-125, 병사 단위 전투): 규칙(CrowdBattle)의 병사 하나 = 화면 병사 하나. 격자 없음.
    /// 위치는 틱 사이를 이어 그리고, 방향은 화면에서만 부드럽게 돈다. 공격 = 대상 쪽으로 찌르며 몸을 비틀어 무기를 휘두름,
    /// 쓰러짐 = 하얗게 번쩍 → 넘어짐 → 땅으로 가라앉아 사라짐. 병사는 몸 전체가 편 색 (2026-11-21 사용자: 디자인 바꿔도 됨 — 멀리서도 파랑 · 빨강).
    /// </summary>
    public sealed class CrowdField3D
    {
        public BattleCameraRig Rig { get; }
        private readonly CrowdBattle _battle;
        private readonly GameObject _root;
        private readonly List<Object> _owned = new List<Object>();
        private readonly float _width, _depth;
        private Vector3[] _prev = new Vector3[0], _cur = new Vector3[0];
        private float[] _yaw = new float[0];
        private int _seenTick = -1;

        private static readonly Color AllyColor = new Color(0.22f, 0.42f, 0.86f);
        private static readonly Color AllyDark = new Color(0.14f, 0.24f, 0.52f);
        private static readonly Color EnemyColor = new Color(0.86f, 0.22f, 0.18f);
        private static readonly Color EnemyDark = new Color(0.50f, 0.13f, 0.11f);
        private static readonly Color DeadWhite = new Color(0.97f, 0.97f, 0.94f);
        private readonly InstanceBatch _allyGeneral, _enemyGeneral, _allyLegs, _enemyLegs, _allyCavLegs, _enemyCavLegs, _arrowBatch;
        /// <summary>병종(1단계 뿌리) · 편 · 모양(0 다리 뺀 몸, 1 통째, 2 하얀 통째)마다 묶음 (R-127).</summary>
        private readonly Dictionary<(string troop, int team, int kind), InstanceBatch> _troopBatches = new Dictionary<(string, int, int), InstanceBatch>();
        private readonly List<InstanceBatch> _allBatches = new List<InstanceBatch>();
        private const float CavStride = 0.7f, CavSwingDeg = 40f;
        private static readonly Color ArrowColor = new Color(0.86f, 0.74f, 0.52f);
        /// <summary>장수 말 다리 (2026-11-21 사용자 "말이 발을 안 움직임"): 달리면 앞다리 둘 · 뒷다리 둘이 번갈아 뻗는다.</summary>
        private readonly InstanceBatch _allyHorseLegs, _enemyHorseLegs;
        private const float HorseStride = 0.9f, HorseSwingDeg = 38f;
        // ---- 부딪침 (R-126): 흙먼지 · 화면 흔들림 ----
        private const int MaxDust = 260;
        private const float DustSeconds = 0.7f;
        private static readonly Color DustColor = new Color(0.80f, 0.71f, 0.55f);
        private readonly List<(Vector3 pos, int tick, int seed)> _dust = new List<(Vector3, int, int)>();
        private readonly InstanceBatch _dustBatch;
        private float _shake;

        // ---- 고른 부대 표시 (R-126 조작): 노란 고리, 이동 목표 · 공격 대상 고리 ----
        /// <summary>화면이 고른 우리 부대 (Armies 번호, 없으면 -1).</summary>
        public int SelectedArmy = -1;
        /// <summary>true면 장수가 살아 있는 우리 부대 모두 고른 표시 ([전체], 2026-11-21 사용자).</summary>
        public bool SelectAll;
        /// <summary>고리는 부대마다 하나씩 (고른 표시 · 목표 표시), 모자라면 더 만든다.</summary>
        private readonly List<LineRenderer> _selRings = new List<LineRenderer>(), _goalRings = new List<LineRenderer>(), _fxRings = new List<LineRenderer>();
        private const int RingSegments = 48;
        /// <summary>그리는 중인 길 (월드, 화면이 채움 — R-132 경로 그리기).</summary>
        public readonly List<Vector3> Trail = new List<Vector3>();
        /// <summary>길 선 (그리는 중 자취 · 고른 부대의 남은 이동 길).</summary>
        private readonly List<LineRenderer> _pathLines = new List<LineRenderer>();
        private readonly List<Vector3> _pts = new List<Vector3>();

        /// <summary>병사마다 걸은 거리 (다리 박자) · 다리 흔들기 세기 (걷기 시작 · 멈출 때 부드럽게).</summary>
        private float[] _stride = new float[0], _legAmp = new float[0];
        /// <summary>한 걸음 주기 동안 가는 거리 (월드). 짧을수록 종종걸음.</summary>
        private const float StrideLength = 0.55f;
        private const float LegSwingDeg = 32f;

        private const float FlashSeconds = 0.15f, FallSeconds = 0.35f, SinkSeconds = 0.45f;
        private const float TurnDegPerSec = 360f;
        /// <summary>병사 모형 크기 (자리 간격 0.45에 맞춰 촘촘한 무리로 보이게).</summary>
        private const float ModelScale = 1.6f;
        private const float GeneralModelScale = 1.0f;

        public CrowdField3D(CrowdBattle battle)
        {
            _battle = battle;
            _width = (float)battle.Rules.FieldWidth.ToDisplayDouble();
            _depth = (float)battle.Rules.FieldDepth.ToDisplayDouble();
            _root = new GameObject("CrowdField3D");

            var camGo = new GameObject("CrowdCamera");
            camGo.transform.SetParent(_root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.72f, 0.52f);
            cam.depth = 1;
            cam.cullingMask = ~(1 << 5);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 300f;
            cam.fieldOfView = 40f;
            Rig = new BattleCameraRig(cam, _width, _depth);
            Rig.SetDistance(36f); // 처음엔 양쪽 군이 다 보이게 (캡처 2026-11-21: 우리 쐐기가 아래로 잘림)
            Rig.LookAt(new Vector3(_width / 2f, 0f, _depth * 0.45f));
            if (battle.Terrain != null) Rig.LookAt(new Vector3(_width / 2f, 0f, (float)battle.Commander(CrowdBattle.Ally).Center.Y.ToDisplayDouble() + 12f)); // 지형 맵 (R-133): 우리 쪽부터

            var lightGo = new GameObject("Sun");
            lightGo.transform.SetParent(_root.transform, false);
            lightGo.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;

            // 바닥 (R-133): 지형 맵이면 높낮이 · 물 · 다리 · 나무, 아니면 풀색 판 (CrowdGround)
            // (기본 도형 Quad는 물리 충돌 부품이 딸려 와, 물리를 뺀 폰 빌드에서 "Can't add component … MeshCollider" 오류 — 2026-11-21 사용자 폰)
            Ground = new CrowdGround(_root.transform, battle, NewMaterial);
            if (battle.Terrain != null) cam.backgroundColor = new Color(0.66f, 0.74f, 0.80f); // 하늘빛 (먼 가장자리)

            // 살아 있는 병사 = 다리 없는 몸 + 따로 흔드는 다리 두 짝, 쓰러지는 병사 = 다리 붙은 통째
            // 병종 모형은 처음 쓸 때 만든다 (TroopBatch)
            var dm = Own(NewMaterial(DustColor));
            dm.enableInstancing = true;
            _dustBatch = new InstanceBatch(Resources.GetBuiltinResource<Mesh>("Sphere.fbx"), new[] { dm }, Matrix4x4.identity);
            var leg = SoldierMeshes.Leg();
            _allyLegs = LegBatch(leg, AllyDark);
            _enemyLegs = LegBatch(leg, EnemyDark);
            var cavLeg = SoldierMeshes.CavalryLeg();
            _allyCavLegs = LegBatch(cavLeg, AllyDark);
            _enemyCavLegs = LegBatch(cavLeg, EnemyDark);
            var am = Own(NewMaterial(ArrowColor));
            am.enableInstancing = true;
            _arrowBatch = new InstanceBatch(Resources.GetBuiltinResource<Mesh>("Cube.fbx"), new[] { am }, Matrix4x4.identity);
            var general = SoldierMeshes.General(true, horseLegs: false);
            var horseLeg = SoldierMeshes.HorseLeg();
            _allyHorseLegs = LegBatch(horseLeg, AllyDark);
            _enemyHorseLegs = LegBatch(horseLeg, EnemyDark);
            _allyGeneral = Batch(general, AllyColor, AllyDark, GeneralModelScale); // 장수 모형은 이미 1.8배라 병사만큼 더 키우지 않음 (캡처: 장수가 대열을 가림)
            _enemyGeneral = Batch(general, EnemyColor, EnemyDark, GeneralModelScale);
            // 모든 묶음이 만들어진 뒤에 목록으로 (2026-11-21 캡처 오류: 말 다리 · 장수 묶음이 아직 없을 때 넣어 null)
            foreach (var b in new[] { _allyGeneral, _enemyGeneral, _allyLegs, _enemyLegs, _allyCavLegs, _enemyCavLegs, _arrowBatch, _allyHorseLegs, _enemyHorseLegs }) _allBatches.Add(b);
        }

        /// <summary>규칙 (x, y) → 월드 (x, 0, y): 우리 편(작은 y)이 화면 아래, 적이 위.</summary>
        public Vector3 ToWorld(Vec p)
        {
            float x = (float)p.X.ToDisplayDouble(), z = (float)p.Y.ToDisplayDouble();
            return new Vector3(x, Ground.StandHeight(x, z), z);
        }

        // ---- 성문 (R-140): 닫힌 동안 나무 문, 맞을수록 낮아지고 (금 간 모습), 부서지면 사라짐. 맞는 순간 살짝 흔들림 ----
        private InstanceBatch _gateBatch;

        private void DrawGates(Camera cam)
        {
            if (_battle.Gates.Count == 0 || _battle.Terrain == null) return;
            if (_gateBatch == null)
            {
                var m = Own(NewMaterial(new Color(0.42f, 0.28f, 0.16f)));
                m.enableInstancing = true;
                _gateBatch = new InstanceBatch(Resources.GetBuiltinResource<Mesh>("Cube.fbx"), new[] { m }, Matrix4x4.identity);
            }
            _gateBatch.Matrices.Clear();
            float cell = (float)_battle.Terrain.CellSize.ToDisplayDouble();
            int w = _battle.Terrain.Map.Width;
            float tickSec = (float)_battle.Rules.TickSeconds.ToDisplayDouble();
            foreach (var g in _battle.Gates)
            {
                if (g.Open) continue;
                float ratio = Mathf.Clamp01((float)g.Hp / Mathf.Max(1, g.MaxHp));
                float h = 0.6f + 2.6f * ratio;
                float shake = g.LastHitTick >= 0 && _battle.Tick - g.LastHitTick < 2 ? 0.04f * Mathf.Sin(Time.time * 60f) : 0f;
                foreach (var c in g.Cells)
                {
                    var p = ToWorld(_battle.Terrain.CellCenter(c % w, c / w));
                    _gateBatch.Matrices.Add(Matrix4x4.TRS(new Vector3(p.x + shake, p.y + h / 2f, p.z), Quaternion.identity, new Vector3(cell + 0.04f, h, cell * 0.7f)));
                }
            }
            _gateBatch.Draw(cam);
        }

        /// <summary>땅 (높낮이 · 물 · 다리 · 소품, R-133).</summary>
        public CrowdGround Ground { get; }

        /// <summary>화면 위치 → 땅 위 월드 자리 (높은 땅은 그 높이 평면과 다시 만나 보며 맞춘다).</summary>
        public bool ScreenToGround(Vector2 screen, out Vector3 world)
        {
            if (!Rig.Ground(screen, 0f, out world)) return false;
            for (int k = 0; k < 4; k++)
                if (!Rig.Ground(screen, Ground.StandHeight(world.x, world.z), out world)) return false;
            return true;
        }

        public void SetViewport(Rect pixels)
        {
            if (pixels.width <= 1 || pixels.height <= 1 || Rig.Camera.pixelRect == pixels) return;
            Rig.Camera.pixelRect = pixels;
            Rig.Refresh();
        }

        /// <summary>틱을 돌린 뒤: 위치를 한 칸 넘긴다 (화면은 그 사이를 잇는다).</summary>
        private void SyncTick()
        {
            int n = _battle.Soldiers.Count;
            if (_cur.Length < n)
            {
                System.Array.Resize(ref _prev, n);
                System.Array.Resize(ref _cur, n);
                System.Array.Resize(ref _yaw, n);
                System.Array.Resize(ref _stride, n);
                System.Array.Resize(ref _legAmp, n);
                for (int i = 0; i < n; i++)
                {
                    _cur[i] = _prev[i] = ToWorld(_battle.Soldiers[i].Pos);
                    var f = _battle.Armies[_battle.Soldiers[i].Army].Facing;
                    _yaw[i] = Mathf.Atan2((float)f.X.ToDisplayDouble(), (float)f.Y.ToDisplayDouble()) * Mathf.Rad2Deg;
                }
            }
            if (_seenTick == _battle.Tick) return;
            for (int i = 0; i < n; i++)
            {
                _prev[i] = _cur[i];
                _cur[i] = ToWorld(_battle.Soldiers[i].Pos);
                _stride[i] += (_cur[i] - _prev[i]).magnitude; // 다리 박자는 틱마다 간 거리로 (화면 시간과 무관 — 캡처 · 배속에서도 같음)
                var s = _battle.Soldiers[i];
                if (s.ImpactTick == _battle.Tick && _dust.Count < MaxDust)
                    _dust.Add((_cur[i] + new Vector3(((i * 37) % 7 - 3) * 0.04f, 0f, ((i * 53) % 7 - 3) * 0.04f), _battle.Tick, (i * 13) % 5));
            }
            if (_battle.ImpactsThisTick >= 3) _shake = Mathf.Min(0.14f, _shake + 0.012f * _battle.ImpactsThisTick);
            _seenTick = _battle.Tick;
        }

        /// <param name="alpha">직전 틱 → 지금 틱 사이 (0~1).</param>
        /// <param name="dt">이번 프레임 화면 시간 (방향 돌리기).</param>
        public void Draw(float alpha, float dt)
        {
            SyncTick();
            _lastAlpha = alpha;
            float tickSec = (float)_battle.Rules.TickSeconds.ToDisplayDouble();
            float now = (_battle.Tick + alpha) * tickSec;
            foreach (var b in _allBatches) b.Matrices.Clear();
            var soldiers = _battle.Soldiers;
            for (int i = 0; i < soldiers.Count; i++)
            {
                var s = soldiers[i];
                var p = Vector3.Lerp(_prev[i], _cur[i], alpha);
                bool ally = s.Team == CrowdBattle.Ally;
                if (!s.Alive)
                {
                    if (s.DiedTick < 0) continue; // 도망쳐 전장 밖으로 나감 (R-128) — 그리지 않음
                    float age = now - s.DiedTick * tickSec;
                    if (age > FallSeconds + SinkSeconds) continue;
                    float fall = Mathf.Clamp01(age / FallSeconds);
                    float sink = Mathf.Clamp01((age - FallSeconds) / SinkSeconds);
                    var back = Quaternion.Euler(0f, _yaw[i], 0f);
                    p += back * Vector3.back * (0.18f * (1f - (1f - fall) * (1f - fall)));
                    p.y = Ground.StandHeight(p.x, p.z) + 0.06f * Mathf.Sin(Mathf.PI * fall) - 0.35f * sink * sink;
                    var rot = back * Quaternion.Euler(-80f * fall * fall, 0f, 0f);
                    (s.IsGeneral ? (age < FlashSeconds ? TroopBatch("infantry", 2, 2) : ally ? _allyGeneral : _enemyGeneral)
                        : TroopBatch(TroopOf(s), age < FlashSeconds ? 2 : s.Team, age < FlashSeconds ? 2 : 1)).Add(p, rot);
                    continue;
                }
                // 바라볼 방향: 치는 중이면 대상, 걸으면 걷는 쪽, 아니면 군 앞
                Vector3 look = Vector3.zero;
                bool attacking = s.LastAttackTick >= 0 && _battle.Tick - s.LastAttackTick < _battle.Rules.AttackTicks && (s.LastTargetId >= 0 || s.HitStructure);
                if (attacking) look = s.HitStructure ? ToWorld(s.StrikeAt) - p : _cur[s.LastTargetId] - p; // 성문을 치면 성문 쪽 (R-140)
                else
                {
                    var mv = _cur[i] - _prev[i];
                    var army = _battle.Armies[s.Army];
                    var fa = new Vector3((float)army.Facing.X.ToDisplayDouble(), 0f, (float)army.Facing.Y.ToDisplayDouble());
                    // 장수가 뒤로 가면 (물러나 지휘 · 자리 맞추기) 적 쪽을 본 채 뒷걸음 — 등을 보이면 퇴각처럼 보임 (2026-11-22 사용자). 도망 · 무너짐은 달려 감
                    bool backpedal = s.IsGeneral && !army.GeneralEscaping && !army.Routed && army.Order != CrowdOrders.Retreat && Vector3.Dot(mv, fa) < 0f;
                    if (backpedal) look = fa;
                    else if (mv.sqrMagnitude > 1e-5f) look = mv;
                    else
                    {
                        var f = _battle.Armies[s.Army].Facing;
                        look = new Vector3((float)f.X.ToDisplayDouble(), 0f, (float)f.Y.ToDisplayDouble());
                    }
                }
                look.y = 0f;
                if (look.sqrMagnitude > 1e-6f)
                {
                    float target = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                    _yaw[i] = Mathf.MoveTowardsAngle(_yaw[i], target, TurnDegPerSec * dt);
                }
                var r = Quaternion.Euler(0f, _yaw[i], 0f);
                bool walking = (_cur[i] - _prev[i]).sqrMagnitude > 1e-5f;
                bool ranged = !s.IsGeneral && _battle.Armies[s.Army].Troop.Ranged;
                if (attacking && ranged)
                {
                    // 활 (R-127): 쏘는 순간 몸을 살짝 젖혀 시위를 당겼다 놓는다
                    float ph = Mathf.Clamp01((now - s.LastAttackTick * tickSec) / (_battle.Rules.AttackTicks * tickSec));
                    r = r * Quaternion.Euler(-12f * Mathf.Sin(ph * Mathf.PI), 0f, 0f);
                }
                else if (attacking)
                {
                    // 공격 한 번 (AttackTicks 동안): 앞으로 찌르며 몸을 비틀어 무기를 휘두른다
                    float ph = Mathf.Clamp01((now - s.LastAttackTick * tickSec) / (_battle.Rules.AttackTicks * tickSec));
                    float swing = Mathf.Sin(ph * Mathf.PI);
                    p += r * Vector3.forward * (0.12f * swing);
                    r = r * Quaternion.Euler(10f * swing, 40f * Mathf.Sin(ph * Mathf.PI * 2f), 0f);
                }
                // 다리: 움직인 거리만큼 박자가 가고, 걷기 시작 · 멈출 때 흔들기 세기가 부드럽게 오르내린다
                float speed = (_cur[i] - _prev[i]).magnitude / Mathf.Max(1e-4f, tickSec);
                // 돌격(R-126)으로 빠르면 달리기: 다리 세기 1.5까지, 몸을 더 숙인다
                float want = walking ? Mathf.Clamp(speed / 1.2f, 0f, 1.5f) : 0f;
                _legAmp[i] = walking ? Mathf.MoveTowards(_legAmp[i], want, dt * 6f) : Mathf.MoveTowards(_legAmp[i], 0f, dt * 4f);
                var bodyRot = r;
                if (!attacking && walking) bodyRot = r * Quaternion.Euler(6f * _legAmp[i] + (speed > 2f ? 8f : 0f), 0f, 0f); // 걸을 때 살짝, 달릴 때 더 앞으로
                // 부딪쳐 밀린 병사: 잠깐 뒤로 젖혀진다
                if (s.ImpactTick >= 0 && s.ImpactTick != s.LastAttackTick)
                {
                    float hit = now - s.ImpactTick * tickSec;
                    if (hit >= 0f && hit < 0.4f) bodyRot = bodyRot * Quaternion.Euler(-22f * Mathf.Sin(hit / 0.4f * Mathf.PI), 0f, 0f);
                }
                p.y = Ground.StandHeight(p.x, p.z);
                if (s.IsGeneral)
                {
                    // 말: 다리 넷을 따로 — 앞 둘 · 뒤 둘이 번갈아 (달리는 말), 몸은 걸음에 맞춰 앞뒤로 살짝 출렁 (위아래로 튀지 않음)
                    float walkedG = _stride[i] - (1f - alpha) * (_cur[i] - _prev[i]).magnitude;
                    float gph = walkedG / HorseStride * Mathf.PI * 2f;
                    float amp = Mathf.Min(1f, _legAmp[i]);
                    var yawRot = Quaternion.Euler(0f, _yaw[i], 0f);
                    var horseRot = yawRot * Quaternion.Euler(4f * Mathf.Sin(gph * 2f) * amp, 0f, 0f);
                    var ga = _battle.Armies[s.Army];
                    if (ga.GeneralPulledBack && !ga.GeneralEscaping && !attacking)
                    {
                        // 지휘 동작: 2.4초마다 0.6초 동안 말이 앞발을 들 듯 몸을 젖힘
                        float cyc = (now + s.Id * 0.37f) % 2.4f;
                        if (cyc < 0.6f) horseRot = horseRot * Quaternion.Euler(-12f * Mathf.Sin(cyc / 0.6f * Mathf.PI), 0f, 0f);
                    }
                    (ally ? _allyGeneral : _enemyGeneral).Add(p, horseRot);
                    var hl = ally ? _allyHorseLegs : _enemyHorseLegs;
                    var gm = Matrix4x4.TRS(p, horseRot, Vector3.one * GeneralModelScale);
                    for (int k = 0; k < 4; k++)
                    {
                        bool front = k < 2;
                        float sw = Mathf.Sin(gph + (front ? 0f : Mathf.PI) + (k % 2 == 0 ? 0f : 0.5f)) * HorseSwingDeg * amp;
                        hl.Matrices.Add(gm * Matrix4x4.TRS(SoldierMeshes.HorseLegAt(k), Quaternion.Euler(sw, 0f, 0f), Vector3.one));
                    }
                    continue;
                }
                TroopBatch(TroopOf(s), s.Team, 0).Add(p, bodyRot);
                float walked = _stride[i] - (1f - alpha) * (_cur[i] - _prev[i]).magnitude; // 틱 사이도 이어서
                if (_battle.Armies[s.Army].Troop.Mounted)
                {
                    // 기병 말 다리 넷 (앞 둘 · 뒤 둘 번갈아)
                    float cph = walked / CavStride * Mathf.PI * 2f + i * 0.9f;
                    float camp = Mathf.Min(1.2f, _legAmp[i]);
                    var cl = ally ? _allyCavLegs : _enemyCavLegs;
                    var cm = Matrix4x4.TRS(p, bodyRot, Vector3.one * ModelScale);
                    for (int k = 0; k < 4; k++)
                    {
                        float sw = Mathf.Sin(cph + (k < 2 ? 0f : Mathf.PI) + (k % 2 == 0 ? 0f : 0.5f)) * CavSwingDeg * camp;
                        cl.Matrices.Add(cm * Matrix4x4.TRS(SoldierMeshes.CavalryLegAt(k), Quaternion.Euler(sw, 0f, 0f), Vector3.one));
                    }
                    continue;
                }
                float step = walked / StrideLength * Mathf.PI * 2f + i * 0.7f;
                float swingL = Mathf.Sin(step) * LegSwingDeg * _legAmp[i];
                if (attacking) swingL = Mathf.Lerp(swingL, 18f, 0.6f); // 칠 때 한 발 앞으로 디딤
                var legs = ally ? _allyLegs : _enemyLegs;
                var baseM = Matrix4x4.TRS(p, Quaternion.Euler(0f, _yaw[i], 0f), Vector3.one * ModelScale);
                legs.Matrices.Add(baseM * Matrix4x4.TRS(new Vector3(-SoldierMeshes.HipSide, SoldierMeshes.HipHeight, 0f), Quaternion.Euler(swingL, 0f, 0f), Vector3.one));
                legs.Matrices.Add(baseM * Matrix4x4.TRS(new Vector3(SoldierMeshes.HipSide, SoldierMeshes.HipHeight, 0f), Quaternion.Euler(attacking ? -12f : -swingL, 0f, 0f), Vector3.one));
            }
            DrawArrows(now, tickSec);
            DrawDust(now, tickSec);
            DrawRings(alpha);
            // 흔들림: 카메라를 제자리로 맞춘 뒤 조금 흔든다 (금방 잦아듦)
            Rig.Refresh();
            if (_shake > 0.001f)
            {
                Rig.Camera.transform.position += new Vector3(Mathf.Sin(now * 71f), Mathf.Sin(now * 53f) * 0.5f, Mathf.Cos(now * 67f)) * _shake;
                _shake = Mathf.MoveTowards(_shake, 0f, dt * 0.5f);
            }
            var cam = Rig.Camera;
            _dustBatch.Draw(cam);
            DrawGates(cam);
            Ground.Draw(cam);
            foreach (var b in _allBatches) b.Draw(cam);
        }

        public void Destroy()
        {
            Ground?.Destroy();
            Object.Destroy(_root);
            foreach (var o in _owned) if (o != null) Object.Destroy(o);
        }

        private T Own<T>(T o) where T : Object { _owned.Add(o); return o; }

        /// <summary>몸 · 투구 · 다리 · 갑옷은 편 색(밝게 · 어둡게), 얼굴만 살색, 무기는 쇠색 — 멀리서도 한 덩어리 편 색.</summary>
        private InstanceBatch Batch(Mesh mesh, Color team, Color dark, float scale = ModelScale)
        {
            var parts = SoldierMeshes.PartsOf(mesh);
            var mats = new Material[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                Color c = parts[i] == SoldierMeshes.Skin ? (team == DeadWhite ? DeadWhite : SoldierMeshes.SkinColor)
                    : parts[i] == SoldierMeshes.Metal ? (team == DeadWhite ? DeadWhite : SoldierMeshes.MetalColor)
                    : parts[i] == SoldierMeshes.Dark || parts[i] == SoldierMeshes.Horse ? dark
                    : team;
                var m = Own(NewMaterial(c));
                m.enableInstancing = true;
                mats[i] = m;
            }
            return new InstanceBatch(mesh, mats, Matrix4x4.Scale(Vector3.one * scale));
        }

        private string TroopOf(CrowdSoldier s) => _battle.Armies[s.Army].Troop.BaseId;

        /// <summary>병종 모양 묶음 (kind 0 다리 뺀 몸 · 1 통째 · 2 하얀 통째, team 2 = 하양).</summary>
        private InstanceBatch TroopBatch(string troop, int team, int kind)
        {
            if (_troopBatches.TryGetValue((troop, team, kind), out var b)) return b;
            var mesh = SoldierMeshes.For(troop, legs: kind != 0);
            b = kind == 2 || team == 2 ? Batch(mesh, DeadWhite, DeadWhite)
                : team == CrowdBattle.Ally ? Batch(mesh, AllyColor, AllyDark) : Batch(mesh, EnemyColor, EnemyDark);
            _troopBatches[(troop, team, kind)] = b;
            _allBatches.Add(b);
            return b;
        }

        /// <summary>화살 (R-127): 쏜 자리 → 쏠 때 대상 자리로 포물선, 날아가는 쪽을 본다.</summary>
        private void DrawArrows(float now, float tickSec)
        {
            foreach (var a in _battle.Arrows)
            {
                float t0 = a.FireTick * tickSec, t1 = a.ArriveTick * tickSec;
                float u = Mathf.Clamp01((now - t0) / Mathf.Max(0.01f, t1 - t0));
                var from = ToWorld(a.From) + Vector3.up * 0.4f;
                var to = ToWorld(a.To) + Vector3.up * 0.2f;
                float dist = Vector3.Distance(from, to);
                float arc = 0.6f + dist * 0.18f;
                Vector3 P(float x) => Vector3.Lerp(from, to, x) + Vector3.up * (4f * arc * x * (1f - x));
                var p = P(u);
                var dir = P(Mathf.Min(1f, u + 0.02f)) - P(Mathf.Max(0f, u - 0.02f));
                if (dir.sqrMagnitude < 1e-6f) continue;
                _arrowBatch.Matrices.Add(Matrix4x4.TRS(p, Quaternion.LookRotation(dir), new Vector3(0.03f, 0.03f, 0.42f)));
            }
        }

        private void DrawDust(float now, float tickSec)
        {
            _dustBatch.Matrices.Clear();
            for (int k = _dust.Count - 1; k >= 0; k--)
            {
                var d = _dust[k];
                float age = now - d.tick * tickSec;
                if (age > DustSeconds) { _dust.RemoveAt(k); continue; }
                if (age < 0f) continue;
                float t = age / DustSeconds;
                float size = Mathf.Lerp(0.12f, 0.5f, t) * (1f - t * t); // 피어오르다 작아지며 사라짐
                var p = d.pos + new Vector3((d.seed - 2) * 0.05f * t, 0.08f + 0.25f * t, 0f);
                _dustBatch.Matrices.Add(Matrix4x4.TRS(p, Quaternion.identity, new Vector3(size, size * 0.6f, size)));
            }
        }

        /// <summary>대열 주위 고리 반지름.</summary>
        private static float RadiusOf(CrowdArmy a) => (float)Game.Foundation.Math.Fix.Max(a.HalfWidth, a.HalfDepth).ToDisplayDouble() + 0.9f;

        /// <summary>
        /// 부대 가운데 (화면): 틱 사이를 이은 병사 위치의 평균 — 규칙 위치(틱마다 건너뜀)로 잡으면 고리가 덜컥거린다 (2026-11-21 사용자 폰).
        /// </summary>
        private Vector3 ShownCenter(CrowdArmy a, float alpha)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var s in a.Soldiers)
                if (s.Alive && !s.IsGeneral && s.Id < _cur.Length) { sum += Vector3.Lerp(_prev[s.Id], _cur[s.Id], alpha); n++; }
            if (n > 0) return sum / n;
            return GeneralShown(a, alpha);
        }

        /// <summary>이름표 자리: 화면에 그린 장수 (틱 사이를 이은 곳 — 틱 위치를 따르면 1초 10번 툭툭 끊겨 덜렁거림, 2026-11-22 사용자 영상), 장수가 없으면 병사 가운데.</summary>
        public Vector3 TagAnchor(CrowdArmy a) => a.General.Alive ? GeneralShown(a, _lastAlpha) : ShownCenter(a, _lastAlpha);
        private float _lastAlpha = 1f;

        private Vector3 GeneralShown(CrowdArmy a, float alpha) =>
            a.General.Id < _cur.Length ? Vector3.Lerp(_prev[a.General.Id], _cur[a.General.Id], alpha) : ToWorld(a.Centroid());

        private void DrawRings(float alpha)
        {
            int sel = 0, goal = 0, path = 0;
            if (Trail.Count >= 2) Polyline(PathLine(path++), Trail); // 그리는 중인 길
            var targeted = new HashSet<int>();
            foreach (var a in _battle.Armies)
            {
                if (a.Team != CrowdBattle.Ally || !_battle.Standing(a)) continue;
                bool chosen = SelectAll ? a.Leading : a.Id == SelectedArmy;
                if (!chosen) continue;
                Circle(Pooled(_selRings, sel++, "SelectRing", new Color(1f, 0.85f, 0.25f), 0.12f), ShownCenter(a, alpha), RadiusOf(a));
                // 목표: 이동 = 그 땅에 작은 고리, 공격 = 대상 부대 둘레 (같은 대상은 한 번만)
                if (a.Order == CrowdOrders.Move)
                {
                    // 남은 길: 부대 → 목표 → 경유 지점들 (지나간 곳은 지워짐), 끝에 고리
                    _pts.Clear();
                    _pts.Add(ShownCenter(a, alpha));
                    _pts.Add(ToWorld(a.OrderPoint));
                    foreach (var w in a.Waypoints) _pts.Add(ToWorld(w));
                    Polyline(PathLine(path++), _pts);
                    Circle(Pooled(_goalRings, goal++, "GoalRing", Color.white, 0.08f), _pts[_pts.Count - 1], 0.6f);
                    if (a.Waypoints.Count <= CrowdBattle.MaxWaypoints)
                        foreach (var w in a.Waypoints) Circle(Pooled(_goalRings, goal++, "GoalRing", Color.white, 0.08f), ToWorld(w), 0.4f); // 예약 경유 지점 (R-129)
                    if (a.PathThenAttack >= 0 && targeted.Add(a.PathThenAttack))
                    {
                        var t = _battle.Armies[a.PathThenAttack];
                        Circle(Pooled(_goalRings, goal++, "GoalRing", new Color(1f, 0.55f, 0.45f), 0.08f), ShownCenter(t, alpha), RadiusOf(t));
                    }
                }
                else if (a.Order == CrowdOrders.Attack && a.OrderTarget >= 0 && targeted.Add(a.OrderTarget))
                {
                    var t = _battle.Armies[a.OrderTarget];
                    Circle(Pooled(_goalRings, goal++, "GoalRing", Color.white, 0.08f), ShownCenter(t, alpha), RadiusOf(t));
                }
            }
            // 스킬 (R-130): 쓴 순간 부대 둘레로 퍼지는 고리 (0.8초), 화살비는 떨어질 때까지 그 자리 붉은 고리
            int fx = 0;
            float tickSec = (float)_battle.Rules.TickSeconds.ToDisplayDouble();
            float now = (_battle.Tick + alpha) * tickSec;
            foreach (var ev in _battle.SkillEvents)
            {
                if (ev.Skill == CrowdBattle.ArrowRain)
                {
                    if (now > ev.LandTick * tickSec + 0.3f) continue;
                    var def = _battle.SkillDef(ev.Skill);
                    float rad = def != null ? (float)def.Radius.ToDisplayDouble() : 2f;
                    Circle(Pooled(_fxRings, fx++, "SkillRing", new Color(0.95f, 0.3f, 0.2f), 0.1f), ToWorld(ev.Point), rad);
                    continue;
                }
                float age = now - ev.Tick * tickSec;
                if (age < 0f || age > 0.8f) continue;
                var army = _battle.Armies[ev.Army];
                Circle(Pooled(_fxRings, fx++, "SkillRing", new Color(1f, 0.92f, 0.4f), 0.14f * (1f - age / 0.8f) + 0.02f), ShownCenter(army, alpha), RadiusOf(army) * (0.6f + age * 1.2f));
            }
            for (int i = fx; i < _fxRings.Count; i++) _fxRings[i].gameObject.SetActive(false);
            for (int i = sel; i < _selRings.Count; i++) _selRings[i].gameObject.SetActive(false);
            for (int i = goal; i < _goalRings.Count; i++) _goalRings[i].gameObject.SetActive(false);
            for (int i = path; i < _pathLines.Count; i++) _pathLines[i].gameObject.SetActive(false);
        }

        private LineRenderer Pooled(List<LineRenderer> pool, int index, string name, Color color, float width)
        {
            while (pool.Count <= index) pool.Add(Ring(name, color, width));
            var line = pool[index];
            if (!line.gameObject.activeSelf) line.gameObject.SetActive(true);
            return line;
        }

        private LineRenderer PathLine(int index)
        {
            var line = Pooled(_pathLines, index, "PathLine", new Color(1f, 1f, 1f, 0.9f), 0.12f);
            line.loop = false;
            return line;
        }

        private static void Polyline(LineRenderer line, List<Vector3> pts)
        {
            line.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++) line.SetPosition(i, pts[i] + new Vector3(0f, 0.07f, 0f));
        }

        private void Circle(LineRenderer line, Vector3 c, float radius)
        {
            for (int i = 0; i < RingSegments; i++)
            {
                float t = i / (float)RingSegments * Mathf.PI * 2f;
                float px = c.x + Mathf.Cos(t) * radius, pz = c.z + Mathf.Sin(t) * radius;
                line.SetPosition(i, new Vector3(px, Ground.StandHeight(px, pz) + 0.1f, pz)); // 땅 높낮이를 따라 (R-133)
            }
        }

        private LineRenderer Ring(string name, Color color, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var line = go.AddComponent<LineRenderer>();
            var tpl = Resources.Load<Material>("Materials/GameUnlit");
            var mat = Own(tpl != null ? new Material(tpl) : NewMaterial(color));
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            line.sharedMaterial = mat;
            line.widthMultiplier = width;
            line.loop = true;
            line.useWorldSpace = true;
            line.positionCount = RingSegments;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            go.SetActive(false);
            return line;
        }

        /// <summary>다리 묶음: 행렬을 직접 넣는다 (모델 크기 · 엉덩이 위치 · 흔들기를 곱한 것).</summary>
        private InstanceBatch LegBatch(Mesh leg, Color dark)
        {
            var m = Own(NewMaterial(dark));
            m.enableInstancing = true;
            return new InstanceBatch(leg, new[] { m }, Matrix4x4.identity);
        }

        private static Material _template;

        public static Material NewMaterial(Color color)
        {
            if (_template == null) _template = Resources.Load<Material>("Materials/GameLit");
            if (_template == null && GraphicsSettings.defaultRenderPipeline != null) _template = GraphicsSettings.defaultRenderPipeline.defaultMaterial;
            var m = _template != null ? new Material(_template) : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            m.mainTexture = null;
            return m;
        }
    }
}
