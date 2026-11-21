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
        // 날씨·바람·전조 (D19)
        private readonly Material _rainMat, _dustMat, _snowMat;
        private readonly List<Matrix4x4> _weather = new List<Matrix4x4>();
        private readonly Dictionary<Game.Battle.Model.Skills.ScheduledEffect, LineRenderer> _telegraphs = new Dictionary<Game.Battle.Model.Skills.ScheduledEffect, LineRenderer>();
        private const int RainDrops = 360, DustMotes = 90;
        private static readonly Vector3 RainScale = new Vector3(0.02f, 0.45f, 0.02f);
        private static readonly Vector3 DustScale = new Vector3(0.07f, 0.03f, 0.07f);
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
            _rainMat = material(new Color(0.70f, 0.78f, 0.90f));
            _dustMat = material(new Color(0.80f, 0.72f, 0.55f));
            _snowMat = material(new Color(0.95f, 0.97f, 1f));
            foreach (var m in new[] { _arrowMat, _flameMat, _emberMat, _rainMat, _dustMat, _snowMat }) m.enableInstancing = true;
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

            DrawWeather(ctx, camera);
            DrawTelegraphs(ctx);

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

        /// <summary>
        /// 날씨·바람 (D19): 카메라가 보는 곳 둘레에만 그린다. 비 = 바람 쪽으로 기운 빗줄기, 혹한 = 천천히 내리는 눈,
        /// 바람이 불면 흙먼지가 바람 방향으로 흐른다 (세기만큼 많고 빠르게). 자리는 시계로 정해 매 프레임 같은 모양이 이어진다.
        /// </summary>
        private void DrawWeather(BattleContext ctx, Camera camera)
        {
            var env = ctx.Environment;
            string weather = env.EffectiveWeather.Id;
            var focus = _field.Rig.Focus;
            float area = 26f;
            var wd = env.WindDirection;
            var wind = new Vector3((float)wd.X.ToDisplayDouble(), 0f, -(float)wd.Y.ToDisplayDouble());
            float strength = (float)env.WindStrength.ToDisplayDouble();

            if (weather == "rain" || weather == "cold")
            {
                bool snow = weather == "cold";
                var rot = snow ? Quaternion.identity : Quaternion.FromToRotation(Vector3.up, (Vector3.up * 6f - wind * strength).normalized);
                float fall = snow ? 1.2f : 14f, height = 9f;
                for (int i = 0; i < (snow ? RainDrops / 2 : RainDrops); i++)
                {
                    float hx = Hash(i * 3 + 1), hz = Hash(i * 3 + 2), hy = Hash(i * 3 + 3);
                    float y = height - Mathf.Repeat(Clock * fall + hy * height, height);
                    var drift = wind * strength * (height - y) * (snow ? 0.4f : 0.15f);
                    if (snow) drift += new Vector3(Mathf.Sin(Clock * 1.3f + i) * 0.3f, 0f, Mathf.Cos(Clock * 1.1f + i) * 0.3f);
                    var p = focus + new Vector3((hx - 0.5f) * area, y, (hz - 0.5f) * area) + drift;
                    _weather.Add(Matrix4x4.TRS(p, rot, snow ? Vector3.one * 0.08f : RainScale));
                }
                Flush(snow ? _snowMat : _rainMat, camera, _weather);
            }
            if (strength > 0.01f && wind.sqrMagnitude > 1e-4f)
            {
                int motes = Mathf.Min(DustMotes * 2, Mathf.RoundToInt(DustMotes * strength));
                var dir = wind.normalized;
                for (int i = 0; i < motes; i++)
                {
                    float hx = Hash(i * 5 + 11), hz = Hash(i * 5 + 12), hs = Hash(i * 5 + 13);
                    float travel = Mathf.Repeat(Clock * (1.5f + strength * 1.5f) * (0.6f + hs) + hs * area, area) - area / 2f;
                    var p = focus + new Vector3((hx - 0.5f) * area, 0f, (hz - 0.5f) * area) + dir * travel;
                    p.y = _field.GroundHeight(p.x, p.z) + 0.25f + Mathf.Sin(Clock * 3f + i) * 0.12f;
                    _weather.Add(Matrix4x4.TRS(p, Quaternion.LookRotation(dir), DustScale));
                }
                Flush(_dustMat, camera, _weather);
            }
        }

        /// <summary>전조 (D19): 아직 나타나지 않은 스킬 효과(화계 등)의 자리에 깜빡이는 붉은 원, 터질 때가 가까울수록 빨리.</summary>
        private void DrawTelegraphs(BattleContext ctx)
        {
            var live = new HashSet<Game.Battle.Model.Skills.ScheduledEffect>(ctx.Scheduled);
            foreach (var kv in new List<KeyValuePair<Game.Battle.Model.Skills.ScheduledEffect, LineRenderer>>(_telegraphs))
                if (!live.Contains(kv.Key)) { if (kv.Value != null) Object.Destroy(kv.Value.gameObject); _telegraphs.Remove(kv.Key); }
            foreach (var s in ctx.Scheduled)
            {
                if (s.Cast == null || (s.Cast.Caster.Team != _ctl.Team && !_ctl.Sees(s.Cast.Caster) && !_ctl.RevealAll)) continue; // 보이는 적의 전조만
                if (!_telegraphs.TryGetValue(s, out var line))
                {
                    line = _newLine(new Color(1f, 0.35f, 0.15f));
                    line.loop = true;
                    line.positionCount = RingSegments;
                    _telegraphs[s] = line;
                }
                long left = s.FireTick - ctx.Tick;
                float speed = left < 10 ? 18f : 7f;
                float r = Mathf.Max(1f, (float)s.Cast.Skill.Radius.ToDisplayDouble()) * (0.85f + 0.15f * Mathf.Sin(Clock * speed));
                line.widthMultiplier = 0.08f + 0.06f * (0.5f + 0.5f * Mathf.Sin(Clock * speed));
                var c = _field.ToWorld(s.Cast.Point);
                for (int k = 0; k < RingSegments; k++)
                {
                    float a = k * Mathf.PI * 2f / RingSegments;
                    float x = c.x + Mathf.Cos(a) * r, z = c.z + Mathf.Sin(a) * r;
                    line.SetPosition(k, new Vector3(x, _field.GroundHeight(x, z) + 0.18f, z));
                }
            }
        }

        private static float Hash(int i)
        {
            unchecked
            {
                uint x = (uint)i * 2654435761u;
                x ^= x >> 15; x *= 2246822519u; x ^= x >> 13;
                return (x & 0xFFFFFF) / (float)0x1000000;
            }
        }

        private void Flush(Material mat, Camera camera, List<Matrix4x4> list)
        {
            int n = list.Count;
            if (n == 0) return;
            if (_buffer.Length < n) _buffer = new Matrix4x4[Mathf.NextPowerOfTwo(n)];
            list.CopyTo(_buffer);
            var rp = new RenderParams(mat) { camera = camera, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderMeshInstanced(rp, _cube, 0, _buffer, n);
            list.Clear();
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
