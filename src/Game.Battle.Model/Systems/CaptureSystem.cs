using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Foundation.Math;

namespace Game.Battle.Model.Systems
{
    /// <summary>
    /// 전투 중 포로 (battle.md 2.5.1 ②, 스위치 capture → battleCapture).
    /// 장수가 있는 중앙 소부대 주변 반경을 방향 칸(sectors)으로 나눠, 적 소부대가 차지한 방향 비율이 기준 이상이고
    /// 반경 안 아군 병력이 기준 이하면 "포위". 포위가 이어지는 동안 압력이 쌓이고, 매 판정마다
    /// 무력에 비례한 확률로 탈출(경상, 퇴각) 하거나 압력만큼의 확률로 포로가 된다 (군단 궤멸).
    /// </summary>
    internal static class CaptureSystem
    {
        public const string BattleCaptureFlag = "battleCapture";

        public static void Step(BattleContext ctx)
        {
            if (!ctx.IsEnabled(BattleCaptureFlag)) return;
            var k = ctx.Combat;
            long every = Skills.SkillRules.Ticks(ctx, k.CaptureCheckEverySec);
            if (every < 1 || ctx.Tick % every != 0) return;
            foreach (var c in ctx.Corps)
            {
                if (!c.InBattle) continue;
                var center = General(c);
                if (center == null || !Encircled(ctx, c, center))
                {
                    c.CapturePressure = Fix.Zero;
                    continue;
                }
                c.CapturePressure = Fix.Min(Fix.OneValue, c.CapturePressure + k.CapturePressurePerSecond * k.CaptureCheckEverySec);
                Fix escape = Fix.Min(Fix.OneValue, c.Setup.General.Might * k.CaptureEscapePerMightPoint);
                if (ctx.Random.Chance(escape))
                {
                    // 포위 탈출 성공 → 경상, 퇴각
                    c.CapturePressure = Fix.Zero;
                    if (ctx.IsEnabled(Contests.PreBattleContests.GeneralInjuryFlag) && !ctx.Injuries.ContainsKey(c.Setup.General.Id))
                        ctx.Injuries[c.Setup.General.Id] = "light";
                    if (c.Status == CorpsStatus.Active) RetreatSystem.BeginRetreat(ctx, c);
                    continue;
                }
                if (ctx.Random.Chance(c.CapturePressure))
                    Capture(ctx, c, EncirclingTeam(ctx, c, center), "encircled", false);
            }
        }

        public static void Capture(BattleContext ctx, CorpsState c, string byTeam, string cause, bool brainwash)
        {
            RetreatSystem.NearbyMoraleDrop(ctx, c);
            c.Status = CorpsStatus.Captured;
            Events.EventLog.Add(ctx, Events.EventKinds.Captured, c.Id, byTeam);
            c.Hidden = false;
            c.ClearPath();
            ctx.Captures.Add(new CaptureRecord { GeneralId = c.Setup.General.Id, CorpsId = c.Id, CapturedByTeam = byTeam, Cause = cause, BrainwashBonus = brainwash });
        }

        /// <summary>장수가 있는 소부대 = 중앙 칸.</summary>
        private static SubUnitState General(CorpsState c)
        {
            foreach (var u in c.Units) if (u.Slot == Data.Defs.SlotIds.Center && u.Alive) return u;
            return null;
        }

        private static bool Encircled(BattleContext ctx, CorpsState c, SubUnitState g)
        {
            var k = ctx.Combat;
            Fix r2 = k.CaptureEncircleRadius * k.CaptureEncircleRadius;
            var taken = new bool[k.CaptureSectors];
            long allies = 0;
            foreach (var o in ctx.Corps)
            {
                if (!o.InBattle) continue;
                foreach (var u in o.Units)
                {
                    if (!u.Alive || Vec.DistanceSquared(u.Position, g.Position) > r2) continue;
                    if (o.Team == c.Team) { allies += u.Troops; continue; }
                    taken[Sector(u.Position - g.Position, k.CaptureSectors)] = true;
                }
            }
            if (allies > k.CaptureAllyTroopsAtOrBelow) return false;
            int n = 0;
            foreach (var t in taken) if (t) n++;
            return Fix.FromInt(n) >= k.CaptureSectorRatio * k.CaptureSectors;
        }

        private static readonly Fix Sin22 = Fix.Parse("0.3827"); // sin 22.5°: 8방향 경계

        /// <summary>방향 → 칸 번호. 8방향(북에서 시계 방향 0~7)으로 나눈 뒤 sectors 칸에 맞춘다 (정수 판정, 결정적).</summary>
        private static int Sector(Vec d, int sectors)
        {
            Vec n = d.Normalized();
            int ix = n.X > Sin22 ? 1 : (n.X < -Sin22 ? -1 : 0);
            int iy = n.Y > Sin22 ? 1 : (n.Y < -Sin22 ? -1 : 0);
            int s8;
            if (iy < 0) s8 = ix < 0 ? 7 : (ix == 0 ? 0 : 1);       // 북서, 북, 북동
            else if (iy == 0) s8 = ix < 0 ? 6 : 2;                  // 서, 동 (가운데는 d = 0일 때뿐 → 동쪽으로)
            else s8 = ix < 0 ? 5 : (ix == 0 ? 4 : 3);               // 남서, 남, 남동
            return s8 * sectors / 8;
        }

        private static string EncirclingTeam(BattleContext ctx, CorpsState c, SubUnitState g)
        {
            string best = null;
            Fix bestD = Fix.Zero;
            foreach (var o in ctx.Corps)
            {
                if (o.Team == c.Team || !o.InBattle) continue;
                foreach (var u in o.Units)
                {
                    if (!u.Alive) continue;
                    Fix d = Vec.DistanceSquared(u.Position, g.Position);
                    if (best == null || d < bestD) { best = o.Team; bestD = d; }
                }
            }
            return best;
        }
    }
}
