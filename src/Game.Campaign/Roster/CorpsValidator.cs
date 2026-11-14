using System.Collections.Generic;
using Game.Campaign.Troops;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Campaign.Roster
{
    /// <summary>
    /// 편성 검사 (verification R-5, army-v2.md 4장): 군단 수 ≤ 계급의 출진 군단 수, 군단마다 병종 칸 수 ≤ 계급의 칸 수·같은 병종 두 칸 금지·인원 1 이상,
    /// 병종마다 모든 군단에 넣은 인원 합 ≤ 그 묶음 병력, 군단 병력 합 ≤ 장수 통솔 한도, 장수가 출전 가능한지, 배운 진형인지.
    /// </summary>
    public static class CorpsValidator
    {
        public static List<string> Validate(DefinitionSet defs, CampaignState state, IReadOnlyList<CorpsComposition> compositions)
        {
            var errors = new List<string>();
            var rank = Army.Rank(defs, state);
            if (compositions.Count > rank.Corps) errors.Add($"군단 {compositions.Count}개가 계급의 출진 군단 수 {rank.Corps}를 넘음.");
            var asked = new Dictionary<string, long>();
            foreach (var c in compositions)
                if (c.Formation == null || !state.LearnedFormations.Contains(c.Formation)) errors.Add($"장수 '{c.GeneralId}' 군단: 배우지 않은 진형 '{c.Formation}'.");
            var usedGenerals = new HashSet<string>();
            foreach (var c in compositions)
            {
                var general = state.FindGeneral(c.GeneralId);
                if (general == null) { errors.Add($"없는 장수 '{c.GeneralId}'."); continue; }
                if (!usedGenerals.Add(c.GeneralId)) errors.Add($"장수 '{c.GeneralId}'가 두 군단에 들어감.");
                if (!GeneralStatus.CanDeploy(general.Status)) errors.Add($"장수 '{c.GeneralId}'는 출전할 수 없는 상태 ({general.Status}).");

                long total = 0;
                int own = Army.OwnPicks(c);
                if (own > rank.Slots) errors.Add($"장수 '{c.GeneralId}' 군단: 병종 칸 {own}개가 계급의 칸 수 {rank.Slots}를 넘음.");
                if (c.Picks.Count > Army.MaxPicksPerCorps) errors.Add($"장수 '{c.GeneralId}' 군단: 칸 {c.Picks.Count}개가 진형 칸 수 {Army.MaxPicksPerCorps}를 넘음.");
                var inCorps = new HashSet<string>();
                foreach (var p in c.Picks)
                {
                    var unit = state.FindUnit(p.UnitId);
                    if (unit == null) { errors.Add($"없는 병종 묶음 '{p.UnitId}'."); continue; }
                    if (!inCorps.Add(p.UnitId)) errors.Add($"장수 '{c.GeneralId}' 군단: 병종 '{unit.TroopType}'이 두 칸에 들어감.");
                    if (p.Troops < 1) errors.Add($"장수 '{c.GeneralId}' 군단: '{unit.TroopType}' 인원 {p.Troops}.");
                    asked[p.UnitId] = (asked.TryGetValue(p.UnitId, out var a) ? a : 0) + p.Troops;
                    total += p.Troops;
                }
                if (c.Picks.Count == 0) errors.Add($"장수 '{c.GeneralId}' 군단: 병종 칸이 비어 있음.");
                long cap = TroopCapCalculator.Cap(defs.Balance, EffectiveStat(defs, general, "leadership"));
                if (total > cap) errors.Add($"장수 '{c.GeneralId}' 군단 병력 {total}이 한도 {cap}를 넘음 ({total - cap} 초과).");
            }
            foreach (var kv in asked)
            {
                var u = state.FindUnit(kv.Key);
                if (u != null && kv.Value > u.Troops) errors.Add($"병종 '{u.TroopType}': 편성 인원 합 {kv.Value}이 보유 병력 {u.Troops}을 넘음.");
            }
            return errors;
        }

        /// <summary>정의 능력치 + 성장분, 경상이면 × 경상 배율 (battle.md 2.5.3).</summary>
        public static Fix EffectiveStat(DefinitionSet defs, GeneralState g, string statId)
        {
            Fix v = defs.Generals[g.GeneralId].Stats.Get(statId);
            if (g.StatGrowth.TryGetValue(statId, out var add)) v += add;
            if (g.Status == GeneralStatus.LightInjury) v = v * defs.Balance.LightInjuryStatMultiplier;
            var def = defs.Stats[statId];
            return Fix.Clamp(v, def.Min, def.Max);
        }
    }
}
