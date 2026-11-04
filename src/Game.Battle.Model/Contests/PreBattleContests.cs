using System.Collections.Generic;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;
using Game.Foundation.Math;
using Newtonsoft.Json.Linq;

namespace Game.Battle.Model.Contests
{
    /// <summary>
    /// 전투 직전 대결을 돌리고 결과를 전투에 반영한다 (battle.md 2.7.5, 2.7.6).
    /// 일기토: 승리 → 사기, 퇴각 → 퇴각한 쪽 사기↓, 쓰러짐 → 혼란 뒤 퇴각 + 중상(R-23). 논쟁: 책략 점수·사기.
    /// </summary>
    internal static class PreBattleContests
    {
        public const string GeneralInjuryFlag = "generalInjury";

        public static List<ContestResult> Run(BattleContext ctx, List<ContestSetup> contests, Dictionary<string, string> injuries)
        {
            var results = new List<ContestResult>();
            foreach (var cs in contests)
            {
                var def = ctx.Defs.Contests[cs.ContestId];
                if (!ctx.IsEnabled(def.Flag)) continue;
                var ca = ctx.FindCorps(cs.CorpsA);
                var cb = ctx.FindCorps(cs.CorpsB);
                var a = new ContestFighter(ca.Setup.General.Id, ca.Setup.General.Stat(def.Stat), ca.Setup.General.Temperament);
                var b = new ContestFighter(cb.Setup.General.Id, cb.Setup.General.Stat(def.Stat), cb.Setup.General.Temperament);
                var session = new ContestSession(def, a, b, ctx.Random);
                var ai = new AiContestChooser(ctx.Defs.Ai, ctx.Random);
                var r = ContestRunner.Run(session, new RecordedChooser(cs.ChoicesA, ai), new RecordedChooser(cs.ChoicesB, ai));
                results.Add(r);
                Apply(ctx, def, r, ca, cb, injuries);
            }
            return results;
        }

        private static void Apply(BattleContext ctx, ContestDef def, ContestResult r, CorpsState ca, CorpsState cb, Dictionary<string, string> injuries)
        {
            bool injuryOn = ctx.IsEnabled(GeneralInjuryFlag);
            if (!def.BattleEffects.TryGetValue(r.End, out var fx)) fx = new JObject();
            if (r.End == ContestEnds.Retreat)
            {
                var who = r.Retreater == "A" ? ca : cb;
                Morale(who, fx, "retreaterMorale");
            }
            else if (r.Winner != null)
            {
                var w = r.Winner == "A" ? ca : cb;
                var l = r.Winner == "A" ? cb : ca;
                Morale(w, fx, "winnerMorale");
                Morale(l, fx, "loserMorale");
                if (fx["winnerPoints"] != null) w.SkillPoints.Adjust(Fix.FromDecimal(fx.Value<decimal>("winnerPoints")));
                if (fx["loserPoints"] != null) l.SkillPoints.Adjust(Fix.FromDecimal(fx.Value<decimal>("loserPoints")));
                if (fx["loserConfusionSec"] != null)
                {
                    long ticks = Skills.SkillRules.Ticks(ctx, Fix.FromDecimal(fx.Value<decimal>("loserConfusionSec")));
                    foreach (var u in l.Units) u.ConfusedTicks = System.Math.Max(u.ConfusedTicks, ticks);
                    if (fx.Value<bool?>("loserRetreats") == true)
                        l.AddStatus(new StatusEffect { Id = StatusIds.Routing, TicksLeft = ticks });
                }
                if (injuryOn && fx.Value<string>("loserInjury") is string inj) injuries[l.Setup.General.Id] = inj;
            }
            if (injuryOn && def.LightInjuryAtOrBelow.HasValue)
                foreach (var (f, c) in new[] { (r.A, ca), (r.B, cb) })
                    if (!f.Fallen && f.Resource <= def.LightInjuryAtOrBelow.Value && !injuries.ContainsKey(c.Setup.General.Id))
                        injuries[c.Setup.General.Id] = "light";
        }

        private static void Morale(CorpsState c, JObject fx, string key)
        {
            if (fx[key] == null) return;
            c.Morale = Fix.Clamp(c.Morale + Fix.FromDecimal(fx.Value<decimal>(key)), Fix.Zero, Fix.Hundred);
        }
    }
}
