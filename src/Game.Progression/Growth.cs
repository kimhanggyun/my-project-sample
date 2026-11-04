using System.Collections.Generic;
using Game.Data.Defs;
using Game.Data.Rules;
using Game.Foundation.Math;

namespace Game.Progression
{
    /// <summary>기록 카운터 id (general-growth.md 2.2).</summary>
    public static class Counters
    {
        public static string Wins(string track) => track + "Wins";
        public static string Losses(string track) => track + "Losses";
        public static string Retreats(string track) => track + "Retreats";
        public static string Draws(string track) => track + "Draws";
        public static string Streak(string track) => track + "Streak";
        /// <summary>승 − 패 (계산 카운터).</summary>
        public static string WinLossDiff(string track) => track + "WinLossDiff";
    }

    public sealed class GrowthReward
    {
        public string Track { get; internal set; }
        public string RuleKey { get; internal set; }
        public string Stat { get; internal set; }
        public Fix Add { get; internal set; }
        public string Trait { get; internal set; }
    }

    /// <summary>
    /// 기록 카운터 + 보상 규칙 엔진 (general-growth.md 2.3). 카운터가 "몇마다" 또는 "몇 이상(한 번만)" 조건을 넘으면 보상.
    /// 이미 받은 보상 횟수를 함께 기록해 같은 보상을 두 번 주지 않는다. 스위치는 트랙마다 (duelGrowth 등).
    /// 상태(카운터·받은 횟수)는 부르는 쪽이 가진 사전을 그대로 받는다 (주인은 Campaign).
    /// </summary>
    public static class GrowthEngine
    {
        /// <summary>대결 결과 하나를 카운터에 더한다. outcome: "win" / "loss" / "retreat" / "draw".</summary>
        public static void RecordContest(IDictionary<string, long> counters, string track, string outcome)
        {
            void Inc(string k, long d = 1) { counters.TryGetValue(k, out var v); counters[k] = v + d; }
            switch (outcome)
            {
                case "win": Inc(Counters.Wins(track)); Inc(Counters.Streak(track)); break;
                case "loss": Inc(Counters.Losses(track)); counters[Counters.Streak(track)] = 0; break;
                case "retreat": Inc(Counters.Retreats(track)); counters[Counters.Streak(track)] = 0; break;
                default: Inc(Counters.Draws(track)); break;
            }
            counters.TryGetValue(Counters.Wins(track), out var w);
            counters.TryGetValue(Counters.Losses(track), out var l);
            counters[Counters.WinLossDiff(track)] = w - l;
        }

        /// <summary>새로 받을 보상 목록. granted에 받은 횟수를 기록한다.</summary>
        public static List<GrowthReward> Evaluate(IReadOnlyList<GrowthTrackDef> tracks, RuleFlags flags,
            IReadOnlyDictionary<string, long> counters, IDictionary<string, long> granted)
        {
            var rewards = new List<GrowthReward>();
            foreach (var t in tracks)
            {
                if (t.Flag != null && !flags.IsEnabled(t.Flag)) continue;
                for (int i = 0; i < t.Rules.Count; i++)
                {
                    var r = t.Rules[i];
                    string key = t.Track + "#" + i + ":" + r.Counter;
                    counters.TryGetValue(r.Counter, out var value);
                    granted.TryGetValue(key, out var already);
                    long due = 0;
                    if (r.Every > 0) due = value / r.Every;
                    else if (r.AtLeast.HasValue && value >= r.AtLeast.Value) due = 1;
                    if (r.Once && due > 1) due = 1;
                    for (long n = already; n < due; n++)
                        rewards.Add(new GrowthReward { Track = t.Track, RuleKey = key, Stat = r.RewardStat, Add = r.RewardAdd, Trait = r.RewardTrait });
                    if (due > already) granted[key] = due;
                }
            }
            return rewards;
        }
    }

    /// <summary>부대 결속 (battle.md 2.19.1, 스위치 cohesionGain): 승리하면 쌓이고 패배하면 조금 줄어든다.</summary>
    public static class Cohesion
    {
        public const string CohesionGainFlag = "cohesionGain";

        public static Fix After(CampaignRules rules, RuleFlags flags, Fix current, bool won)
        {
            if (!flags.IsEnabled(CohesionGainFlag)) return current;
            Fix next = won ? current + rules.CohesionWinGain : current - rules.CohesionLossPenalty;
            return Fix.Clamp(next, Fix.Zero, rules.CohesionMax);
        }
    }
}
