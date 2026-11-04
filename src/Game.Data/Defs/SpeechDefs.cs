using System.Collections.Generic;
using Game.Data.Json;
using Game.Foundation.Math;
using Newtonsoft.Json.Linq;

namespace Game.Data.Defs
{
    public sealed class SpeechSituationDef
    {
        public string Event { get; internal set; }
        /// <summary>"report" / "warning" / "shout".</summary>
        public string Kind { get; internal set; }
        public string LineKey { get; internal set; }
        /// <summary>있으면 이벤트 추가 값(예: 스킬 id)을 붙인 키를 먼저 찾는다 (speech.skill.fireAttack).</summary>
        public string PerDataKeyPrefix { get; internal set; }
    }

    /// <summary>장수 말풍선 정의 (speech.json, battle.md 4.4).</summary>
    public sealed class SpeechRules
    {
        public Fix MinIntervalSec { get; private set; }
        public IReadOnlyDictionary<string, SpeechSituationDef> Situations { get; private set; }
        public IReadOnlyDictionary<string, int> KindPriority { get; private set; }

        public static SpeechRules From(JObject o)
        {
            var sit = new Dictionary<string, SpeechSituationDef>();
            foreach (var p in JsonUtil.RequireObject(o, "situations", "speech").Properties())
            {
                var s = (JObject)p.Value;
                sit[p.Name] = new SpeechSituationDef
                {
                    Event = p.Name,
                    Kind = JsonUtil.RequireString(s, "kind", "speech." + p.Name),
                    LineKey = JsonUtil.RequireString(s, "lineKey", "speech." + p.Name),
                    PerDataKeyPrefix = s.Value<string>("perDataKeyPrefix"),
                };
            }
            var pri = new Dictionary<string, int>();
            foreach (var p in JsonUtil.RequireObject(o, "kindPriority", "speech").Properties()) pri[p.Name] = p.Value.Value<int>();
            return new SpeechRules { MinIntervalSec = JsonUtil.RequireFix(o, "minIntervalSec", "speech"), Situations = sit, KindPriority = pri };
        }
    }
}
