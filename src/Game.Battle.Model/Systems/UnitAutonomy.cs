using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Foundation.Math;

namespace Game.Battle.Model.Systems
{
    /// <summary>
    /// 소부대 자율 판단 (battle.md 2.2.2, R-74). 스위치 unitAutonomy. 병종 능력치(judgment 판단력, pursuit 추격)로.
    /// ① 상대 고르기: 닿는 적 중 점수 = −거리 + 판단력/100 × (병종 상성 %p/10 + 약해진 정도 × 3). 판단력이 높을수록 유리한 상대를 고른다.
    /// ② 빈 곳 메우기: 선봉 칸 부대가 쓰러지면 중군의 근접 부대가 선봉 자리로 나선다.
    /// ③ 추격: 지난 틱에 치던 적이 퇴각 중이면 진형 자리에서 그쪽으로 pursuit/25칸까지 쫓아간다.
    /// </summary>
    public static class UnitAutonomy
    {
        public const string Flag = "unitAutonomy";

        public static bool On(BattleContext ctx) => ctx.IsEnabled(Flag);

        /// <summary>상대 점수 (클수록 고름). 판단력이 0이면 가장 가까운 적.</summary>
        public static Fix Score(BattleContext ctx, SubUnitState me, SubUnitState v, Fix distSq)
        {
            Fix score = -Fix.Sqrt(distSq);
            Fix judgment = me.Troop.Stats.Get("judgment") / Fix.Hundred;
            Fix counter = Fix.Zero;
            foreach (var r in ctx.Defs.TroopCounters)
                if (me.Troop.HasTag(r.AttackerTag) && v.Troop.HasTag(r.DefenderTag)) counter += r.Pp;
            Fix weak = v.StartTroops > 0 ? Fix.OneValue - Fix.FromRatio(v.Troops, v.StartTroops) : Fix.Zero;
            return score + judgment * (counter / Fix.FromInt(10) + weak * Fix.FromInt(3));
        }

        /// <summary>그 칸에 설 부대의 자리: 선봉이 비면 중군 근접 부대가 선봉 자리로.</summary>
        public static string SlotOf(CorpsState corps, SubUnitState u)
        {
            if (u.Slot != "center" || !u.Troop.HasTag(Tags.Melee) || !corps.Formation.Slots.ContainsKey("vanguard")) return u.Slot;
            foreach (var o in corps.Units)
                if (o.Slot == "vanguard") return o.Alive ? u.Slot : "vanguard";
            return "vanguard"; // 선봉 칸 부대가 처음부터 없으면 중군이 앞에 선다
        }

        /// <summary>추격: 퇴각하는 적을 치던 부대는 자리에서 그쪽으로 조금 나간다.</summary>
        public static Vec Pursue(BattleContext ctx, SubUnitState u, Vec slotPos)
        {
            var t = u.AttackTarget;
            if (t == null || !t.Alive) return slotPos;
            CorpsState tc = null;
            foreach (var c in ctx.Corps) if (c.Units.Contains(t)) { tc = c; break; }
            if (tc == null || tc.Status != CorpsStatus.Retreating) return slotPos;
            Vec d = t.Position - slotPos;
            Fix len = d.Length;
            if (len <= Fix.Zero) return slotPos;
            Fix go = Fix.Min(len, u.Troop.Stats.Get("pursuit") / Fix.FromInt(25));
            return slotPos + d.Normalized() * go;
        }
    }
}
