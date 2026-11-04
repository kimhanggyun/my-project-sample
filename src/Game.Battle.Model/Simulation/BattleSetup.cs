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
        /// <summary>
        /// 팀 → 이미 탐색한 칸 (같은 스텝 재도전 시 탐색 기억, 스위치 fogMemory). 칸 순서는 y × 폭 + x. 없으면 모두 미탐색.
        /// </summary>
        public Dictionary<string, bool[]> InitialExplored { get; set; } = new Dictionary<string, bool[]>();
        /// <summary>전투 시작 직전 대결 (일기토·논쟁). 발생 판정은 바깥에서 <see cref="Contests.ContestTrigger"/>로 한다.</summary>
        public List<ContestSetup> Contests { get; set; } = new List<ContestSetup>();
        /// <summary>바람 (스텝 데이터). 세기 0이면 바람 없음.</summary>
        public WindSetup Wind { get; set; } = new WindSetup();
        /// <summary>날씨 id (conditions.json weather). 기본 맑음.</summary>
        public string Weather { get; set; } = "clear";
        /// <summary>시간대 id (conditions.json timeOfDay). 기본 낮.</summary>
        public string TimeOfDay { get; set; } = "day";
        /// <summary>전투 중 바람·날씨가 바뀌는 이벤트 (틱 순서).</summary>
        public List<EnvironmentChange> Changes { get; set; } = new List<EnvironmentChange>();
        /// <summary>스텝 연출: 조건 → 행동 (대사, 증원, AI 성향 바꾸기).</summary>
        public List<EventSetup> Events { get; set; } = new List<EventSetup>();
    }

    public sealed class EventSetup
    {
        public string Id { get; set; }
        public TriggerSetup Trigger { get; set; }
        public List<ActionSetup> Actions { get; set; } = new List<ActionSetup>();
        /// <summary>한 번만 (기본). false면 조건이 맞는 틱마다.</summary>
        public bool Once { get; set; } = true;
    }

    /// <summary>조건: start, atTick(Tick), corpsLeft(CorpsId), corpsTroopsBelow(CorpsId, Ratio), teamTroopsBelow(Team, Ratio).</summary>
    public sealed class TriggerSetup
    {
        public string Type { get; set; }
        public long Tick { get; set; }
        public string CorpsId { get; set; }
        public string Team { get; set; }
        public Fix Ratio { get; set; }
    }

    /// <summary>행동: dialogue(Key, CorpsId), reinforce(CorpsId), setAiStance(CorpsId, Stance).</summary>
    public sealed class ActionSetup
    {
        public string Type { get; set; }
        public string Key { get; set; }
        public string CorpsId { get; set; }
        public string Stance { get; set; }
    }

    public sealed class WindSetup
    {
        /// <summary>바람이 불어 가는 방향 ("N", "SE" …). 없으면 바람 없음.</summary>
        public string Direction { get; set; }
        public Fix Strength { get; set; }
    }

    /// <summary>정해진 틱에 바람(방향·세기) 또는 날씨가 바뀐다. 비어 있는 값은 그대로.</summary>
    public sealed class EnvironmentChange
    {
        public long Tick { get; set; }
        public WindSetup Wind { get; set; }
        public string Weather { get; set; }
    }

    /// <summary>대결 하나. 선택 기록이 없거나 모자라면 AI가 고른다 (시간 안에 못 고르면 대신 고름).</summary>
    public sealed class ContestSetup
    {
        public string ContestId { get; set; }
        public string CorpsA { get; set; }
        public string CorpsB { get; set; }
        public List<string> ChoicesA { get; set; }
        public List<string> ChoicesB { get; set; }
    }

    /// <summary>전투에 들어가는 장수 능력치. 부상·고용 등은 바깥(캠페인)에서 반영한 뒤 넘긴다.</summary>
    public sealed class GeneralSnapshot
    {
        public string Id { get; set; }
        public Fix Leadership { get; set; }
        public Fix Might { get; set; }
        public Fix Intellect { get; set; }
        public string Temperament { get; set; }
        /// <summary>가진 스킬 전부 (기본 스킬 + 배운 스킬). 보유 스킬은 전부 가지고 출진한다.</summary>
        public List<string> Skills { get; set; } = new List<string>();

        /// <summary>통솔·무력·지략 밖의 능력치 (능력치는 확장형, battle.md 2.9).</summary>
        public Dictionary<string, Fix> OtherStats { get; set; } = new Dictionary<string, Fix>();

        /// <summary>능력치 id로 값 (스킬 요구 조건용). 모르는 id는 0.</summary>
        public Fix Stat(string id)
        {
            if (id == "leadership") return Leadership;
            if (id == "might") return Might;
            if (id == "intellect") return Intellect;
            return OtherStats.TryGetValue(id, out var v) ? v : Fix.Zero;
        }

        public static GeneralSnapshot From(GeneralDef def) => new GeneralSnapshot
        {
            Skills = new List<string>(def.InnateSkills),
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
        /// <summary>부대 결속 (0~100).</summary>
        public Fix Cohesion { get; set; }
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
        /// <summary>AI 난이도 id (ai.json). 판단의 질만 바꾼다.</summary>
        public string AiDifficulty { get; set; } = "normal";
        /// <summary>쓸 수 있는 진형 (AI 상성 대응용). 비면 지금 진형만.</summary>
        public List<string> KnownFormations { get; set; } = new List<string>();
        /// <summary>예비 군단: 증원 이벤트(reinforce)가 올 때까지 전장 밖.</summary>
        public bool Reserve { get; set; }
        /// <summary>플레이어 군단을 처음부터 위임 (전투 중 delegate 명령으로 켜고 끈다).</summary>
        public bool Delegated { get; set; }
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
