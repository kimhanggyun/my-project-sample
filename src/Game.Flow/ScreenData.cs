using System;
using System.Collections.Generic;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Flow
{
    // 디자인 시안(docs/design/mockups 04·05)이 쓰는 값 — 화면은 이것을 그리기만 한다 (2026-11-14).

    /// <summary>스토리 카드 하나의 상태.</summary>
    public enum StepCardState
    {
        /// <summary>깬 스텝 (다시 할 수 있다).</summary>
        Cleared,
        /// <summary>열렸고 아직 안 깸 (스토리 순서에서 첫 번째가 "다음 도전").</summary>
        Open,
        /// <summary>잠김 (<see cref="StepCard.LockReason"/>).</summary>
        Locked,
    }

    /// <summary>스토리 탭 카드·줄 하나 (시안 04).</summary>
    public sealed class StepCard
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        public StepCardState State { get; internal set; }
        /// <summary>스토리 순서에서 처음 나오는 열린·안 깬 스텝 = "다음 도전" 큰 카드.</summary>
        public bool IsNext { get; internal set; }
        /// <summary>구출 전투 (기한이 있어 빨간 카드).</summary>
        public bool IsRescue { get; internal set; }
        /// <summary>잠긴 까닭: "previous"(앞 스텝을 깨면 열림), "rescue"(갇힌 장수가 없음). 잠기지 않았으면 null.</summary>
        public string LockReason { get; internal set; }

        // ---- 요약 (고르기 전에 보는 것) ----
        public string BattleType { get; internal set; }
        /// <summary>제한 시간 (분, 0 = 없음).</summary>
        public Fix MaxMinutes { get; internal set; }
        public int EnemyCorps { get; internal set; }
        public long EnemyTroops { get; internal set; }
        public int RecommendedLevel { get; internal set; }
        public string Weather { get; internal set; }
        public string TimeOfDay { get; internal set; }

        // ---- 첫 클리어 보상 (이미 깼으면 다시 받지 않는다) ----
        public IReadOnlyDictionary<string, long> RewardResources { get; internal set; }
        public long RewardExp { get; internal set; }
        public IReadOnlyList<string> RewardGenerals { get; internal set; }
        public IReadOnlyList<string> RewardSkills { get; internal set; }
        public IReadOnlyList<string> RewardFormations { get; internal set; }

        // ---- 구출 (E23) ----
        /// <summary>그 수용소에 갇힌 우리 장수 수와 가장 짧은 기한 (구출 스텝만).</summary>
        public int RescueHeld { get; internal set; }
        public long RescueDeadlineUnixSeconds { get; internal set; }
    }

    /// <summary>스토리 탭 (시안 04): 진행도·다음 도전·깬/잠긴 스텝·구출 카드.</summary>
    public static class StoryBoard
    {
        /// <summary>스토리 순서(본편)의 카드들, 그다음 열린 구출 스텝. 훈련(drill)은 <see cref="Drills"/>.</summary>
        public static List<StepCard> Build(DefinitionSet defs, StoryFlow flow, CampaignState state)
        {
            var list = new List<StepCard>();
            bool nextTaken = false;
            foreach (var id in defs.StepIndex.Order)
            {
                var c = Card(defs, flow, state, id, false);
                if (c == null) continue;
                if (c.State == StepCardState.Open && !nextTaken) { c.IsNext = true; nextTaken = true; }
                list.Add(c);
            }
            foreach (var kv in defs.StepIndex.RescueSteps)
                if (kv.Value != null && flow.IsUnlocked(state, kv.Value))
                {
                    var c = Card(defs, flow, state, kv.Value, true);
                    if (c != null) list.Add(c);
                }
            return list;
        }

        /// <summary>진행도: 깬 본편 스텝 수 / 본편 스텝 수.</summary>
        public static (int cleared, int total) Progress(DefinitionSet defs, CampaignState state)
        {
            int n = 0;
            foreach (var id in defs.StepIndex.Order) if (state.ClearedSteps.Contains(id)) n++;
            return (n, defs.StepIndex.Order.Count);
        }

        /// <summary>열린 훈련 전투 스텝 id (반복 가능, 머리 줄 아래 작은 버튼).</summary>
        public static List<string> Drills(DefinitionSet defs, StoryFlow flow, CampaignState state)
        {
            var list = new List<string>();
            foreach (var kv in defs.StepIndex.Drills) if (flow.IsUnlocked(state, kv.Key)) list.Add(kv.Key);
            return list;
        }

        public static StepCard Card(DefinitionSet defs, StoryFlow flow, CampaignState state, string id, bool rescue)
        {
            StepDef step;
            try { step = flow.LoadStep(id); } catch (Exception) { return null; }
            if (step == null) return null;
            bool cleared = state.ClearedSteps.Contains(id);
            bool open = flow.IsUnlocked(state, id);
            long troops = 0;
            foreach (var e in step.Enemy) foreach (var u in e.Units) troops += u.Troops;
            var c = new StepCard
            {
                Id = id,
                NameKey = step.NameKey,
                State = cleared ? StepCardState.Cleared : open ? StepCardState.Open : StepCardState.Locked,
                IsRescue = rescue,
                LockReason = cleared || open ? null : step.RescueCamp != null ? "rescue" : "previous",
                BattleType = step.BattleType,
                MaxMinutes = step.MaxMinutes,
                EnemyCorps = step.Enemy.Count,
                EnemyTroops = troops,
                RecommendedLevel = step.RecommendedLevel,
                Weather = step.Weather,
                TimeOfDay = step.TimeOfDay,
                RewardResources = step.RewardResources ?? new Dictionary<string, long>(),
                RewardExp = step.RewardExp,
                RewardGenerals = step.RewardGenerals ?? new List<string>(),
                RewardSkills = step.RewardSkills ?? new List<string>(),
                RewardFormations = step.RewardFormations ?? new List<string>(),
            };
            if (step.RescueCamp != null)
            {
                long soonest = long.MaxValue;
                foreach (var g in state.Generals)
                    if (g.Status == GeneralStatus.Captured && g.CampId == step.RescueCamp) { c.RescueHeld++; soonest = Math.Min(soonest, g.RescueDeadlineUnixSeconds); }
                c.RescueDeadlineUnixSeconds = c.RescueHeld > 0 ? soonest : 0;
            }
            return c;
        }
    }

    /// <summary>장수 탭 정렬 기준 (시안 05).</summary>
    public static class GeneralSorts
    {
        public const string Grade = "grade";
        public const string Level = "level";
        public const string Ready = "ready";
        public static readonly IReadOnlyList<string> All = new[] { Grade, Level, Ready };
    }

    /// <summary>장수 탭 (시안 05): 정렬, 장수마다 할 일 점.</summary>
    public static class GeneralRoster
    {
        /// <summary>등급 순위 (gacha.json grades 순서, 뒤가 높음). 모르는 등급은 −1.</summary>
        public static int GradeRank(DefinitionSet defs, string generalId)
        {
            if (!defs.Generals.TryGetValue(generalId, out var g)) return -1;
            foreach (var gr in defs.Gacha.Grades) if (gr.Id == g.Grade) return gr.Rank;
            return -1;
        }

        /// <summary>
        /// 정렬한 사본. grade = 등급(높은 순) → 레벨(높은 순) → 이름 id. level = 레벨 → 등급 → id.
        /// ready = 출전 가능 먼저 → 등급 → 레벨 → id. 모르는 기준은 grade.
        /// </summary>
        public static List<GeneralState> Sorted(DefinitionSet defs, IEnumerable<GeneralState> generals, string by)
        {
            var list = new List<GeneralState>(generals);
            int Cmp(GeneralState a, GeneralState b)
            {
                int ga = GradeRank(defs, a.GeneralId), gb = GradeRank(defs, b.GeneralId);
                int byGrade = gb.CompareTo(ga), byLevel = b.Level.CompareTo(a.Level);
                int byId = string.CompareOrdinal(a.GeneralId, b.GeneralId);
                switch (by)
                {
                    case GeneralSorts.Level: return byLevel != 0 ? byLevel : byGrade != 0 ? byGrade : byId;
                    case GeneralSorts.Ready:
                        int ra = a.Status == GeneralStatus.Available ? 0 : 1, rb = b.Status == GeneralStatus.Available ? 0 : 1;
                        return ra != rb ? ra.CompareTo(rb) : byGrade != 0 ? byGrade : byLevel != 0 ? byLevel : byId;
                    default: return byGrade != 0 ? byGrade : byLevel != 0 ? byLevel : byId;
                }
            }
            list.Sort(Cmp);
            return list;
        }

        /// <summary>그 장수에게 할 일이 있나 (카드 빨간 점): 가진 스킬북으로 지금 배울 수 있는 스킬, 또는 한계 돌파 가능. 탭 할 일 점(TabAlerts)과 같은 조건.</summary>
        public static bool NeedsAttention(DefinitionSet defs, CampaignState s, string generalId)
        {
            if (new LimitBreakService(defs).Check(s, generalId) == LimitBreakService.Ok) return true;
            var books = new SkillBookService(defs);
            foreach (var kv in s.SkillBooks)
                if (kv.Value > 0 && books.Check(s, generalId, kv.Key) == LearnCheck.Ok) return true;
            return false;
        }
    }
}
