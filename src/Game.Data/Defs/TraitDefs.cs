using System.Collections.Generic;
using Game.Data.Json;
using Game.Foundation.Math;
using Newtonsoft.Json.Linq;

namespace Game.Data.Defs
{
    /// <summary>
    /// 특성 효과 하나 (traits.md 3장, R-67). 종류(type)와 값, 그리고 조건(없으면 늘). 새 효과는 종류를 더하고 전투 쪽에서 읽는다.
    /// attackPp / defensePp = 피해 보정 %p (가산, R-1), moraleLossMul = 손실 사기 하락 배율, skillCostPct = 스킬 비용 %(음수면 줄어듦),
    /// contestDamagePct = 일기토·논쟁에서 주는 피해 %.
    /// </summary>
    public sealed class TraitEffect
    {
        public const string AttackPp = "attackPp";
        public const string DefensePp = "defensePp";
        public const string MoraleLossMul = "moraleLossMul";
        public const string SkillCostPct = "skillCostPct";
        public const string ContestDamagePct = "contestDamagePct";
        public static readonly IReadOnlyList<string> Types = new[] { AttackPp, DefensePp, MoraleLossMul, SkillCostPct, ContestDamagePct };

        public string Type { get; internal set; }
        public Fix Value { get; internal set; }
        /// <summary>조건: 공격(방어)하는 소부대가 선 지형 id.</summary>
        public string Terrain { get; internal set; }
        public string TimeOfDay { get; internal set; }
        public string Weather { get; internal set; }
        /// <summary>조건: 소부대 병종 태그 (melee, ranged, mounted …).</summary>
        public string TroopTag { get; internal set; }
        /// <summary>조건: 공격 대상 군단이 퇴각 중.</summary>
        public bool TargetRetreating { get; internal set; }
        /// <summary>조건: 내 군단이 대기(수비) 명령 중.</summary>
        public bool Holding { get; internal set; }
        /// <summary>skillCostPct가 적용되는 스킬 id (비면 모든 스킬).</summary>
        public IReadOnlyList<string> Skills { get; internal set; } = new List<string>();
    }

    public sealed class TraitDef
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        public string DescKey { get; internal set; }
        public IReadOnlyList<TraitEffect> Effects { get; internal set; } = new List<TraitEffect>();
    }

    /// <summary>특성 정의 (Rules/traits.json, R-67). 읽을 때 검사: id 겹침, 모르는 효과 종류. 파일이 없으면 특성 효과 없음.</summary>
    public sealed class TraitRules
    {
        public const string File = "traits.json";
        public static readonly TraitRules None = new TraitRules();

        public IReadOnlyDictionary<string, TraitDef> Traits { get; private set; } = new Dictionary<string, TraitDef>();

        public static TraitRules From(JObject o)
        {
            const string c = "traits.json";
            var map = new Dictionary<string, TraitDef>();
            if (!(o["traits"] is JArray arr)) throw new DataException(c + ".traits: 배열이 필요합니다.");
            foreach (JObject t in arr)
            {
                string id = JsonUtil.RequireString(t, "id", c);
                if (map.ContainsKey(id)) throw new DataException($"{c}: 특성 id '{id}'가 겹칩니다.");
                var effects = new List<TraitEffect>();
                foreach (JObject e in (t["effects"] as JArray) ?? new JArray())
                {
                    string type = JsonUtil.RequireString(e, "type", c + "." + id);
                    if (!((IList<string>)TraitEffect.Types).Contains(type)) throw new DataException($"{c}.{id}: 모르는 효과 '{type}'.");
                    effects.Add(new TraitEffect
                    {
                        Type = type,
                        Value = JsonUtil.RequireFix(e, "value", c + "." + id),
                        Terrain = e.Value<string>("terrain"),
                        TimeOfDay = e.Value<string>("timeOfDay"),
                        Weather = e.Value<string>("weather"),
                        TroopTag = e.Value<string>("troopTag"),
                        TargetRetreating = e.Value<bool?>("targetRetreating") ?? false,
                        Holding = e.Value<bool?>("holding") ?? false,
                        Skills = JsonUtil.StringList(e, "skills"),
                    });
                }
                map[id] = new TraitDef { Id = id, NameKey = t.Value<string>("nameKey") ?? "trait." + id, DescKey = t.Value<string>("descKey") ?? "trait." + id + ".desc", Effects = effects };
            }
            return new TraitRules { Traits = map };
        }
    }
}
