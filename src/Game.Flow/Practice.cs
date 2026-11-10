using System.Collections.Generic;
using Game.Battle.Model.Contests;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Data.Rules;
using Game.Foundation.Random;
using Game.Online;
using Game.Progression;

namespace Game.Flow
{
    /// <summary>대결 전적·성장 보상 반영 (전투 대결·연습 대결 공통, general-growth.md 2.2·2.3).</summary>
    public static class ContestRecords
    {
        /// <summary>outcome: win / loss / retreat / draw. 새로 받은 성장 보상을 돌려준다 (능력치·특성은 이미 반영).</summary>
        public static List<GrowthReward> Record(DefinitionSet defs, RuleFlags flags, GeneralState g, string contestId, string outcome)
        {
            GrowthEngine.RecordContest(g.Records, contestId, outcome);
            var rewards = GrowthEngine.Evaluate(defs.GrowthTracks, flags, g.Records, g.GrantedRewards);
            foreach (var r in rewards)
            {
                if (r.Stat != null)
                {
                    g.StatGrowth.TryGetValue(r.Stat, out var v);
                    g.StatGrowth[r.Stat] = v + r.Add;
                }
                if (r.Trait != null && !g.LearnedTraits.Contains(r.Trait)) g.LearnedTraits.Add(r.Trait);
            }
            return rewards;
        }

        /// <summary>대결 결과에서 한쪽("A"/"B")의 결과.</summary>
        public static string OutcomeOf(ContestResult c, string side) =>
            c.End == ContestEnds.Retreat ? (c.Retreater == side ? "retreat" : "win")
            : c.Winner == null ? "draw" : (c.Winner == side ? "win" : "loss");
    }

    /// <summary>
    /// 연습 대결 한 판 (화면이 한 라운드씩 진행). 내 선택 → 상대 AI 선택 → 라운드 순서라,
    /// 같은 시드와 선택 기록으로 <see cref="PracticeService.Apply"/>가 똑같이 다시 돌린다 (서버 검증).
    /// </summary>
    public sealed class PracticeMatch
    {
        private readonly IContestChooser _opponent;
        public ContestSession Session { get; }
        public string GeneralId { get; }
        public string ContestId { get; }
        public string OpponentId { get; }
        public ulong Seed { get; }
        public List<string> Choices { get; } = new List<string>();
        public bool IsOver => Session.IsOver;

        internal PracticeMatch(ContestSession session, IContestChooser opponent, string generalId, string contestId, string opponentId, ulong seed)
        {
            Session = session;
            _opponent = opponent;
            GeneralId = generalId;
            ContestId = contestId;
            OpponentId = opponentId;
            Seed = seed;
        }

        /// <summary>내 행동 하나 (고를 수 없으면 false).</summary>
        public bool Play(string myAction)
        {
            if (Session.IsOver || !Session.CanChoose(Session.A, myAction)) return false;
            string theirs = _opponent.Choose(Session, Session.B, Session.A);
            Choices.Add(myAction);
            return Session.Play(myAction, theirs);
        }

        /// <summary>끝난 판을 행동으로 (세이브에 반영·서버 제출).</summary>
        public PlayerAction ToAction(long nowUnixSeconds) => new PlayerAction
        {
            Kind = ActionKinds.ContestPractice, GeneralId = GeneralId, ContestId = ContestId, OpponentId = OpponentId,
            Seed = Seed, Choices = new List<string>(Choices), DoneAtUnixSeconds = nowUnixSeconds,
        };
    }

    public sealed class PracticeOutcome
    {
        public ContestResult Result { get; internal set; }
        /// <summary>win / loss / retreat / draw.</summary>
        public string Outcome { get; internal set; }
        public long Exp { get; internal set; }
        public LevelUpResult Level { get; internal set; }
        public List<GrowthReward> Rewards { get; internal set; } = new List<GrowthReward>();
    }

    /// <summary>
    /// 연습 대결 (general-growth.md 2.4, R-59): 장수 키우기에서 AI 상대와 일기토·논쟁.
    /// 결과는 전투 대결과 같은 전적·성장 보상, 결과별 경험치. 하루 횟수 상한은 데이터(0 = 없음, 서버 연동 후 서버 값).
    /// </summary>
    public sealed class PracticeService
    {
        public const string Ok = "ok";
        public const string NoGeneral = "noGeneral";
        public const string Captured = "captured";
        public const string NoContest = "noContest";
        public const string NoOpponent = "noOpponent";
        public const string DailyLimit = "dailyLimit";
        public const long SecondsPerDay = 86400;

        private readonly DefinitionSet _defs;
        private readonly RuleFlags _flags;

        public PracticeService(DefinitionSet defs)
        {
            _defs = defs;
            _flags = defs.ResolveFlags();
        }

        public static long DayOf(long unixSeconds) => unixSeconds / SecondsPerDay;

        /// <summary>오늘 한 연습 횟수.</summary>
        public static int TodayCount(CampaignState s, long nowUnixSeconds) => s.PracticeDay == DayOf(nowUnixSeconds) ? s.PracticeCount : 0;

        public string Check(CampaignState state, string generalId, string contestId, string opponentId, long nowUnixSeconds)
        {
            var g = generalId == null ? null : state.FindGeneral(generalId);
            if (g == null) return NoGeneral;
            if (g.Status == GeneralStatus.Captured) return Captured;
            if (contestId == null || !_defs.Contests.ContainsKey(contestId)) return NoContest;
            if (opponentId == null || _defs.Practice.Opponent(opponentId) == null) return NoOpponent;
            int limit = _defs.Practice.DailyLimit;
            if (limit > 0 && TodayCount(state, nowUnixSeconds) >= limit) return DailyLimit;
            return Ok;
        }

        /// <summary>새 판 (검사를 통과하지 못하면 null).</summary>
        public PracticeMatch Start(CampaignState state, string generalId, string contestId, string opponentId, ulong seed, long nowUnixSeconds)
        {
            if (Check(state, generalId, contestId, opponentId, nowUnixSeconds) != Ok) return null;
            var def = _defs.Contests[contestId];
            var g = state.FindGeneral(generalId);
            var opp = _defs.Practice.Opponent(opponentId);
            var rng = new SeededRandom(seed);
            var me = new ContestFighter(generalId, CorpsValidator.EffectiveStat(_defs, g, def.Stat), _defs.Generals[generalId].Temperament);
            var them = new ContestFighter(opponentId, opp.Stat, opp.Temperament);
            var session = new ContestSession(def, me, them, rng);
            return new PracticeMatch(session, new AiContestChooser(_defs.Ai, rng), generalId, contestId, opponentId, seed);
        }

        /// <summary>
        /// 행동을 세이브에 반영: 같은 시드·선택으로 다시 돌려 (선택이 그 라운드에 고를 수 없거나 모자라거나 남으면 실패),
        /// 전적·성장 보상·경험치·오늘 횟수. 실패하면 null이고 세이브는 그대로.
        /// </summary>
        public PracticeOutcome Apply(CampaignState state, PlayerAction a)
        {
            if (a?.Choices == null || a.Choices.Count == 0) return null;
            long now = a.DoneAtUnixSeconds;
            var match = Start(state, a.GeneralId, a.ContestId, a.OpponentId, a.Seed, now);
            if (match == null) return null;
            foreach (var c in a.Choices)
                if (match.IsOver || !match.Play(c)) return null;
            if (!match.IsOver) return null;

            var g = state.FindGeneral(a.GeneralId);
            var result = match.Session.Result;
            var o = new PracticeOutcome { Result = result, Outcome = ContestRecords.OutcomeOf(result, "A") };
            o.Rewards = ContestRecords.Record(_defs, _flags, g, a.ContestId, o.Outcome);
            o.Exp = _defs.Practice.Exp.TryGetValue(o.Outcome, out var e) ? e : 0;
            if (o.Exp > 0)
            {
                var r = LevelEngine.AddExp(_defs.Levels, _defs.Generals[g.GeneralId].GrowthRates, g.Level, g.Exp, o.Exp, LimitBreakService.MaxLevelOf(_defs, g));
                g.Level = r.Level;
                g.Exp = r.Exp;
                foreach (var kv in r.StatGains)
                {
                    g.StatGrowth.TryGetValue(kv.Key, out var v);
                    g.StatGrowth[kv.Key] = v + kv.Value;
                }
                o.Level = r;
            }
            long day = DayOf(now);
            if (state.PracticeDay != day) { state.PracticeDay = day; state.PracticeCount = 0; }
            state.PracticeCount++;
            return o;
        }

        /// <summary>장수의 대결 전적 한 줄 (승·패·퇴각·무·연승).</summary>
        public static (long wins, long losses, long retreats, long draws, long streak) RecordOf(GeneralState g, string contestId)
        {
            long V(string k) => g.Records.TryGetValue(k, out var v) ? v : 0;
            return (V(Counters.Wins(contestId)), V(Counters.Losses(contestId)), V(Counters.Retreats(contestId)), V(Counters.Draws(contestId)), V(Counters.Streak(contestId)));
        }

        /// <summary>다음 성장 보상까지 (트랙 → 규칙마다: 카운터 값 / 목표, 보상 설명용 정의).</summary>
        public List<(GrowthTrackDef track, GrowthRuleDef rule, long value, long target)> Progress(GeneralState g)
        {
            var list = new List<(GrowthTrackDef, GrowthRuleDef, long, long)>();
            foreach (var t in _defs.GrowthTracks)
            {
                if (t.Flag != null && !_flags.IsEnabled(t.Flag)) continue;
                foreach (var r in t.Rules)
                {
                    g.Records.TryGetValue(r.Counter, out var value);
                    long target;
                    if (r.Every > 0) target = (value / r.Every + 1) * r.Every;
                    else if (r.AtLeast.HasValue) { if (value >= r.AtLeast.Value && r.Once) continue; target = r.AtLeast.Value; }
                    else continue;
                    list.Add((t, r, value, target));
                }
            }
            return list;
        }
    }
}
