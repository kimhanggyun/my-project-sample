using System.Collections.Generic;
using Game.Campaign.Troops;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Campaign.Roster
{
    /// <summary>
    /// 편성 검사 (verification R-5): 칸 id가 맞는지, 부대가 있는지, 한 부대는 한 군단에만,
    /// 군단 병력 합 ≤ 장수 통솔 한도, 장수가 출전 가능한지.
    /// </summary>
    public static class CorpsValidator
    {
        public static List<string> Validate(DefinitionSet defs, CampaignState state, IReadOnlyList<CorpsComposition> compositions)
        {
            var errors = new List<string>();
            foreach (var c in compositions)
                if (c.Formation == null || !state.LearnedFormations.Contains(c.Formation)) errors.Add($"장수 '{c.GeneralId}' 군단: 배우지 않은 진형 '{c.Formation}'.");
            var usedUnits = new HashSet<string>();
            var usedGenerals = new HashSet<string>();
            foreach (var c in compositions)
            {
                var general = state.FindGeneral(c.GeneralId);
                if (general == null) { errors.Add($"없는 장수 '{c.GeneralId}'."); continue; }
                if (!usedGenerals.Add(c.GeneralId)) errors.Add($"장수 '{c.GeneralId}'가 두 군단에 들어감.");
                if (!GeneralStatus.CanDeploy(general.Status)) errors.Add($"장수 '{c.GeneralId}'는 출전할 수 없는 상태 ({general.Status}).");

                long total = 0;
                foreach (var slot in c.Slots)
                {
                    if (!SlotIds.IsValid(slot.Key)) { errors.Add($"없는 칸 '{slot.Key}'."); continue; }
                    var unit = state.FindUnit(slot.Value);
                    if (unit == null) { errors.Add($"없는 부대 '{slot.Value}'."); continue; }
                    if (!usedUnits.Add(unit.UnitId)) errors.Add($"부대 '{unit.UnitId}'가 두 군단에 들어감.");
                    total += unit.Troops;
                }
                long cap = TroopCapCalculator.Cap(defs.Balance, EffectiveStat(defs, general, "leadership"));
                if (total > cap) errors.Add($"장수 '{c.GeneralId}' 군단 병력 {total}이 한도 {cap}를 넘음 ({total - cap} 초과).");
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
