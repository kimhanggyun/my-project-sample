using System.Collections.Generic;
using Game.Battle.Model.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Unity.Battle3D
{
    /// <summary>
    /// 전장 연출 (단순 도형): 화살(원거리 공격), 불꽃(불타는 칸), 스킬 파동(스킬 사용 이벤트).
    /// 전투 상태를 읽기만 하고, 연출 시계는 화면이 넘겨준다 (일시정지면 멈춤, 배속이면 빨라짐).
    /// </summary>
    public sealed class BattleEffects3D
    {
        private const float ArrowFlight = 0.7f;
        private const float ArrowEvery = 0.45f;
        private const int MaxArrows = 400;
        private const float RingTime = 0.9f;
        private const int RingSegments = 40;
        private static readonly Vector3 ArrowScale = new Vector3(0.04f, 0.04f, 0.45f);

        private struct Arrow { public Vector3 From, To; public float Start; }
        private struct Ring { public LineRenderer Line; public Vector3 Center; public float Start, Radius; }

        private readonly BattleField3D _field;
        private readonly Game.Battle.Model.Control.BattleControl _ctl;
        private readonly Mesh _cube;
        private readonly Material _arrowMat, _flameMat, _emberMat;
        private readonly List<Arrow> _arrows = new List<Arrow>();
        private readonly Dictionary<SubUnitState, float> _nextArrow = new Dictionary<SubUnitState, float>();
        private readonly List<Ring> _rings = new List<Ring>();
        private readonly List<Matrix4x4> _batch = new List<Matrix4x4>();
        private Matrix4x4[] _buffer = new Matrix4x4[256];
        private readonly System.Func<Color, LineRenderer> _newLine;

        public float Clock { get; private set; }

        internal BattleEffects3D(BattleField3D field, Game.Battle.Model.Control.BattleControl ctl, Mesh cube, System.Func<Color, Material> material, System.Func<Color, LineRenderer> newLine)
        {
            _field = field;
            _ctl = ctl;
            _cube = cube;
            _newLine = newLine;
            _arrowMat = material(new Color(0.25f, 0.18f, 0.10f));
            _flameMat = material(new Color(1f, 0.55f, 0.10f));
            _emberMat = material(new Color(1f, 0.85f, 0.30f));
            foreach (var m in new[] { _arrowMat, _flameMat, _emberMat }) m.enableInstancing = true;
        }

        public void Advance(float dt) => Clock += dt;

        /// <summary>틱이 지난 뒤: 원거리 소부대가 공격했으면 화살을 쏜다 (보이는 쪽만, 소부대마다 간격).</summary>
        public void OnTicked(BattleContext ctx)
        {
            foreach (var c in ctx.Corps)
            {
                if (!c.InBattle) continue;
                foreach (var u in c.Units)
                {
                    if (u.AttackTarget == null || !u.Troop.HasTag("ranged")) continue;
                    if (_nextArrow.TryGetValue(u, out var next) && next > Clock) continue;
                    _nextArrow[u] = Clock + ArrowEvery;
                    var target = Owner(ctx, u.AttackTarget);
                    if (!_ctl.Sees(c) && (target == null || !_ctl.Sees(target))) continue;
                    var from = _field.ToWorld(u.Position) + Vector3.up * 0.4f;
                    var to = _field.ToWorld(u.AttackTarget.Position) + Vector3.up * 0.2f;
                    for (int k = 0; k < 3 && _arrows.Count < MaxArrows; k++)
                    {
                        var jitter = new Vector3(Mathf.Sin(Clock * 7f + k * 2.1f), 0f, Mathf.Cos(Clock * 5f + k * 1.7f)) * 0.5f;
                        _arrows.Add(new Arrow { From = from + jitter * 0.3f, To = to + jitter, Start = Clock + k * 0.08f });
                    }
                }
            }
        }

        /// <summary>스킬 사용: 시전 군단 자리에서 퍼지는 파동.</summary>
        public void SkillUsed(Vector3 at, Color color)
        {
            var line = _newLine(color);
            line.loop = true;
            line.positionCount = RingSegments;
            _rings.Add(new Ring { Line = line, Center = at, Start = Clock, Radius = 3.5f });
        }

        /// <summary>매 프레임.</summary>
        public void Draw(BattleContext ctx, Camera camera)
        {
            // 화살: 포물선 위의 위치, 날아가는 방향으로
            _batch.Clear();
            for (int i = _arrows.Count - 1; i >= 0; i--)
            {
                var a = _arrows[i];
                float t = (Clock - a.Start) / ArrowFlight;
                if (t >= 1f) { _arrows.RemoveAt(i); continue; }
                if (t < 0f) continue;
                float arc = 1.2f + Vector3.Distance(a.From, a.To) * 0.12f;
                Vector3 P(float s) => Vector3.Lerp(a.From, a.To, s) + Vector3.up * (4f * arc * s * (1f - s));
                var p = P(t);
                var dir = P(Mathf.Min(1f, t + 0.05f)) - p;
                var rot = dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir) : Quaternion.identity;
                _batch.Add(Matrix4x4.TRS(p, rot, ArrowScale));
            }
            Flush(_arrowMat, camera);

            // 불꽃: 불타는 칸마다 깜빡이는 기둥 몇 개
            if (ctx.Environment != null && ctx.Environment.Burning.Count > 0)
            {
                int w = ctx.Field.Width;
                _batch.Clear();
                var embers = new List<Matrix4x4>();
                foreach (var kv in ctx.Environment.Burning)
                {
                    int x = kv.Key % w, y = kv.Key / w;
                    for (int k = 0; k < 3; k++)
                    {
                        float ox = 0.2f + 0.3f * k, oy = 0.25f + 0.25f * ((k + x) % 3);
                        var basePos = _field.ToWorld(new Game.Battle.Model.Geometry.Vec(
                            Game.Foundation.Math.Fix.FromInt(x), Game.Foundation.Math.Fix.FromInt(y))) + new Vector3(ox, 0f, -oy);
                        float flick = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Clock * 9f + x * 1.3f + y * 0.7f + k));
                        float h = 0.5f * flick;
                        _batch.Add(Matrix4x4.TRS(basePos + Vector3.up * h / 2f, Quaternion.identity, new Vector3(0.22f, h, 0.22f)));
                        float rise = (Clock * 0.8f + k * 0.33f + x * 0.1f) % 1f;
                        embers.Add(Matrix4x4.TRS(basePos + Vector3.up * (0.4f + rise * 1.2f), Quaternion.identity, Vector3.one * 0.07f * (1f - rise)));
                    }
                }
                Flush(_flameMat, camera);
                _batch.AddRange(embers);
                Flush(_emberMat, camera);
            }

            // 스킬 파동
            for (int i = _rings.Count - 1; i >= 0; i--)
            {
                var r = _rings[i];
                float t = (Clock - r.Start) / RingTime;
                if (t >= 1f || r.Line == null)
                {
                    if (r.Line != null) Object.Destroy(r.Line.gameObject);
                    _rings.RemoveAt(i);
                    continue;
                }
                float radius = Mathf.Lerp(0.3f, r.Radius, t);
                r.Line.widthMultiplier = Mathf.Lerp(0.25f, 0.02f, t);
                for (int k = 0; k < RingSegments; k++)
                {
                    float ang = k * Mathf.PI * 2f / RingSegments;
                    float x = r.Center.x + Mathf.Cos(ang) * radius, z = r.Center.z + Mathf.Sin(ang) * radius;
                    r.Line.SetPosition(k, new Vector3(x, _field.GroundHeight(x, z) + 0.15f + t * 0.5f, z));
                }
            }
        }

        private void Flush(Material mat, Camera camera)
        {
            int n = _batch.Count;
            if (n == 0) return;
            if (_buffer.Length < n) _buffer = new Matrix4x4[Mathf.NextPowerOfTwo(n)];
            _batch.CopyTo(_buffer);
            var rp = new RenderParams(mat) { camera = camera, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderMeshInstanced(rp, _cube, 0, _buffer, n);
            _batch.Clear();
        }

        private static CorpsState Owner(BattleContext ctx, SubUnitState unit)
        {
            foreach (var c in ctx.Corps) if (c.Units.Contains(unit)) return c;
            return null;
        }
    }
}
