using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Game.Foundation.Math;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Data.Json
{
    public static class JsonUtil
    {
        /// <summary>
        /// 소수는 decimal로 읽는다 (double을 거치지 않아 기기와 무관하게 같은 값).
        /// </summary>
        public static JToken Parse(string text)
        {
            using (var reader = new JsonTextReader(new StringReader(text)))
            {
                reader.FloatParseHandling = FloatParseHandling.Decimal;
                reader.DateParseHandling = DateParseHandling.None;
                return JToken.ReadFrom(reader);
            }
        }

        public static JObject ParseObject(string text)
        {
            var token = Parse(text);
            if (token is JObject o) return o;
            throw new DataException("JSON 최상위가 객체가 아닙니다.");
        }

        public static Fix ToFix(JToken token, string context)
        {
            if (token == null || token.Type == JTokenType.Null)
                throw new DataException($"{context}: 값이 없습니다.");
            switch (token.Type)
            {
                case JTokenType.Integer: return Fix.FromInt(token.Value<long>());
                case JTokenType.Float: return Fix.FromDecimal(token.Value<decimal>());
                case JTokenType.String: return Fix.Parse(token.Value<string>());
                default: throw new DataException($"{context}: 숫자가 아닙니다 ({token.Type}).");
            }
        }

        public static Fix FixOr(JObject obj, string key, Fix fallback, string context) =>
            obj.TryGetValue(key, out var t) ? ToFix(t, context + "." + key) : fallback;

        public static Fix RequireFix(JObject obj, string key, string context)
        {
            if (!obj.TryGetValue(key, out var t)) throw new DataException($"{context}.{key}: 필수 값이 없습니다.");
            return ToFix(t, context + "." + key);
        }

        public static long RequireLong(JObject obj, string key, string context)
        {
            if (!obj.TryGetValue(key, out var t) || t.Type != JTokenType.Integer)
                throw new DataException($"{context}.{key}: 정수가 필요합니다.");
            return t.Value<long>();
        }

        public static string RequireString(JObject obj, string key, string context)
        {
            if (!obj.TryGetValue(key, out var t) || t.Type != JTokenType.String)
                throw new DataException($"{context}.{key}: 문자열이 필요합니다.");
            return t.Value<string>();
        }

        public static JObject RequireObject(JObject obj, string key, string context)
        {
            if (!obj.TryGetValue(key, out var t) || !(t is JObject o))
                throw new DataException($"{context}.{key}: 객체가 필요합니다.");
            return o;
        }

        public static List<string> StringList(JObject obj, string key)
        {
            var list = new List<string>();
            if (obj.TryGetValue(key, out var t) && t is JArray arr)
                foreach (var item in arr) list.Add(item.Value<string>());
            return list;
        }

        /// <summary>
        /// 덮어쓰기 병합: 객체는 키별로 재귀 병합, 배열·값은 통째로 교체, null은 키 삭제.
        /// overlay를 바꾸지 않고 새 객체를 돌려준다.
        /// </summary>
        public static JObject DeepMerge(JObject baseObj, JObject overlay)
        {
            var result = (JObject)baseObj.DeepClone();
            if (overlay == null) return result;
            foreach (var prop in overlay.Properties())
            {
                if (prop.Value.Type == JTokenType.Null) { result.Remove(prop.Name); continue; }
                if (prop.Value is JObject overlayChild && result[prop.Name] is JObject baseChild)
                    result[prop.Name] = DeepMerge(baseChild, overlayChild);
                else
                    result[prop.Name] = prop.Value.DeepClone();
            }
            return result;
        }

        public static string Invariant(long v) => v.ToString(CultureInfo.InvariantCulture);
    }

    public sealed class DataException : Exception
    {
        public DataException(string message) : base(message) { }
    }
}
