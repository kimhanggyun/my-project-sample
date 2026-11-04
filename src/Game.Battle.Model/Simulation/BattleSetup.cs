using System.Collections.Generic;
using Game.Data.Defs;
using Game.Data.Rules;
using Game.Foundation.Math;

namespace Game.Battle.Model.Simulation
{
    /// <summary>
    /// 전투 입력 (경계 객체). 전투는 이것만 받고 <see cref="BattleResult"/>만 낸다.
    /// 경제·포로·고용 모듈을 모른다. 같은 BattleSetup + 같은 명령 기록이면 같은 결과 (R-29).
    /// </summary>
    public sealed class BattleSetup
    {
        public DefinitionSet Defs { get; set; }
        public MapDef Map { get; set; }
        /// <summary>스텝·규칙 세트 덮어쓰기를 적용한 값.</summary>
        public BalanceData Balance { get; set; }
        public CombatRules Combat { get; set; }
        public RuleFlags Flags { get; set; }
        public ulong Seed { get; set; }
        /// <summary>시간 제한 (분). 다 되면 남은 병력 비율로 판정.</summary>
        public Fix MaxMinutes { get; set; } = Fix.FromInt(10);
        /// <summary>패배 조건 id 목록 (<see cref="Victory.VictoryConditionIds"/>). 하나라도 만족하면 그 팀 패배.</summary>
        public List<string> DefeatConditions { get; set; } = new List<string> { Victory.VictoryConditionIds.Annihilation, Victory.VictoryConditionIds.Commander };
        public List<CorpsSetup> Corps { get; set; } = new List<CorpsSetup>();
    }

    /// <summary>전투에 들어가는 장수 능력치. 부상·고용 등은 바깥(캠페인)에서 반영한 뒤 넘긴다.</summary>
    public sealed class GeneralSnapshot
    {
        public string Id { get; set; }
        public Fix Leadership { get; set; }
        public Fix Might { get; set; }
        public Fix Intellect { get; set; }
        public string Temperament { get; set; }

        public static GeneralSnapshot From(GeneralDef def) => new GeneralSnapshot
        {
            Id = def.Id,
            Leadership = def.Stats.Get("leadership"),
            Might = def.Stats.Get("might"),
            Intellect = def.Stats.Get("intellect"),
            Temperament = def.Temperament,
        };
    }

    public static class Controllers
    {
        public const string Player = "player";
        public const string Ai = "ai";
    }

    public static class CorpsRoles
    {
        public const string Commander = "commander";
    }

    public sealed class UnitSetup
    {
        /// <summary>캠페인 쪽 부대 id (결과를 돌려줄 때 쓴다). 없어도 된다.</summary>
        public string UnitId { get; set; }
        public string Slot { get; set; }
        public string TroopType { get; set; }
        public long Troops { get; set; }
    }

    public sealed class CorpsSetup
    {
        public string Id { get; set; }
        public string Team { get; set; }
        /// <summary>"commander"(대장) 등. 대장 군단이 빠지면 패배 조건 commander.</summary>
        public string Role { get; set; }
        /// <summary>"player" 또는 "ai".</summary>
        public string Controller { get; set; } = Controllers.Ai;
        /// <summary>AI 성향 id (<see cref="Ai.AiStanceIds"/>).</summary>
        public string AiStance { get; set; } = Ai.AiStanceIds.Aggressive;
        public GeneralSnapshot General { get; set; }
        public string Formation { get; set; }
        /// <summary>가져간 군량.</summary>
        public long Supply { get; set; }
        public List<UnitSetup> Units { get; set; } = new List<UnitSetup>();
        /// <summary>시작 위치. 없으면 맵의 팀 시작 위치에서 팀 안 순서대로 옆으로 벌려 놓는다.</summary>
        public Fix? X { get; set; }
        public Fix? Y { get; set; }
        public string Facing { get; set; }
    }
}
