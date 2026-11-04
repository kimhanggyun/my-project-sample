using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Battle.Model.Environment
{
    /// <summary>
    /// 전장 환경: 바람, 날씨, 시간대, 불 (battle.md 2.17). 모두 가산 보정으로 다른 규칙에 들어간다.
    /// 스위치: wind(windRange, windMove), weather, timeOfDay, fireSpread.
    /// </summary>
    public sealed class BattleEnvironment
    {
        public const string WindFlag = "wind";
        public const string WindRangeFlag = "windRange";
        public const string WindMoveFlag = "windMove";
        public const string WeatherFlag = "weather";
        public const string TimeOfDayFlag = "timeOfDay";
        public const string FireSpreadFlag = "fireSpread";

        private readonly BattleContext _ctx;

        /// <summary>바람이 불어 가는 방향 (단위 벡터). 바람 없으면 0.</summary>
        public Vec WindDirection { get; internal set; }
        /// <summary>바람 세기 (날씨 보너스 포함 전).</summary>
        public Fix WindBaseStrength { get; internal set; }
        public ConditionDef Weather { get; internal set; }
        public ConditionDef TimeOfDay { get; internal set; }
        /// <summary>타는 칸 (칸 번호 → 남은 틱). 칸 번호 순서로 처리한다.</summary>
        public SortedDictionary<int, long> Burning { get; } = new SortedDictionary<int, long>();
        /// <summary>타는 칸 → 불을 놓은 팀 (번진 불은 원래 불의 팀).</summary>
        public Dictionary<int, string> FireTeam { get; } = new Dictionary<int, string>();
        /// <summary>불 피해를 이미 기록한 (군단, 팀).</summary>
        internal HashSet<string> FireHitRecorded { get; } = new HashSet<string>();

        internal BattleEnvironment(BattleContext ctx) { _ctx = ctx; }

        public bool WindOn => _ctx.IsEnabled(WindFlag);

        /// <summary>실제 바람 세기 (스위치 꺼지면 0, 강풍 날씨면 더함).</summary>
        public Fix WindStrength
        {
            get
            {
                if (!WindOn || WindDirection.Equals(Vec.Zero)) return Fix.Zero;
                return Fix.Max(Fix.Zero, WindBaseStrength + EffectiveWeather.WindStrengthBonus);
            }
        }

        public ConditionDef EffectiveWeather => _ctx.IsEnabled(WeatherFlag) ? Weather : ConditionDef.None;
        public ConditionDef EffectiveTime => _ctx.IsEnabled(TimeOfDayFlag) ? TimeOfDay : ConditionDef.None;

        /// <summary>날씨 + 시간대 합.</summary>
        public Fix Sum(System.Func<ConditionDef, Fix> f) => f(EffectiveWeather) + f(EffectiveTime);

        public Fix TagSum(System.Func<ConditionDef, IReadOnlyDictionary<string, Fix>> f, TroopTypeDef troop)
        {
            Fix s = Fix.Zero;
            foreach (var c in new[] { EffectiveWeather, EffectiveTime })
                foreach (var kv in f(c))
                    if (troop.HasTag(kv.Key)) s += kv.Value;
            return s;
        }

        public bool FireAllowed => EffectiveWeather.FireAllowed && EffectiveTime.FireAllowed;

        /// <summary>원거리 사거리 변화 (칸): 순풍 늘고, 역풍 줄고, 옆바람 조금 준다 (windRange) + 날씨.</summary>
        public Fix RangedRangeDelta(Vec from, Vec to)
        {
            Fix d = Sum(c => c.RangedRangeCells);
            Fix s = WindStrength;
            if (s == Fix.Zero || !_ctx.IsEnabled(WindRangeFlag)) return d;
            var k = _ctx.Combat;
            Fix dot = (to - from).Normalized().Dot(WindDirection);
            if (dot >= k.WindFrontDot) return d + k.WindTailRangeCellsPerStrength * s;
            if (dot <= -k.WindFrontDot) return d + k.WindHeadRangeCellsPerStrength * s;
            return d + k.WindCrossRangeCellsPerStrength * s;
        }

        /// <summary>이동 속도 보정 (%p): 바람(진행 방향·세기, windMove) + 날씨·시간대 + 병종 태그.</summary>
        public Fix MovePp(Vec moveDir, IReadOnlyList<TroopTypeDef> members)
        {
            Fix pp = Sum(c => c.MovePp);
            Fix tag = Fix.Zero;
            bool first = true;
            foreach (var m in members)
            {
                Fix t = TagSum(c => c.TagMovePp, m);
                if (first || t < tag) { tag = t; first = false; } // 가장 느려지는 병종 기준
            }
            pp += tag;
            Fix s = WindStrength;
            if (s > Fix.Zero && _ctx.IsEnabled(WindMoveFlag))
                pp += moveDir.Dot(WindDirection) * s * _ctx.Combat.WindMovePpPerStrength;
            return pp;
        }

        public bool IsBurning(Cell c) => Burning.ContainsKey(c.Y * _ctx.Field.Width + c.X);
    }

    /// <summary>바람·날씨 이벤트, 불 번짐·타기·불 피해.</summary>
    internal static class EnvironmentSystem
    {
        public static BattleEnvironment Create(BattleContext ctx)
        {
            var s = ctx.Setup;
            var env = new BattleEnvironment(ctx)
            {
                Weather = ctx.Defs.Weathers[s.Weather],
                TimeOfDay = ctx.Defs.TimesOfDay[s.TimeOfDay],
            };
            SetWind(env, s.Wind);
            return env;
        }

        private static void SetWind(BattleEnvironment env, WindSetup w)
        {
            if (w == null) return;
            env.WindDirection = w.Direction == null || w.Strength <= Fix.Zero ? Vec.Zero : Facing.Parse(w.Direction);
            env.WindBaseStrength = w.Strength;
        }

        public static void Step(BattleContext ctx)
        {
            var env = ctx.Environment;
            foreach (var ch in ctx.Setup.Changes)
            {
                if (ch.Tick != ctx.Tick) continue;
                if (ch.Wind != null) SetWind(env, ch.Wind);
                if (ch.Weather != null) env.Weather = ctx.Defs.Weathers[ch.Weather];
            }
            FireStep(ctx);
        }

        /// <summary>지점에 불을 붙인다 (화계). 불이 붙는 지형이 아니면 아무 일 없음.</summary>
        public static bool Ignite(BattleContext ctx, Cell c, string team = null)
        {
            if (!ctx.Field.Map.InBounds(c.X, c.Y)) return false;
            var rule = ctx.Field.RuleAt(c);
            int idx = c.Y * ctx.Field.Width + c.X;
            if (!rule.Flammable || ctx.Environment.Burning.ContainsKey(idx)) return false;
            ctx.Environment.Burning[idx] = Skills.SkillRules.Ticks(ctx, rule.BurnSec);
            if (team != null) ctx.Environment.FireTeam[idx] = team;
            return true;
        }

        private static readonly int[] Dx = { 0, 1, 0, -1, 1, 1, -1, -1 };
        private static readonly int[] Dy = { -1, 0, 1, 0, -1, 1, 1, -1 };

        private static void FireStep(BattleContext ctx)
        {
            var env = ctx.Environment;
            if (env.Burning.Count == 0) return;
            var k = ctx.Combat;
            int w = ctx.Field.Width;

            // 번짐: 정해진 간격마다, 바람 방향 쪽 이웃 (강풍 날씨면 간격이 짧아짐). 비 등 불 금지 날씨에는 번지지 않음.
            long every = (k.FireSpreadEverySec / k.TickSeconds * Fix.Hundred / (Fix.Hundred + env.EffectiveWeather.FireSpreadPp)).CeilToLong();
            if (every < 1) every = 1;
            if (ctx.IsEnabled(BattleEnvironment.FireSpreadFlag) && env.FireAllowed && env.WindStrength > Fix.Zero && ctx.Tick % every == 0)
            {
                var spread = new List<(Cell cell, string team)>();
                foreach (var idx in env.Burning.Keys)
                {
                    var c = new Cell(idx % w, idx / w);
                    env.FireTeam.TryGetValue(idx, out var team);
                    for (int i = 0; i < 8; i++)
                    {
                        var n = new Cell(c.X + Dx[i], c.Y + Dy[i]);
                        Fix dot = new Vec(Fix.FromInt(Dx[i]), Fix.FromInt(Dy[i])).Normalized().Dot(env.WindDirection);
                        if (dot >= k.FireSpreadMinDot) spread.Add((n, team));
                    }
                }
                foreach (var (n, team) in spread) Ignite(ctx, n, team);
            }

            // 불 피해: 타는 칸 위 소부대 (불에 약한 병종은 배수). 숨은 복병은 드러난다.
            foreach (var corps in ctx.Corps)
            {
                if (!corps.InBattle) continue;
                bool hit = false;
                foreach (var u in corps.Units)
                {
                    var cell = Cell.Of(u.Position);
                    if (!u.Alive || !env.IsBurning(cell)) continue;
                    if (env.FireTeam.TryGetValue(cell.Y * w + cell.X, out var by) && env.FireHitRecorded.Add(corps.Id + "|" + by))
                        Events.EventLog.Add(ctx, Events.EventKinds.FireHit, corps.Id, by);
                    Fix loss = Fix.FromInt(u.Troops) * k.FireTroopLossPctPerSecond / 100 * k.TickSeconds;
                    if (u.Troop.HasTag("flammable")) loss = loss * k.FireFlammableTagMultiplier;
                    u.PendingLoss += loss;
                    hit = true;
                }
                if (!hit) continue;
                corps.BeingHit = true;
                corps.Morale = Fix.Clamp(corps.Morale - k.FireMoralePerSecond * k.TickSeconds, Fix.Zero, Fix.Hundred);
                if (corps.Hidden) Skills.StealthSystem.Expose(corps);
                corps.RemoveStatus(StatusIds.AmbushSetup);
            }

            // 타기: 시간이 다 되면 지형이 바뀐다 (숲 → 불탄 땅: 숨을 곳·엄폐가 사라짐)
            var done = new List<int>();
            var keys = new List<int>(env.Burning.Keys);
            foreach (var idx in keys)
            {
                long left = env.Burning[idx] - 1;
                if (left > 0) { env.Burning[idx] = left; continue; }
                done.Add(idx);
            }
            foreach (var idx in done)
            {
                env.Burning.Remove(idx);
                env.FireTeam.Remove(idx);
                var c = new Cell(idx % w, idx / w);
                var rule = ctx.Field.RuleAt(c);
                if (rule.BurnsTo != null) ctx.Field.SetTerrain(c, rule.BurnsTo);
            }
        }
    }
}
