using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Foundation.Math;

namespace Game.Battle.Model.Commands
{
    /// <summary>
    /// 플레이어 명령 (입력 객체). 틱 번호와 함께 기록되어 리플레이·서버 검증에 그대로 쓴다.
    /// </summary>
    public sealed class BattleCommand
    {
        public long Tick { get; set; }
        public string CorpsId { get; set; }
        /// <summary><see cref="CommandKinds"/> 또는 등록한 확장 명령 id.</summary>
        public string Kind { get; set; }
        public Fix X { get; set; }
        public Fix Y { get; set; }
        public string TargetCorpsId { get; set; }
        public string FormationId { get; set; }
        public string Mode { get; set; }
        /// <summary>useSkill의 스킬 id.</summary>
        public string SkillId { get; set; }

        public BattleCommand Clone() => (BattleCommand)MemberwiseClone();
    }

    public static class CommandKinds
    {
        /// <summary>진군: 가장 가까운 적을 찾아 공격 이동. 대상이 빠지면 다음 적으로 계속.</summary>
        public const string Advance = "advance";
        /// <summary>지점 이동 (Mode: normal / forced / attackMove).</summary>
        public const string MoveTo = "moveTo";
        /// <summary>군단 지정 공격.</summary>
        public const string Attack = "attack";
        /// <summary>대기 (사거리 안의 적과는 싸운다, R-20).</summary>
        public const string Hold = "hold";
        /// <summary>퇴각.</summary>
        public const string Retreat = "retreat";
        /// <summary>진형 변경.</summary>
        public const string ChangeFormation = "changeFormation";
        /// <summary>스킬 사용 (SkillId + 대상 군단 또는 지점). 사거리 밖이면 다가간 뒤 쓴다.</summary>
        public const string UseSkill = "useSkill";
        /// <summary>위임 켜기/끄기 (Mode "on"/"off"). 스위치 delegate.</summary>
        public const string Delegate = "delegate";
        public const string DelegateFlag = "delegate";
    }

    /// <summary>명령 하나의 처리기. 새 명령은 처리기를 추가하고 등록한다 (기존 분기문을 고치지 않는다).</summary>
    public interface ICommandHandler
    {
        string Kind { get; }
        bool IsValid(BattleContext ctx, CorpsState corps, BattleCommand cmd);
        void Apply(BattleContext ctx, CorpsState corps, BattleCommand cmd);
    }

    public sealed class CommandRegistry
    {
        private readonly Dictionary<string, ICommandHandler> _handlers = new Dictionary<string, ICommandHandler>();

        public CommandRegistry Register(ICommandHandler handler)
        {
            _handlers[handler.Kind] = handler;
            return this;
        }

        public bool TryGet(string kind, out ICommandHandler handler) => _handlers.TryGetValue(kind ?? "", out handler);

        public static CommandRegistry WithBuiltIns() => new CommandRegistry()
            .Register(new AdvanceHandler())
            .Register(new MoveToHandler())
            .Register(new AttackHandler())
            .Register(new HoldHandler())
            .Register(new RetreatHandler())
            .Register(new ChangeFormationHandler())
            .Register(new Skills.UseSkillHandler())
            .Register(new DelegateHandler());
    }

    internal sealed class AdvanceHandler : ICommandHandler
    {
        public string Kind => CommandKinds.Advance;
        public bool IsValid(BattleContext ctx, CorpsState corps, BattleCommand cmd) => true;
        public void Apply(BattleContext ctx, CorpsState corps, BattleCommand cmd) =>
            corps.AssignOrder(new CorpsOrder { Kind = OrderKinds.Advance, Mode = MoveModes.AttackMove });
    }

    internal sealed class MoveToHandler : ICommandHandler
    {
        public string Kind => CommandKinds.MoveTo;
        public bool IsValid(BattleContext ctx, CorpsState corps, BattleCommand cmd)
        {
            string mode = cmd.Mode ?? MoveModes.Normal;
            if (mode != MoveModes.Normal && mode != MoveModes.Forced && mode != MoveModes.AttackMove) return false;
            return ctx.Field.Map.InBounds((int)cmd.X.FloorToLong(), (int)cmd.Y.FloorToLong());
        }
        public void Apply(BattleContext ctx, CorpsState corps, BattleCommand cmd)
        {
            corps.AssignOrder(new CorpsOrder { Kind = OrderKinds.MoveTo, Point = new Vec(cmd.X, cmd.Y), Mode = cmd.Mode ?? MoveModes.Normal });
        }
    }

    internal sealed class AttackHandler : ICommandHandler
    {
        public string Kind => CommandKinds.Attack;
        public bool IsValid(BattleContext ctx, CorpsState corps, BattleCommand cmd)
        {
            var t = ctx.FindCorps(cmd.TargetCorpsId);
            return t != null && t.Team != corps.Team && t.InBattle && ctx.IsVisibleTo(corps.Team, t);
        }
        public void Apply(BattleContext ctx, CorpsState corps, BattleCommand cmd)
        {
            corps.AssignOrder(new CorpsOrder { Kind = OrderKinds.Attack, TargetCorpsId = cmd.TargetCorpsId, Mode = MoveModes.Normal });
        }
    }

    internal sealed class HoldHandler : ICommandHandler
    {
        public string Kind => CommandKinds.Hold;
        public bool IsValid(BattleContext ctx, CorpsState corps, BattleCommand cmd) => true;
        public void Apply(BattleContext ctx, CorpsState corps, BattleCommand cmd)
        {
            corps.AssignOrder(new CorpsOrder());
        }
    }

    internal sealed class RetreatHandler : ICommandHandler
    {
        public string Kind => CommandKinds.Retreat;
        public bool IsValid(BattleContext ctx, CorpsState corps, BattleCommand cmd) => true;
        public void Apply(BattleContext ctx, CorpsState corps, BattleCommand cmd) => Systems.RetreatSystem.BeginRetreat(ctx, corps);
    }

    /// <summary>위임 켜고 끄기. 켜면 AI가 바로 지휘하고(진행 중인 플레이어 명령은 그대로 둔다), 끄면 지금 명령을 유지한 채 플레이어에게 돌아온다.</summary>
    internal sealed class DelegateHandler : ICommandHandler
    {
        public string Kind => CommandKinds.Delegate;
        public bool IsValid(BattleContext ctx, CorpsState corps, BattleCommand cmd) =>
            ctx.IsEnabled(CommandKinds.DelegateFlag) && (cmd.Mode == "on" || cmd.Mode == "off") && (cmd.Mode == "on") != corps.Delegated;
        public void Apply(BattleContext ctx, CorpsState corps, BattleCommand cmd) => corps.Delegated = cmd.Mode == "on";
    }

    internal sealed class ChangeFormationHandler : ICommandHandler
    {
        public string Kind => CommandKinds.ChangeFormation;
        public bool IsValid(BattleContext ctx, CorpsState corps, BattleCommand cmd) =>
            cmd.FormationId != null && ctx.Defs.Formations.ContainsKey(cmd.FormationId) && cmd.FormationId != corps.Formation.Id;
        public void Apply(BattleContext ctx, CorpsState corps, BattleCommand cmd) =>
            corps.Formation = ctx.Defs.Formations[cmd.FormationId];
    }
}
