using System.Collections.Generic;
using Game.Data.Json;
using Game.Foundation.Math;
using Newtonsoft.Json.Linq;

namespace Game.Data.Defs
{
    /// <summary>연습 대결 상대 하나 (대결 능력치 하나로 겨룬다: 일기토는 무력, 논쟁은 지략 자리에).</summary>
    public sealed class PracticeOpponentDef
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        public Fix Stat { get; internal set; }
        public string Temperament { get; internal set; }
    }

    /// <summary>
    /// 연습 대결 (Rules/practice.json, R-59). 읽을 때 검사: 상대 id가 겹치지 않음, 성향이 있음, 경험치는 0 이상.
    /// 파일이 없으면 연습 대결 없음.
    /// </summary>
    public sealed class PracticeRules
    {
        public const string File = "practice.json";
        public static readonly PracticeRules None = new PracticeRules();

        /// <summary>하루(서버 시각 기준) 연습 횟수 상한. 0이면 제한 없음.</summary>
        public int DailyLimit { get; private set; }
        /// <summary>결과(win/draw/loss/retreat) → 경험치.</summary>
        public IReadOnlyDictionary<string, long> Exp { get; private set; } = new Dictionary<string, long>();
        public IReadOnlyList<PracticeOpponentDef> Opponents { get; private set; } = new List<PracticeOpponentDef>();
        public bool Enabled => Opponents.Count > 0;

        public PracticeOpponentDef Opponent(string id)
        {
            foreach (var o in Opponents) if (o.Id == id) return o;
            return null;
        }

        public static PracticeRules From(JObject o, IReadOnlyDictionary<string, TemperamentDef> temperaments)
        {
            var r = new PracticeRules { DailyLimit = (int)(o.Value<long?>("dailyLimit") ?? 0) };
            if (r.DailyLimit < 0) throw new DataException(File + ": dailyLimit는 0 이상.");
            var exp = new Dictionary<string, long>();
            if (o["exp"] is JObject e)
                foreach (var p in e.Properties())
                {
                    long v = p.Value.Value<long>();
                    if (v < 0) throw new DataException($"{File}: exp.{p.Name}는 0 이상.");
                    exp[p.Name] = v;
                }
            r.Exp = exp;
            var list = new List<PracticeOpponentDef>();
            foreach (JObject x in (JArray)o["opponents"] ?? new JArray())
            {
                var d = new PracticeOpponentDef
                {
                    Id = JsonUtil.RequireString(x, "id", File),
                    NameKey = JsonUtil.RequireString(x, "nameKey", File),
                    Stat = JsonUtil.RequireFix(x, "stat", File),
                    Temperament = JsonUtil.RequireString(x, "temperament", File),
                };
                if (list.Exists(p => p.Id == d.Id)) throw new DataException($"{File}: 상대 '{d.Id}'가 겹칩니다.");
                if (!temperaments.ContainsKey(d.Temperament)) throw new DataException($"{File}: 상대 '{d.Id}'의 성향 '{d.Temperament}'이 없습니다.");
                list.Add(d);
            }
            r.Opponents = list;
            return r;
        }
    }
}
