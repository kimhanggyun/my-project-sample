using System.Collections.Generic;
using Game.Data.Json;
using Newtonsoft.Json.Linq;

namespace Game.Data.Rules
{
    /// <summary>
    /// 규칙 스위치. 정의(기본값·상위 스위치)는 ruleFlags.json, 값은 공통 → 스텝 → 규칙 세트로 덮어쓴다.
    /// 상위 스위치가 꺼지면 하위는 꺼진 것으로 본다 (verification R-19).
    /// 외부 서비스 켜고 끄기(서비스 설정)와는 별개다.
    /// </summary>
    public sealed class RuleFlags
    {
        private readonly Dictionary<string, bool> _values;
        private readonly Dictionary<string, string> _parents;

        private RuleFlags(Dictionary<string, bool> values, Dictionary<string, string> parents)
        {
            _values = values;
            _parents = parents;
        }

        /// <param name="definitions">ruleFlags.json의 "flags" 객체: { id: { default, parent? } }</param>
        /// <param name="overrideLayers">순서대로 적용할 { id: bool } 객체들</param>
        public static RuleFlags Build(JObject definitions, params JObject[] overrideLayers)
        {
            var values = new Dictionary<string, bool>();
            var parents = new Dictionary<string, string>();
            foreach (var prop in definitions.Properties())
            {
                var def = prop.Value as JObject ?? throw new DataException($"ruleFlags.{prop.Name}: 객체가 필요합니다.");
                values[prop.Name] = def.Value<bool?>("default") ?? throw new DataException($"ruleFlags.{prop.Name}.default가 없습니다.");
                var parent = def.Value<string>("parent");
                if (parent != null) parents[prop.Name] = parent;
            }
            foreach (var kv in parents)
                if (!values.ContainsKey(kv.Value))
                    throw new DataException($"ruleFlags.{kv.Key}: 없는 상위 스위치 '{kv.Value}'.");

            if (overrideLayers != null)
                foreach (var layer in overrideLayers)
                {
                    if (layer == null) continue;
                    foreach (var prop in layer.Properties())
                    {
                        if (!values.ContainsKey(prop.Name))
                            throw new DataException($"알 수 없는 규칙 스위치 '{prop.Name}'.");
                        values[prop.Name] = prop.Value.Value<bool>();
                    }
                }
            return new RuleFlags(values, parents);
        }

        public bool IsEnabled(string id)
        {
            if (!_values.TryGetValue(id, out var own))
                throw new DataException($"알 수 없는 규칙 스위치 '{id}'.");
            if (!own) return false;
            var guard = 0;
            var current = id;
            while (_parents.TryGetValue(current, out var parent))
            {
                if (!_values[parent]) return false;
                current = parent;
                if (++guard > 32) throw new DataException($"규칙 스위치 '{id}'의 상위 관계가 순환합니다.");
            }
            return true;
        }

        public IEnumerable<string> Ids => _values.Keys;
    }
}
