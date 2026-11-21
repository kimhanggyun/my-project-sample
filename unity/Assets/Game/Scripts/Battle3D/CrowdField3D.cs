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
        private readonly InstanceBatch _ally, _enemy, _white, _allyGeneral, _enemyGeneral, _allyWhole, _enemyWhole, _allyLegs, _enemyLegs;
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
        private readonly LineRenderer _selRing, _goalRing;
        private const int RingSegments = 48;

        /// <summary>병사마다 걸은 거리 (다리 박자) · 다리 흔들기 세기 (걷기 시작 · 멈출 때 부드럽게).</summary>
        private float[] _stride = new float[0], _legAmp = new float[0];
        /// <summary>한 걸음 주기 동안 가는 거리 (월드). 짧을수록 종종걸음.</summary>
        private const float StrideLength = 0.55f;
        private const float LegSwingDeg = 32f;

        private const float FlashSeconds = 0.15f, FallSeconds = 0.35f, SinkSeconds = 0.45f;
        private const float TurnDegPerSec = 360f;
        /// <summary>병사 모형 크기 (자리 간격 0.45에 맞춰 촘촘한 무리로 보이게).</summary>
        private const float ModelScale = 1.6f;

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

            var lightGo = new GameObject("Sun");
            lightGo.transform.SetParent(_root.transform, false);
            lightGo.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;

            // 바닥: 풀색 판 하나 (가장자리 조금 넘게)
            // (기본 도형 Quad는 물리 충돌 부품이 딸려 와, 물리를 뺀 폰 빌드에서 "Can't add component … MeshCollider" 오류 — 2026-11-21 사용자 폰)
            var ground = new GameObject("Ground");
            ground.transform.SetParent(_root.transform, false);
            var gmesh = Own(new Mesh { name = "crowd_ground" });
            float m = 20f;
            gmesh.vertices = new[] { new Vector3(-m, 0f, -m), new Vector3(-m, 0f, _depth + m), new Vector3(_width + m, 0f, _depth + m), new Vector3(_width + m, 0f, -m) };
            gmesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            gmesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            gmesh.RecalculateBounds();
            ground.AddComponent<MeshFilter>().sharedMesh = gmesh;
            ground.AddComponent<MeshRenderer>().sharedMaterial = Own(NewMaterial(new Color(0.50f, 0.64f, 0.38f)));

            // 살아 있는 병사 = 다리 없는 몸 + 따로 흔드는 다리 두 짝, 쓰러지는 병사 = 다리 붙은 통째
            var body = SoldierMeshes.For("infantry", legs: false);
            var whole = SoldierMeshes.For("infantry");
            _ally = Batch(body, AllyColor, AllyDark);
            _enemy = Batch(body, EnemyColor, EnemyDark);
            _allyWhole = Batch(whole, AllyColor, AllyDark);
            _enemyWhole = Batch(whole, EnemyColor, EnemyDark);
            _white = Batch(whole, DeadWhite, DeadWhite);
            var dm = Own(NewMaterial(DustColor));
            dm.enableInstancing = true;
            _dustBatch = new InstanceBatch(Resources.GetBuiltinResource<Mesh>("Sphere.fbx"), new[] { dm }, Matrix4x4.identity);
            _selRing = Ring("SelectRing", new Color(1f, 0.85f, 0.25f), 0.12f);
            _goalRing = Ring("GoalRing", new Color(1f, 1f, 1f), 0.08f);
            var leg = SoldierMeshes.Leg();
            _allyLegs = LegBatch(leg, AllyDark);
            _enemyLegs = LegBatch(leg, EnemyDark);
            var general = SoldierMeshes.General(true);
            _allyGeneral = Batch(general, AllyColor, AllyDark);
            _enemyGeneral = Batch(general, EnemyColor, EnemyDark);
        }

        /// <summary>규칙 (x, y) → 월드 (x, 0, y): 우리 편(작은 y)이 화면 아래, 적이 위.</summary>
        public Vector3 ToWorld(Vec p) => new Vector3((float)p.X.ToDisplayDouble(), 0f, (float)p.Y.ToDisplayDouble());

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
            float tickSec = (float)_battle.Rules.TickSeconds.ToDisplayDouble();
            float now = (_battle.Tick + alpha) * tickSec;
            _ally.Matrices.Clear(); _enemy.Matrices.Clear(); _white.Matrices.Clear();
            _allyWhole.Matrices.Clear(); _enemyWhole.Matrices.Clear(); _allyLegs.Matrices.Clear(); _enemyLegs.Matrices.Clear();
            _allyGeneral.Matrices.Clear(); _enemyGeneral.Matrices.Clear();
            var soldiers = _battle.Soldiers;
            for (int i = 0; i < soldiers.Count; i++)
            {
                var s = soldiers[i];
                var p = Vector3.Lerp(_prev[i], _cur[i], alpha);
                bool ally = s.Team == CrowdBattle.Ally;
                if (!s.Alive)
                {
                    float age = now - s.DiedTick * tickSec;
                    if (age > FallSeconds + SinkSeconds) continue;
                    float fall = Mathf.Clamp01(age / FallSeconds);
                    float sink = Mathf.Clamp01((age - FallSeconds) / SinkSeconds);
                    var back = Quaternion.Euler(0f, _yaw[i], 0f);
                    p += back * Vector3.back * (0.18f * (1f - (1f - fall) * (1f - fall)));
                    p.y = 0.06f * Mathf.Sin(Mathf.PI * fall) - 0.35f * sink * sink;
                    var rot = back * Quaternion.Euler(-80f * fall * fall, 0f, 0f);
                    (age < FlashSeconds ? _white : s.IsGeneral ? (ally ? _allyGeneral : _enemyGeneral) : ally ? _allyWhole : _enemyWhole).Add(p, rot);
                    continue;
                }
                // 바라볼 방향: 치는 중이면 대상, 걸으면 걷는 쪽, 아니면 군 앞
                Vector3 look = Vector3.zero;
                bool attacking = s.LastAttackTick >= 0 && _battle.Tick - s.LastAttackTick < _battle.Rules.AttackTicks && s.LastTargetId >= 0;
                if (attacking) look = _cur[s.LastTargetId] - p;
                else
                {
                    var mv = _cur[i] - _prev[i];
                    if (mv.sqrMagnitude > 1e-5f) look = mv;
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
                if (attacking)
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
                p.y = 0f;
                if (s.IsGeneral) { (ally ? _allyGeneral : _enemyGeneral).Add(p, bodyRot); continue; }
                (ally ? _ally : _enemy).Add(p, bodyRot);
                float walked = _stride[i] - (1f - alpha) * (_cur[i] - _prev[i]).magnitude; // 틱 사이도 이어서
                float step = walked / StrideLength * Mathf.PI * 2f + i * 0.7f;
                float swingL = Mathf.Sin(step) * LegSwingDeg * _legAmp[i];
                if (attacking) swingL = Mathf.Lerp(swingL, 18f, 0.6f); // 칠 때 한 발 앞으로 디딤
                var legs = ally ? _allyLegs : _enemyLegs;
                var baseM = Matrix4x4.TRS(p, Quaternion.Euler(0f, _yaw[i], 0f), Vector3.one * ModelScale);
                legs.Matrices.Add(baseM * Matrix4x4.TRS(new Vector3(-SoldierMeshes.HipSide, SoldierMeshes.HipHeight, 0f), Quaternion.Euler(swingL, 0f, 0f), Vector3.one));
                legs.Matrices.Add(baseM * Matrix4x4.TRS(new Vector3(SoldierMeshes.HipSide, SoldierMeshes.HipHeight, 0f), Quaternion.Euler(attacking ? -12f : -swingL, 0f, 0f), Vector3.one));
            }
            DrawDust(now, tickSec);
            DrawRings();
            // 흔들림: 카메라를 제자리로 맞춘 뒤 조금 흔든다 (금방 잦아듦)
            Rig.Refresh();
            if (_shake > 0.001f)
            {
                Rig.Camera.transform.position += new Vector3(Mathf.Sin(now * 71f), Mathf.Sin(now * 53f) * 0.5f, Mathf.Cos(now * 67f)) * _shake;
                _shake = Mathf.MoveTowards(_shake, 0f, dt * 0.5f);
            }
            var cam = Rig.Camera;
            _dustBatch.Draw(cam);
            _ally.Draw(cam); _enemy.Draw(cam); _white.Draw(cam); _allyGeneral.Draw(cam); _enemyGeneral.Draw(cam);
            _allyWhole.Draw(cam); _enemyWhole.Draw(cam); _allyLegs.Draw(cam); _enemyLegs.Draw(cam);
        }

        public void Destroy()
        {
            Object.Destroy(_root);
            foreach (var o in _owned) if (o != null) Object.Destroy(o);
        }

        private T Own<T>(T o) where T : Object { _owned.Add(o); return o; }

        /// <summary>몸 · 투구 · 다리 · 갑옷은 편 색(밝게 · 어둡게), 얼굴만 살색, 무기는 쇠색 — 멀리서도 한 덩어리 편 색.</summary>
        private InstanceBatch Batch(Mesh mesh, Color team, Color dark)
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
            return new InstanceBatch(mesh, mats, Matrix4x4.Scale(Vector3.one * ModelScale));
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

        private void DrawRings()
        {
            bool sel = SelectedArmy >= 0 && SelectedArmy < _battle.Armies.Count && _battle.Standing(_battle.Armies[SelectedArmy]);
            _selRing.gameObject.SetActive(sel);
            _goalRing.gameObject.SetActive(false);
            if (!sel) return;
            var a = _battle.Armies[SelectedArmy];
            Circle(_selRing, ToWorld(a.Centroid()), RadiusOf(a));
            if (a.Order == CrowdOrders.Move) { _goalRing.gameObject.SetActive(true); Circle(_goalRing, ToWorld(a.OrderPoint), 0.6f); }
            else if (a.Order == CrowdOrders.Attack && a.OrderTarget >= 0)
            {
                var t = _battle.Armies[a.OrderTarget];
                _goalRing.gameObject.SetActive(true);
                Circle(_goalRing, ToWorld(t.Centroid()), RadiusOf(t));
            }
        }

        private static void Circle(LineRenderer line, Vector3 c, float radius)
        {
            for (int i = 0; i < RingSegments; i++)
            {
                float t = i / (float)RingSegments * Mathf.PI * 2f;
                line.SetPosition(i, c + new Vector3(Mathf.Cos(t) * radius, 0.06f, Mathf.Sin(t) * radius));
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

        private static Material NewMaterial(Color color)
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
