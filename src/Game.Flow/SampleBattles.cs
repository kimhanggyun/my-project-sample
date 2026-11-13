using System.Collections.Generic;
using Game.Campaign;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Economy;

namespace Game.Flow
{
    /// <summary>맵 하나 (샘플전투·맵 탭, R-87): 맵 id, 전장 유형, 그 맵을 쓰는 스텝 (스텝 순서대로).</summary>
    public sealed class SampleMapEntry
    {
        public string MapId { get; set; }
        public string BattleType { get; set; }
        public List<string> Steps { get; } = new List<string>();
    }

    /// <summary>
    /// 샘플전투 (R-87): 맵을 골라 바로 싸워 본다. 세이브 사본으로 출진하므로 보상·군량 소비·부상·진행이 남지 않는다.
    /// 사본은 모든 스텝을 깬 것으로 하고 군량을 넉넉히 채운다. 편성은 내 저장된 편성 그대로 (수상전은 수군 부대가 있어야 한다).
    /// </summary>
    public static class SampleBattles
    {
        /// <summary>사본 군량 (출진 군량이 모자라지 않게).</summary>
        public const long SampleSupply = 1_000_000_000;

        /// <summary>스텝 순서(본편 → 훈련·구출) 기준으로 맵을 모은다.</summary>
        public static List<SampleMapEntry> Catalog(DefinitionSet defs, StoryFlow flow)
        {
            var ids = new List<string>(defs.StepIndex.Order);
            foreach (var kv in defs.StepIndex.Drills) if (!ids.Contains(kv.Key)) ids.Add(kv.Key);
            foreach (var kv in defs.StepIndex.RescueSteps) if (kv.Value != null && !ids.Contains(kv.Value)) ids.Add(kv.Value);
            var list = new List<SampleMapEntry>();
            var byMap = new Dictionary<string, SampleMapEntry>();
            foreach (var id in ids)
            {
                StepDef step;
                try { step = flow.LoadStep(id); } catch (System.Exception) { continue; }
                if (step == null || step.Map == null) continue;
                if (!byMap.TryGetValue(step.Map, out var e))
                {
                    e = new SampleMapEntry { MapId = step.Map, BattleType = step.BattleType };
                    byMap[step.Map] = e;
                    list.Add(e);
                }
                e.Steps.Add(id);
            }
            return list;
        }

        /// <summary>세이브 사본: 모든 스텝 깸, 군량 넉넉히. 원본은 바뀌지 않는다.</summary>
        public static CampaignState Copy(DefinitionSet defs, CampaignState state)
        {
            var copy = SaveJson.Read(SaveJson.Write(state));
            foreach (var id in defs.StepIndex.Order) if (!copy.ClearedSteps.Contains(id)) copy.ClearedSteps.Add(id);
            copy.Wallet[ResourceIds.Supply] = SampleSupply;
            return copy;
        }

        /// <summary>샘플 출진 준비 (사본으로). 위임 여부는 request대로.</summary>
        public static SortiePlan Prepare(DefinitionSet defs, StoryFlow flow, CampaignState state, string stepId, ulong seed, bool delegateAll = false) =>
            flow.Prepare(Copy(defs, state), new SortieRequest { StepId = stepId, Seed = seed, Delegate = delegateAll });
    }
}
