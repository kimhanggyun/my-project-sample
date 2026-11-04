using System.Collections.Generic;
using Game.Data.Defs;
using Game.Data.Rules;

namespace Game.Battle.Model.Events
{
    /// <summary>말풍선 하나 (화면은 카드 강조 + 말풍선으로 보여준다).</summary>
    public sealed class Speech
    {
        public long Tick { get; internal set; }
        public string CorpsId { get; internal set; }
        /// <summary>"report" / "warning" / "shout".</summary>
        public string Kind { get; internal set; }
        /// <summary>테마 문자열 키.</summary>
        public string LineKey { get; internal set; }
        public int Priority { get; internal set; }
    }

    /// <summary>
    /// 장수 말풍선 공통 시스템 (battle.md 4.4): 전투 이벤트 기록 → 말풍선. 이벤트에서만 만들므로 리플레이에서도 같은 말풍선이 나온다.
    /// 같은 틱이면 우선순위(보고 > 경고 > 외침) 순서, 같은 군단·같은 상황은 최소 간격. AI 군단은 보고를 띄우지 않는다.
    /// 스위치 generalSpeech.
    /// </summary>
    public sealed class SpeechPlanner
    {
        public const string GeneralSpeechFlag = "generalSpeech";

        private readonly SpeechRules _rules;
        private readonly IReadOnlyDictionary<string, string> _strings;
        private readonly bool _on;
        private readonly long _minTicks;
        private readonly System.Func<string, bool> _isAi;
        private readonly Dictionary<string, long> _last = new Dictionary<string, long>();

        /// <param name="isAiCorps">군단 id → AI 군단인지 (보고 숨김).</param>
        public SpeechPlanner(DefinitionSet defs, RuleFlags flags, Game.Foundation.Math.Fix tickSeconds, System.Func<string, bool> isAiCorps)
        {
            _rules = defs.Speech;
            _strings = defs.Strings;
            _on = flags.IsEnabled(GeneralSpeechFlag);
            _minTicks = (_rules.MinIntervalSec / tickSeconds).CeilToLong();
            _isAi = isAiCorps ?? (_ => false);
        }

        /// <summary>이벤트 하나 → 말풍선 (없으면 null). 실시간으로 이벤트가 생길 때마다 부른다.</summary>
        public Speech Feed(BattleEvent e)
        {
            if (!_on || !_rules.Situations.TryGetValue(e.Kind, out var s)) return null;
            if (s.Kind == "report" && _isAi(e.CorpsId)) return null;
            string key = e.CorpsId + "|" + e.Kind;
            if (_last.TryGetValue(key, out var last) && e.Tick - last < _minTicks) return null;
            _last[key] = e.Tick;
            string line = s.LineKey;
            if (s.PerDataKeyPrefix != null && e.Data != null && _strings.ContainsKey(s.PerDataKeyPrefix + e.Data)) line = s.PerDataKeyPrefix + e.Data;
            return new Speech { Tick = e.Tick, CorpsId = e.CorpsId, Kind = s.Kind, LineKey = line, Priority = _rules.KindPriority[s.Kind] };
        }

        /// <summary>전체 기록 → 말풍선 목록 (틱 순, 같은 틱은 우선순위 높은 것 먼저).</summary>
        public List<Speech> Plan(IEnumerable<BattleEvent> events)
        {
            var list = new List<Speech>();
            foreach (var e in events)
            {
                var sp = Feed(e);
                if (sp != null) list.Add(sp);
            }
            var indexed = new List<(Speech s, int i)>();
            for (int i = 0; i < list.Count; i++) indexed.Add((list[i], i));
            indexed.Sort((a, b) => a.s.Tick != b.s.Tick ? a.s.Tick.CompareTo(b.s.Tick)
                : a.s.Priority != b.s.Priority ? b.s.Priority.CompareTo(a.s.Priority) : a.i.CompareTo(b.i));
            var sorted = new List<Speech>(list.Count);
            foreach (var x in indexed) sorted.Add(x.s);
            return sorted;
        }
    }
}
