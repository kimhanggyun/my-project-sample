using System.Collections.Generic;
using Game.Data.Json;
using Game.Foundation.Math;
using Newtonsoft.Json.Linq;

namespace Game.Data.Defs
{
    /// <summary>병종 승급 하나: from → to, 조건(훈련도·스텝)과 비용.</summary>
    public sealed class PromotionDef
    {
        public string From { get; internal set; }
        public string To { get; internal set; }
        public Fix Training { get; internal set; }
        /// <summary>클리어해야 하는 스텝 (없으면 null).</summary>
        public string Step { get; internal set; }
        public long Gold { get; internal set; }
    }

    /// <summary>
    /// 부대 키우기 데이터 (Rules/troopGrowth.json, verification R-52). 검사: 훈련 값 범위, 승급의 병종이 있고 to의 부모가 from, 같은 승급 두 번 없음.
    /// 파일이 없으면 훈련·승급 없음.
    /// </summary>
    public sealed class TroopGrowthRules
    {
        public const string File = "troopGrowth.json";
        public Fix TrainingStep { get; private set; }
        public Fix TrainingMax { get; private set; }
        public long TrainingGoldPer1000 { get; private set; }
        public IReadOnlyList<PromotionDef> Promotions { get; private set; } = new List<PromotionDef>();
        public bool Enabled { get; private set; }

        public static readonly TroopGrowthRules None = new TroopGrowthRules();

        public static TroopGrowthRules From(JObject o, IReadOnlyDictionary<string, TroopTypeDef> troops)
        {
            var t = JsonUtil.RequireObject(o, "training", File);
            var r = new TroopGrowthRules
            {
                Enabled = true,
                TrainingStep = JsonUtil.RequireFix(t, "step", File + ".training"),
                TrainingMax = JsonUtil.RequireFix(t, "max", File + ".training"),
                TrainingGoldPer1000 = JsonUtil.RequireLong(t, "goldPer1000", File + ".training"),
            };
            if (r.TrainingStep <= Fix.Zero || r.TrainingMax <= Fix.Zero || r.TrainingMax > Fix.OneValue || r.TrainingGoldPer1000 < 0)
                throw new DataException($"{File}.training: step·max는 0보다 크고 max ≤ 1, 비용은 0 이상.");
            var list = new List<PromotionDef>();
            var seen = new HashSet<string>();
            foreach (JObject p in (JArray)o["promotions"] ?? new JArray())
            {
                var d = new PromotionDef
                {
                    From = JsonUtil.RequireString(p, "from", File),
                    To = JsonUtil.RequireString(p, "to", File),
                    Training = JsonUtil.FixOr(p, "training", Fix.Zero, File),
                    Step = p.Value<string>("step"),
                    Gold = p.Value<long?>("gold") ?? 0,
                };
                if (!troops.TryGetValue(d.From, out _) || !troops.TryGetValue(d.To, out var to)) throw new DataException($"{File}: 없는 병종 '{d.From}' → '{d.To}'.");
                if (to.Parent != d.From) throw new DataException($"{File}: '{d.To}'의 부모는 '{to.Parent}'인데 '{d.From}'에서 승급합니다.");
                if (!seen.Add(d.From + ">" + d.To)) throw new DataException($"{File}: 같은 승급 '{d.From}' → '{d.To}'이 두 번.");
                list.Add(d);
            }
            r.Promotions = list;
            return r;
        }
    }
}
