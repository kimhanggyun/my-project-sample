using System.Collections.Generic;
using Game.Data.Json;
using Newtonsoft.Json.Linq;

namespace Game.Data.Rules
{
    /// <summary>
    /// 수치 덮어쓰기 순서를 한 곳에서 처리한다 (verification R-18).
    /// 공통 기본값 → 스텝 → 규칙 세트(대전 등) 순으로, 나중 것이 이긴다.
    /// </summary>
    public static class BalanceResolver
    {
        public static JObject Resolve(JObject common, JObject stepOverride = null, JObject ruleSetOverride = null)
        {
            var merged = JsonUtil.DeepMerge(common, stepOverride);
            return JsonUtil.DeepMerge(merged, ruleSetOverride);
        }

        public static JObject Resolve(IEnumerable<JObject> layersInOrder)
        {
            JObject result = new JObject();
            foreach (var layer in layersInOrder) result = JsonUtil.DeepMerge(result, layer);
            return result;
        }
    }
}
