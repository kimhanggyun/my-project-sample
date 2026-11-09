using System;
using System.Collections.Generic;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Online;

namespace Game.Flow
{
    /// <summary>행동 하나를 세이브에 적용하는 처리기. 새 행동은 이것을 구현해 <see cref="PlayerActionRunner.Register"/>로 더한다.</summary>
    public interface IPlayerActionHandler
    {
        string Kind { get; }
        /// <summary>규칙대로 적용하면 true. 적용할 수 없으면 false (세이브는 그대로).</summary>
        bool Apply(CampaignState state, PlayerAction action);
    }

    /// <summary>
    /// 기기 행동 (verification R-41): 기기와 서버가 같은 처리기로 같은 행동을 적용한다.
    /// 기기는 화면에서 행동을 만들어 적용·저장하고 서버로 보낸다. 서버는 세이브를 통째로 믿지 않고 자기 세이브에 다시 적용한다.
    /// </summary>
    public sealed class PlayerActionRunner
    {
        /// <summary>한 번에 받는 행동 수 상한 (서버 부하·악용 방지).</summary>
        public const int MaxPerSubmission = 200;

        private readonly Dictionary<string, IPlayerActionHandler> _handlers = new Dictionary<string, IPlayerActionHandler>(StringComparer.Ordinal);

        public PlayerActionRunner(DefinitionSet defs)
        {
            var troops = new TroopService(defs);
            var books = new SkillBookService(defs);
            Register(new Handler(ActionKinds.FormationSave, (s, a) =>
            {
                if (a.Corps == null) return false;
                var ed = new FormationEditor(defs, s, a.Situation);
                ed.Draft.Clear();
                foreach (var c in a.Corps)
                    ed.Draft.Add(new CorpsComposition { GeneralId = c?.GeneralId, Formation = c?.Formation, Role = c?.Role, Slots = new Dictionary<string, string>(c?.Slots ?? new Dictionary<string, string>()) });
                return ed.Apply();
            }));
            Register(new Handler(ActionKinds.FormationRemovePreset, (s, a) => new FormationEditor(defs, s, a.Situation).RemovePreset()));
            Register(new Handler(ActionKinds.SkillBookBuy, (s, a) => books.Buy(s, a.SkillId)));
            Register(new Handler(ActionKinds.SkillLearn, (s, a) => books.Learn(s, a.GeneralId, a.SkillId) == LearnCheck.Ok));
            Register(new Handler(ActionKinds.UnitConscript, (s, a) => troops.Conscript(s, a.UnitId, a.Count) > 0));
            Register(new Handler(ActionKinds.UnitRecruit, (s, a) => troops.Recruit(s, a.UnitId, a.Count) > 0));
            Register(new Handler(ActionKinds.UnitHeal, (s, a) =>
                (a.PayWith == Economy.ResourceIds.Supply || a.PayWith == Economy.ResourceIds.Gold) && troops.Heal(s, a.UnitId, a.Count, a.PayWith) > 0));
            Register(new Handler(ActionKinds.UnitCreate, (s, a) => troops.Create(s, a.TroopType) != null));
            Register(new Handler(ActionKinds.UnitDisband, (s, a) => troops.Disband(s, a.UnitId)));
        }

        /// <summary>처리기를 더한다 (같은 종류가 있으면 바꾼다).</summary>
        public PlayerActionRunner Register(IPlayerActionHandler handler)
        {
            _handlers[handler.Kind] = handler;
            return this;
        }

        public bool Knows(string kind) => kind != null && _handlers.ContainsKey(kind);

        /// <summary>행동 하나. 모르는 종류이거나 규칙상 안 되면 false.</summary>
        public bool Apply(CampaignState state, PlayerAction action)
        {
            if (state == null || action?.Kind == null || !_handlers.TryGetValue(action.Kind, out var h)) return false;
            return h.Apply(state, action);
        }

        /// <summary>
        /// 행동 묶음을 차례로 적용한다. 하나라도 안 되면 그 순번을 돌려주고 state는 쓰지 말아야 한다 (일부가 이미 바뀌었을 수 있다 → 호출하는 쪽이 사본에 적용).
        /// 모두 되면 -1.
        /// </summary>
        public int ApplyAll(CampaignState state, IReadOnlyList<PlayerAction> actions)
        {
            if (actions == null || actions.Count == 0 || actions.Count > MaxPerSubmission) return 0;
            for (int i = 0; i < actions.Count; i++)
                if (!Apply(state, actions[i])) return i;
            return -1;
        }

        // ---- 화면이 행동을 만드는 곳 ----

        public static PlayerAction SaveFormation(string situation, IEnumerable<CorpsComposition> draft)
        {
            var list = new List<ActionCorps>();
            foreach (var c in draft)
                list.Add(new ActionCorps { GeneralId = c.GeneralId, Formation = c.Formation, Role = c.Role, Slots = new Dictionary<string, string>(c.Slots) });
            return new PlayerAction { Kind = ActionKinds.FormationSave, Situation = situation, Corps = list };
        }

        public static PlayerAction RemovePreset(string situation) => new PlayerAction { Kind = ActionKinds.FormationRemovePreset, Situation = situation };
        public static PlayerAction BuySkillBook(string skillId) => new PlayerAction { Kind = ActionKinds.SkillBookBuy, SkillId = skillId };
        public static PlayerAction LearnSkill(string generalId, string skillId) => new PlayerAction { Kind = ActionKinds.SkillLearn, GeneralId = generalId, SkillId = skillId };
        public static PlayerAction Conscript(string unitId, long count) => new PlayerAction { Kind = ActionKinds.UnitConscript, UnitId = unitId, Count = count };
        public static PlayerAction Recruit(string unitId, long count) => new PlayerAction { Kind = ActionKinds.UnitRecruit, UnitId = unitId, Count = count };
        public static PlayerAction Heal(string unitId, long count, string payWith) => new PlayerAction { Kind = ActionKinds.UnitHeal, UnitId = unitId, Count = count, PayWith = payWith };
        public static PlayerAction CreateUnit(string troopType) => new PlayerAction { Kind = ActionKinds.UnitCreate, TroopType = troopType };
        public static PlayerAction DisbandUnit(string unitId) => new PlayerAction { Kind = ActionKinds.UnitDisband, UnitId = unitId };

        private sealed class Handler : IPlayerActionHandler
        {
            private readonly Func<CampaignState, PlayerAction, bool> _apply;
            public Handler(string kind, Func<CampaignState, PlayerAction, bool> apply) { Kind = kind; _apply = apply; }
            public string Kind { get; }
            public bool Apply(CampaignState state, PlayerAction action) => _apply(state, action);
        }
    }

    /// <summary>서버 쪽 행동 검증 (참고 서버·가짜 서버 공통): 서버 세이브 사본에 차례로 적용하고, 모두 되면 새 세이브 JSON.</summary>
    public static class ActionVerifier
    {
        /// <summary>시각 검사(R-42: 세이브 시각 이후, 차례대로, 지금까지) → 적용(R-41). savedAt = 마지막 행동 시각 (새 세이브 시각).</summary>
        public static ActionVerdict Apply(PlayerActionRunner runner, CloudSave save, ActionSubmission submission, long nowUnixSeconds, out string json, out long savedAt)
        {
            json = null;
            savedAt = save?.SavedAtUnixSeconds ?? 0;
            var state = SaveJson.Read(save?.Json);
            if (state == null || submission?.Actions == null || submission.Actions.Count == 0 || submission.Actions.Count > PlayerActionRunner.MaxPerSubmission)
                return new ActionVerdict { Valid = false, Reason = VerdictReasons.BadAction, FailedIndex = 0 };
            long floor = savedAt;
            for (int i = 0; i < submission.Actions.Count; i++)
            {
                var a = submission.Actions[i];
                long at = RecordTime.Resolve(a?.DoneAtUnixSeconds ?? 0, nowUnixSeconds);
                if (!RecordTime.Allowed(floor, at, nowUnixSeconds)) return new ActionVerdict { Valid = false, Reason = VerdictReasons.BadTime, FailedIndex = i };
                if (!runner.Apply(state, a)) return new ActionVerdict { Valid = false, Reason = VerdictReasons.BadAction, FailedIndex = i };
                floor = System.Math.Max(floor, at);
            }
            json = SaveJson.Write(state);
            savedAt = System.Math.Min(floor, nowUnixSeconds);
            return new ActionVerdict { Valid = true, Reason = VerdictReasons.Ok };
        }
    }

    /// <summary>
    /// 기록 시각 규칙 (verification R-42): 기록(행동·전투)은 기기가 한 시각을 달고 온다. 서버는 그 시각으로 다시 계산한다.
    /// 허용: 서버 세이브 시각(앞 기록) 이후 · 지금 이전 · 너무 오래되지 않음. 시계 차이는 조금 봐준다.
    /// </summary>
    public static class RecordTime
    {
        /// <summary>오프라인 기록을 받아 주는 기간 (초). 이보다 오래된 기록은 거절 (구출 기한 등 시간 규칙을 미루지 못하게).</summary>
        public const long MaxOfflineSeconds = 72 * 3600;
        /// <summary>기기·서버 시계 차이 허용 (초).</summary>
        public const long SkewSeconds = 120;

        /// <summary>0이면 지금.</summary>
        public static long Resolve(long at, long now) => at <= 0 ? now : at;

        public static bool Allowed(long floor, long at, long now) =>
            at >= floor - SkewSeconds && at <= now + SkewSeconds && at >= now - MaxOfflineSeconds;
    }
}
