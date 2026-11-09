using System.Collections.Generic;
using Game.Data.Json;
using Game.Foundation.Math;
using Newtonsoft.Json.Linq;

namespace Game.Data.Defs
{
    /// <summary>스킬 요구 능력치 하나 (장수 능력치 ≥ min).</summary>
    public sealed class StatRequirement
    {
        public string Stat { get; internal set; }
        public Fix Min { get; internal set; }
    }

    /// <summary>스킬북으로 배울 때의 최소 조건 하나: 장수 기록 카운터 ≥ 값 (generals.md 4장).</summary>
    public sealed class CounterRequirement
    {
        /// <summary>장수 기록 카운터 id (예: "battle.ambushed" = 복병 기습을 당한 횟수).</summary>
        public string Counter { get; internal set; }
        public long AtLeast { get; internal set; }
    }

    /// <summary>
    /// 스킬 정의 (skills.json). 효과·조건은 종류(type) + 매개변수 묶음이다.
    /// 종류별 해석은 전투 모듈의 등록표가 한다 (새 효과는 구현 추가 + 등록, 데이터는 조합만).
    /// </summary>
    public sealed class SkillDef
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        public string Category { get; internal set; }
        public string Tier { get; internal set; }
        public IReadOnlyList<StatRequirement> Requires { get; internal set; }
        /// <summary>true면 요구 중 하나만 맞아도 된다 (예: 도발 = 지략 30 또는 무력 50).</summary>
        public bool RequiresAny { get; internal set; }
        public Fix Cost { get; internal set; }
        public int UsesPerBattle { get; internal set; }
        /// <summary>"self", "enemyCorps", "allyCorps", "point", "area".</summary>
        public string TargetType { get; internal set; }
        /// <summary>사거리 (칸). 없으면 전장 어디든.</summary>
        public Fix? Range { get; internal set; }
        public Fix Radius { get; internal set; }
        /// <summary>전조 시간: 사용 후 효과가 나타나기까지.</summary>
        public Fix TelegraphSec { get; internal set; }
        public IReadOnlyList<SkillPart> Conditions { get; internal set; }
        public IReadOnlyList<SkillPart> Effects { get; internal set; }
        /// <summary>스킬북으로 배울 때의 최소 조건 (비어 있으면 조건 없음). 숨은 보상으로 바로 얻을 때는 보지 않는다.</summary>
        public IReadOnlyList<CounterRequirement> LearnConditions { get; internal set; }
        /// <summary>true면 조건 중 하나만 맞아도 된다.</summary>
        public bool LearnAny { get; internal set; }

        public static SkillDef From(JObject o)
        {
            string id = JsonUtil.RequireString(o, "id", "skills");
            string c = "skills." + id;
            var req = new List<StatRequirement>();
            if (o["requires"] is JArray ra)
                foreach (JObject r in ra)
                    req.Add(new StatRequirement { Stat = JsonUtil.RequireString(r, "stat", c + ".requires"), Min = JsonUtil.RequireFix(r, "min", c + ".requires") });
            var target = JsonUtil.RequireObject(o, "target", c);
            var learn = new List<CounterRequirement>();
            var learnObj = o["learn"] as JObject;
            if (learnObj?["counters"] is JArray la)
                foreach (JObject r in la)
                    learn.Add(new CounterRequirement { Counter = JsonUtil.RequireString(r, "counter", c + ".learn"), AtLeast = JsonUtil.RequireLong(r, "atLeast", c + ".learn") });
            return new SkillDef
            {
                Id = id,
                NameKey = JsonUtil.RequireString(o, "nameKey", c),
                Category = o.Value<string>("category"),
                Tier = o.Value<string>("tier"),
                Requires = req,
                RequiresAny = o.Value<bool?>("requiresAny") ?? false,
                Cost = JsonUtil.RequireFix(o, "cost", c),
                UsesPerBattle = (int)JsonUtil.RequireLong(o, "usesPerBattle", c),
                TargetType = JsonUtil.RequireString(target, "type", c + ".target"),
                Range = target.TryGetValue("range", out var rt) ? JsonUtil.ToFix(rt, c + ".target.range") : (Fix?)null,
                Radius = JsonUtil.FixOr(target, "radius", Fix.Zero, c + ".target"),
                TelegraphSec = JsonUtil.FixOr(o, "telegraphSec", Fix.Zero, c),
                Conditions = Parts(o, "conditions", c),
                Effects = Parts(o, "effects", c),
                LearnConditions = learn,
                LearnAny = learnObj?.Value<bool?>("any") ?? false,
            };
        }

        private static List<SkillPart> Parts(JObject o, string key, string c)
        {
            var list = new List<SkillPart>();
            if (o[key] is JArray arr)
                foreach (JObject p in arr) list.Add(new SkillPart(JsonUtil.RequireString(p, "type", c + "." + key), p, c + "." + key));
            return list;
        }
    }

    /// <summary>효과·조건 하나: 종류 + 매개변수 (읽기 전용).</summary>
    public sealed class SkillPart
    {
        private readonly JObject _params;
        private readonly string _context;

        public string Type { get; }

        internal SkillPart(string type, JObject parameters, string context)
        {
            Type = type;
            _params = (JObject)parameters.DeepClone();
            _context = context + "." + type;
        }

        public Fix Fix(string key) => JsonUtil.RequireFix(_params, key, _context);
        public Fix FixOr(string key, Fix fallback) => JsonUtil.FixOr(_params, key, fallback, _context);
        public string String(string key) => JsonUtil.RequireString(_params, key, _context);
        public bool Has(string key) => _params.ContainsKey(key);
    }
}
