using System.Collections.Generic;
using Game.Battle.Model.Simulation;

namespace Game.Battle.Model.Victory
{
    public static class VictoryConditionIds
    {
        /// <summary>팀의 모든 군단이 전장에서 빠짐 (퇴각·궤멸).</summary>
        public const string Annihilation = "annihilation";
        /// <summary>대장(commander) 군단이 전장에서 빠짐.</summary>
        public const string Commander = "commander";
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
            .Register(new CommanderCondition());
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
                if (c.Team == team && c.Setup.Role == CorpsRoles.Commander && !c.InBattle) return true;
            return false;
        }
    }
}
