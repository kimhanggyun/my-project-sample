using System.Collections.Generic;
using Game.Battle.Model.Simulation;
using Game.Foundation.Math;

namespace Game.Battle.Model.Events
{
    /// <summary>전투 중 일어난 일 하나. 화면(말풍선·연출)·리플레이·숨은 보상 판정이 같은 기록을 쓴다.</summary>
    public sealed class BattleEvent
    {
        public long Tick { get; internal set; }
        /// <summary><see cref="EventKinds"/>.</summary>
        public string Kind { get; internal set; }
        public string CorpsId { get; internal set; }
        /// <summary>종류별 추가 값 (스킬 id, 대사 키, 팀 …).</summary>
        public string Data { get; internal set; }
    }

    public static class EventKinds
    {
        public const string SkillUsed = "skillUsed";
        public const string RetreatStarted = "retreatStarted";
        public const string Captured = "captured";
        public const string Destroyed = "destroyed";
        public const string AmbushSprung = "ambushSprung";
        public const string FireHit = "fireHit";
        public const string SupplyLow = "supplyLow";
        public const string MoraleCritical = "moraleCritical";
        public const string Reinforced = "reinforced";
        public const string Dialogue = "dialogue";
        /// <summary>숨은 적(은신·복병 준비 중)을 찾아냄. 군단 = 찾은 군단, 값 = 숨은 군단.</summary>
        public const string HiddenFound = "hiddenFound";
        /// <summary>복병 기습을 당함. 군단 = 당한 군단, 값 = 복병 군단.</summary>
        public const string Ambushed = "ambushed";
        /// <summary>협공 대기가 길어짐 (한 번 묻는다, battle.md 4.3.4). 응답이 없으면 곧 단독 공격.</summary>
        public const string CoordinateWaiting = "coordinateWaiting";
        /// <summary>예약을 꺼냈는데 받을 수 없어 건너뜀 (battle.md 4.3.6). 값 = 스킬 id 또는 명령 종류.</summary>
        public const string QueuedSkipped = "queuedSkipped";
        /// <summary>지점 이동인데 길이 막혀 더 갈 수 없음 → 멈추고 다음 예약으로 (battle.md 4.3.6).</summary>
        public const string PathBlocked = "pathBlocked";
        /// <summary>플레이어 군단이 일기토 신청을 받음, 답을 기다림 (R-79). 군단 = 받은 쪽, 값 = 건 쪽.</summary>
        public const string DuelOffered = "duelOffered";
        /// <summary>일기토 신청을 받아들임 (R-71). 군단 = 건 쪽, 값 = 받은 쪽.</summary>
        public const string DuelStarted = "duelStarted";
        /// <summary>일기토 신청을 거절 (그 군단 사기↓). 군단 = 거절한 쪽, 값 = 건 쪽.</summary>
        public const string DuelRefused = "duelRefused";
        /// <summary>전투 중 일기토가 끝남. 군단 = 건 쪽, 값 = "끝 종류:이긴 쪽 또는 물러난 쪽(A/B)".</summary>
        public const string DuelEnded = "duelEnded";
        /// <summary>함정을 밟음 (R-80). 군단 = 밟은 쪽, 값 = 놓은 군단.</summary>
        public const string TrapSprung = "trapSprung";
        /// <summary>함정이 간파에 걸려 사라짐. 군단 = 놓은 군단.</summary>
        public const string TrapFound = "trapFound";
        /// <summary>낙석에 맞음. 군단 = 맞은 쪽, 값 = 쓴 군단.</summary>
        public const string RockfallHit = "rockfallHit";
        /// <summary>위장 깃발이 들킴 (간파·가까이 감). 값 = 깃발을 세운 팀.</summary>
        public const string DecoyExposed = "decoyExposed";
        /// <summary>거짓 퇴각에 속아 추격하다 흐트러짐. 군단 = 속은 쪽, 값 = 거짓 퇴각한 군단.</summary>
        public const string FeignSprung = "feignSprung";
    }

    internal static class EventLog
    {
        public static void Add(BattleContext ctx, string kind, string corpsId, string data = null) =>
            ctx.Log.Add(new BattleEvent { Tick = ctx.Tick, Kind = kind, CorpsId = corpsId, Data = data });

        /// <summary>군단마다 한 번만 남기는 경고 (군량 부족, 사기 붕괴 직전).</summary>
        public static void Warnings(BattleContext ctx)
        {
            var k = ctx.Combat;
            foreach (var c in ctx.Corps)
            {
                if (c.Status != CorpsStatus.Active) continue;
                if (!c.Warned.Contains(EventKinds.SupplyLow) && c.StartSupply > 0
                    && Fix.FromInt(c.Supply) <= k.WarningSupplyLowRatio * c.StartSupply)
                {
                    c.Warned.Add(EventKinds.SupplyLow);
                    Add(ctx, EventKinds.SupplyLow, c.Id);
                }
                if (!c.Warned.Contains(EventKinds.MoraleCritical) && ctx.IsEnabled("morale")
                    && c.Morale <= c.Temperament.RetreatMorale + k.WarningMoraleCriticalMargin)
                {
                    c.Warned.Add(EventKinds.MoraleCritical);
                    Add(ctx, EventKinds.MoraleCritical, c.Id);
                }
            }
        }
    }

    /// <summary>
    /// 혼란 걸기 (공통): 부대 결속이 높으면 일정 확률로 버티고, 걸려도 시간이 짧다 (battle.md 2.19.1).
    /// 확률은 시드 난수에서만.
    /// </summary>
    public static class Confusion
    {
        public static bool Apply(BattleContext ctx, SubUnitState u, long ticks)
        {
            var k = ctx.Combat;
            if (u.Cohesion > Fix.Zero && ctx.Random.Chance(u.Cohesion * k.CohesionConfusionResistPerPoint)) return false;
            Fix cut = Fix.Min(k.CohesionMaxDurationCut, u.Cohesion * k.CohesionConfusionDurationCutPerPoint);
            long t = (Fix.FromInt(ticks) * (Fix.OneValue - cut)).CeilToLong();
            u.ConfusedTicks = System.Math.Max(u.ConfusedTicks, t);
            return true;
        }
    }

    // ---- 스텝 연출 (ai.md 8장, battle.md 6장 events): 조건이 되면 행동 ----

    /// <summary>이벤트 조건 하나. 새 조건은 구현 추가 + 등록.</summary>
    public interface IEventTrigger
    {
        string Type { get; }
        bool IsMet(BattleContext ctx, TriggerSetup t);
    }

    /// <summary>이벤트 행동 하나 (대사, 증원, AI 성향 바꾸기 …).</summary>
    public interface IEventAction
    {
        string Type { get; }
        void Apply(BattleContext ctx, ActionSetup a);
    }

    public sealed class EventRegistry
    {
        private readonly Dictionary<string, IEventTrigger> _triggers = new Dictionary<string, IEventTrigger>();
        private readonly Dictionary<string, IEventAction> _actions = new Dictionary<string, IEventAction>();

        public EventRegistry Register(IEventTrigger t) { _triggers[t.Type] = t; return this; }
        public EventRegistry Register(IEventAction a) { _actions[a.Type] = a; return this; }
        public bool HasTrigger(string type) => type != null && _triggers.ContainsKey(type);
        public bool HasAction(string type) => type != null && _actions.ContainsKey(type);
        internal IEventTrigger Trigger(string type) => _triggers[type];
        internal IEventAction Action(string type) => _actions[type];

        public static EventRegistry WithBuiltIns() => new EventRegistry()
            .Register(new StartTrigger())
            .Register(new AtTickTrigger())
            .Register(new CorpsLeftTrigger())
            .Register(new CorpsTroopsBelowTrigger())
            .Register(new TeamTroopsBelowTrigger())
            .Register(new DialogueAction())
            .Register(new ReinforceAction())
            .Register(new SetAiStanceAction());
    }

    internal static class EventSystem
    {
        public static void Step(BattleContext ctx, EventRegistry reg)
        {
            var events = ctx.Setup.Events;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (ctx.FiredEvents.Contains(i) && e.Once) continue;
                if (!reg.Trigger(e.Trigger.Type).IsMet(ctx, e.Trigger)) continue;
                ctx.FiredEvents.Add(i);
                foreach (var a in e.Actions) reg.Action(a.Type).Apply(ctx, a);
            }
        }
    }

    internal sealed class StartTrigger : IEventTrigger
    {
        public string Type => "start";
        public bool IsMet(BattleContext ctx, TriggerSetup t) => ctx.Tick == 0;
    }

    internal sealed class AtTickTrigger : IEventTrigger
    {
        public string Type => "atTick";
        public bool IsMet(BattleContext ctx, TriggerSetup t) => ctx.Tick >= t.Tick;
    }

    /// <summary>그 군단이 퇴각을 시작했거나 전장에서 빠짐.</summary>
    internal sealed class CorpsLeftTrigger : IEventTrigger
    {
        public string Type => "corpsLeft";
        public bool IsMet(BattleContext ctx, TriggerSetup t)
        {
            var c = ctx.FindCorps(t.CorpsId);
            return c != null && c.Status != CorpsStatus.Active && c.Status != CorpsStatus.Reserve;
        }
    }

    /// <summary>그 군단 병력이 시작의 비율 이하 (예: 적 대장 위기 → 증원).</summary>
    internal sealed class CorpsTroopsBelowTrigger : IEventTrigger
    {
        public string Type => "corpsTroopsBelow";
        public bool IsMet(BattleContext ctx, TriggerSetup t)
        {
            var c = ctx.FindCorps(t.CorpsId);
            return c != null && c.Status != CorpsStatus.Reserve && Fix.FromInt(c.Troops) <= t.Ratio * c.StartTroops;
        }
    }

    /// <summary>팀 전체 남은 병력(전장에 있는 군단)이 시작의 비율 이하 (예: 50% 이하면 지구전으로).</summary>
    internal sealed class TeamTroopsBelowTrigger : IEventTrigger
    {
        public string Type => "teamTroopsBelow";
        public bool IsMet(BattleContext ctx, TriggerSetup t)
        {
            long start = 0, now = 0;
            foreach (var c in ctx.Corps)
            {
                if (c.Team != t.Team || c.Status == CorpsStatus.Reserve) continue;
                start += c.StartTroops;
                if (c.InBattle) now += c.Troops;
            }
            return start > 0 && Fix.FromInt(now) <= t.Ratio * start;
        }
    }

    /// <summary>대사 (화면이 이벤트 기록에서 읽어 보여준다).</summary>
    internal sealed class DialogueAction : IEventAction
    {
        public string Type => "dialogue";
        public void Apply(BattleContext ctx, ActionSetup a) => EventLog.Add(ctx, EventKinds.Dialogue, a.CorpsId, a.Key);
    }

    /// <summary>증원: 예비 군단을 전장에 들인다.</summary>
    internal sealed class ReinforceAction : IEventAction
    {
        public string Type => "reinforce";
        public void Apply(BattleContext ctx, ActionSetup a)
        {
            var c = ctx.FindCorps(a.CorpsId);
            if (c == null || c.Status != CorpsStatus.Reserve) return;
            c.Status = CorpsStatus.Active;
            EventLog.Add(ctx, EventKinds.Reinforced, c.Id);
        }
    }

    /// <summary>AI 성향 바꾸기 (작전 페이즈, 예: 수비 → 공격).</summary>
    internal sealed class SetAiStanceAction : IEventAction
    {
        public string Type => "setAiStance";
        public void Apply(BattleContext ctx, ActionSetup a)
        {
            var c = ctx.FindCorps(a.CorpsId);
            if (c != null) c.AiStance = a.Stance;
        }
    }
}
