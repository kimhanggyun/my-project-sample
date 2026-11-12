using System.Collections.Generic;
using Game.Battle.Model.Simulation;

namespace Game.Battle.Model.Victory
{
    public static class VictoryConditionIds
    {
        /// <summary>팀의 모든 군단이 전장에서 빠짐 (퇴각·궤멸·포로). 오지 않은 예비 군단은 세지 않는다 (증원은 그 전에 와야 한다).</summary>
        public const string Annihilation = "annihilation";
        /// <summary>대장(commander) 군단이 전장에서 빠짐 (아직 오지 않은 예비 군단은 빠진 것이 아님).</summary>
        public const string Commander = "commander";
        /// <summary>전장의 모든 군단이 군량 0 (R-78).</summary>
        public const string SupplyOut = "supplyOut";
        /// <summary>버티기: setup.HoldOut 팀이 시간까지 남으면 다른 팀 패배 (R-78).</summary>
        public const string HoldOut = "holdOut";
        /// <summary>구역 점령: 점령 구역을 목표 팀이 정한 시간 동안 혼자 차지하면 다른 팀 패배 (R-78).</summary>
        public const string Zone = "zone";
    }

    /// <summary>패배 조건 하나. 새 조건(거점 점령 등)은 구현을 추가하고 등록한다.</summary>
    public interface IDefeatCondition
    {
        string Id { get; }
        bool IsDefeated(BattleContext ctx, string team);
    }

    public sealed class VictoryRegistry
    {
        private readonly Dictionary<string, IDefeatCondition> _conditions = new Dictionary<string, IDefeatCondition>();

        public VictoryRegistry Register(IDefeatCondition c)
        {
            _conditions[c.Id] = c;
            return this;
        }

        public bool Contains(string id) => id != null && _conditions.ContainsKey(id);
        public IDefeatCondition Get(string id) => _conditions[id];

        public static VictoryRegistry WithBuiltIns() => new VictoryRegistry()
            .Register(new AnnihilationCondition())
            .Register(new CommanderCondition())
            .Register(new SupplyOutCondition())
            .Register(new HoldOutCondition())
            .Register(new ZoneCondition());
    }

    internal sealed class AnnihilationCondition : IDefeatCondition
    {
        public string Id => VictoryConditionIds.Annihilation;
        public bool IsDefeated(BattleContext ctx, string team)
        {
            foreach (var c in ctx.Corps) if (c.Team == team && c.InBattle) return false;
            return true;
        }
    }

    internal sealed class CommanderCondition : IDefeatCondition
    {
        public string Id => VictoryConditionIds.Commander;
        public bool IsDefeated(BattleContext ctx, string team)
        {
            foreach (var c in ctx.Corps)
                if (c.Team == team && c.Setup.Role == CorpsRoles.Commander && !c.InBattle && c.Status != CorpsStatus.Reserve) return true;
            return false;
        }
    }
}
