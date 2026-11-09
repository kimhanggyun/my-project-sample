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
        /// <returns>(성공, 실패한 순번 또는 -1, 성공이면 새 세이브 JSON).</returns>
        public static (bool ok, int failedIndex, string json) Apply(PlayerActionRunner runner, CloudSave save, ActionSubmission submission)
        {
            var state = SaveJson.Read(save?.Json);
            if (state == null || submission?.Actions == null) return (false, 0, null);
            int failed = runner.ApplyAll(state, submission.Actions);
            return failed >= 0 ? (false, failed, null) : (true, -1, SaveJson.Write(state));
        }
    }
}
