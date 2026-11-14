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
                ed.Draft.AddRange(FromActionCorps(a.Corps));
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
            var growth = new TroopGrowthService(defs);
            Register(new Handler(ActionKinds.UnitTrain, (s, a) => growth.Train(s, a.UnitId)));
            Register(new Handler(ActionKinds.UnitPromote, (s, a) => growth.Promote(s, a.UnitId, a.TroopType, a.Count)));
            var gacha = new GachaService(defs);
            Register(new Handler(ActionKinds.GachaDraw, (s, a) => gacha.Draw(s, a.BannerId, (int)a.Count, a.Seed).Ok));
            var practice = new PracticeService(defs);
            Register(new Handler(ActionKinds.ContestPractice, (s, a) => practice.Apply(s, a) != null));
            // 포로 (R-66): 시각은 행동 시각(서버 시각 기준), 무작위는 행동의 시드 — 서버도 같은 결과
            Game.Campaign.Captivity.CaptivityService Captivity(PlayerAction a) => new Game.Campaign.Captivity.CaptivityService(defs, defs.ResolveFlags(),
                new Game.Foundation.Time.ManualTimeProvider(DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, a.DoneAtUnixSeconds))));
            Register(new Handler(ActionKinds.PrisonerRecruit, (s, a) =>
                a.GeneralId != null && Captivity(a).TryRecruit(s, a.GeneralId, new Game.Foundation.Random.SeededRandom(a.Seed)) != Game.Campaign.Captivity.RecruitOutcome.NotAllowed));
            Register(new Handler(ActionKinds.SetDifficulty, (s, a) =>
            {
                if (a.Situation == null || !defs.Ai.SettingShift.ContainsKey(a.Situation) || s.Difficulty == a.Situation) return false;
                s.Difficulty = a.Situation;
                return true;
            }));
            Register(new Handler(ActionKinds.PrisonerRelease, (s, a) =>
            {
                if (a.GeneralId == null || s.FindPrisoner(a.GeneralId) == null) return false;
                Captivity(a).Release(s, a.GeneralId);
                return true;
            }));
            var shop = new PurchaseService(defs);
            Register(new Handler(ActionKinds.StorePurchase, (s, a) => shop.Apply(s, a)));
            var guides = new GuideService(defs);
            Register(new Handler(ActionKinds.GuideSeen, (s, a) => guides.MarkSeen(s, a.GuideId)));
            Register(new Handler(ActionKinds.GuideReset, (s, a) => guides.Reset(s)));
            var limitBreak = new LimitBreakService(defs);
            Register(new Handler(ActionKinds.GeneralLimitBreak, (s, a) => limitBreak.Apply(s, a.GeneralId)));
            // 우편 받기: 서버는 우편이 있는지·양이 맞는지 먼저 본다 (ActionVerifier의 serverCheck)
            Register(new Handler(ActionKinds.MailClaim, (s, a) =>
            {
                if (a.MailId == null || a.Count <= 0 || (a.PayWith != Economy.ResourceIds.Gold && a.PayWith != Economy.ResourceIds.Supply)) return false;
                new Economy.Wallet(s.Wallet).Add(a.PayWith, a.Count);
                return true;
            }));
            // 개발 행동 (R-86): 서버는 마스터 계정의 것만 받는다 (StrategyGameModule). 기기는 개발 빌드·마스터일 때만 화면에 둔다.
            Register(new Handler(ActionKinds.DevGrant, (s, a) =>
            {
                if (a.Count <= 0 || a.Count > DevGrantMax) return false;
                if (a.PayWith != Economy.ResourceIds.Supply && a.PayWith != Economy.ResourceIds.Gold && a.PayWith != Economy.ResourceIds.Gem) return false;
                new Economy.Wallet(s.Wallet).Add(a.PayWith, a.Count);
                return true;
            }));
            Register(new Handler(ActionKinds.DevLordLevel, (s, a) =>
            {
                int max = System.Math.Max(1, defs.Campaign.LordMaxLevel);
                long to = System.Math.Max(1, System.Math.Min(max, s.LordLevel + a.Count));
                if (a.Count == 0 || to == s.LordLevel) return false;
                s.LordLevel = (int)to;
                return true;
            }));
            Register(new Handler(ActionKinds.DevUnlockAll, (s, a) =>
            {
                bool any = false;
                foreach (var id in defs.StepIndex.Order)
                    if (!s.ClearedSteps.Contains(id)) { s.ClearedSteps.Add(id); any = true; }
                return any;
            }));
            Register(new Handler(ActionKinds.DevGiveGeneral, (s, a) =>
            {
                if (a.GeneralId == null) return false;
                var ids = new List<string>();
                if (a.GeneralId == "*") ids.AddRange(defs.Generals.Keys);
                else if (defs.Generals.ContainsKey(a.GeneralId)) ids.Add(a.GeneralId);
                ids.Sort(StringComparer.Ordinal);
                bool any = false;
                foreach (var id in ids)
                    if (s.FindGeneral(id) == null && s.FindPrisoner(id) == null) { s.Generals.Add(GeneralState.Join(defs.Generals[id])); any = true; }
                return any;
            }));
        }

        /// <summary>dev.grant 한 번 상한.</summary>
        public const long DevGrantMax = 1_000_000_000;

        public static PlayerAction DevGrant(string resource, long amount) => new PlayerAction { Kind = ActionKinds.DevGrant, PayWith = resource, Count = amount };
        public static PlayerAction DevUnlockAll() => new PlayerAction { Kind = ActionKinds.DevUnlockAll };
        public static PlayerAction DevLordLevel(long delta) => new PlayerAction { Kind = ActionKinds.DevLordLevel, Count = delta };
        public static PlayerAction DevGiveGeneral(string generalId) => new PlayerAction { Kind = ActionKinds.DevGiveGeneral, GeneralId = generalId };

        public static List<ActionCorps> ToActionCorps(IEnumerable<CorpsComposition> comps)
        {
            var list = new List<ActionCorps>();
            foreach (var c in comps)
            {
                var ac = new ActionCorps { GeneralId = c.GeneralId, Formation = c.Formation, Role = c.Role, BoardRow = c.BoardRow, BoardCol = c.BoardCol, Commander = c.Commander };
                foreach (var p in c.Picks) ac.Picks.Add(new ActionPick { UnitId = p.UnitId, Troops = p.Troops, Hired = p.Hired });
                list.Add(ac);
            }
            return list;
        }

        public static List<CorpsComposition> FromActionCorps(IEnumerable<ActionCorps> corps)
        {
            var list = new List<CorpsComposition>();
            foreach (var c in corps)
            {
                var cc = new CorpsComposition { GeneralId = c?.GeneralId, Formation = c?.Formation, Role = c?.Role, BoardRow = c?.BoardRow, BoardCol = c?.BoardCol, Commander = c?.Commander ?? false };
                if (c?.Picks != null) foreach (var p in c.Picks) if (p != null) cc.Picks.Add(new TroopPick { UnitId = p.UnitId, Troops = p.Troops, Hired = p.Hired });
                list.Add(cc);
            }
            return list;
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

        public static PlayerAction SaveFormation(string situation, IEnumerable<CorpsComposition> draft) =>
            new PlayerAction { Kind = ActionKinds.FormationSave, Situation = situation, Corps = ToActionCorps(draft) };

        public static PlayerAction TrainUnit(string unitId) => new PlayerAction { Kind = ActionKinds.UnitTrain, UnitId = unitId };
        public static PlayerAction LimitBreak(string generalId) => new PlayerAction { Kind = ActionKinds.GeneralLimitBreak, GeneralId = generalId };
        /// <param name="count">승급할 인원 (0 = 지금 병력 전부, 예전 행동과 같음).</param>
        public static PlayerAction PromoteUnit(string unitId, string to, long count = 0) => new PlayerAction { Kind = ActionKinds.UnitPromote, UnitId = unitId, TroopType = to, Count = count };

        public static PlayerAction DrawGacha(string bannerId, int count, ulong seed) =>
            new PlayerAction { Kind = ActionKinds.GachaDraw, BannerId = bannerId, Count = count, Seed = seed };

        public static PlayerAction ClaimMail(Mail mail) =>
            new PlayerAction { Kind = ActionKinds.MailClaim, MailId = mail.Id, PayWith = mail.Resource, Count = mail.Amount };

        public static PlayerAction RemovePreset(string situation) => new PlayerAction { Kind = ActionKinds.FormationRemovePreset, Situation = situation };
        public static PlayerAction BuySkillBook(string skillId) => new PlayerAction { Kind = ActionKinds.SkillBookBuy, SkillId = skillId };
        public static PlayerAction LearnSkill(string generalId, string skillId) => new PlayerAction { Kind = ActionKinds.SkillLearn, GeneralId = generalId, SkillId = skillId };
        public static PlayerAction Conscript(string unitId, long count) => new PlayerAction { Kind = ActionKinds.UnitConscript, UnitId = unitId, Count = count };
        public static PlayerAction Recruit(string unitId, long count) => new PlayerAction { Kind = ActionKinds.UnitRecruit, UnitId = unitId, Count = count };
        public static PlayerAction Heal(string unitId, long count, string payWith) => new PlayerAction { Kind = ActionKinds.UnitHeal, UnitId = unitId, Count = count, PayWith = payWith };
        public static PlayerAction CreateUnit(string troopType) => new PlayerAction { Kind = ActionKinds.UnitCreate, TroopType = troopType };
        public static PlayerAction RecruitPrisoner(string generalId, ulong seed) => new PlayerAction { Kind = ActionKinds.PrisonerRecruit, GeneralId = generalId, Seed = seed };
        public static PlayerAction ReleasePrisoner(string generalId) => new PlayerAction { Kind = ActionKinds.PrisonerRelease, GeneralId = generalId };
        public static PlayerAction SetDifficulty(string id) => new PlayerAction { Kind = ActionKinds.SetDifficulty, Situation = id };
        public static PlayerAction Purchase(string productId, string receipt) => new PlayerAction { Kind = ActionKinds.StorePurchase, ProductId = productId, Receipt = receipt };
        public static PlayerAction SeeGuide(string guideId) => new PlayerAction { Kind = ActionKinds.GuideSeen, GuideId = guideId };
        public static PlayerAction ResetGuides() => new PlayerAction { Kind = ActionKinds.GuideReset };
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
        /// <param name="serverCheck">서버만 아는 조건 (예: 우편이 있는가). false면 그 순번에서 badAction. null이면 없음.</param>
        public static ActionVerdict Apply(PlayerActionRunner runner, CloudSave save, ActionSubmission submission, long nowUnixSeconds, out string json, out long savedAt,
            Func<PlayerAction, bool> serverCheck = null)
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
                if ((serverCheck != null && !serverCheck(a)) || !runner.Apply(state, a)) return new ActionVerdict { Valid = false, Reason = VerdictReasons.BadAction, FailedIndex = i };
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
