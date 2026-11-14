using System.Collections.Generic;
using Game.Foundation.Math;

namespace Game.Campaign.Roster
{
    /// <summary>보유 장수 1명의 상태 (세이브됨). 정의(GeneralDef)는 데이터에, 바뀌는 값만 여기.</summary>
    public sealed class GeneralState
    {
        /// <summary>새로 합류한 장수 (레벨 1, 기본 스킬·특성). 모집·등용·스토리 합류가 같이 쓴다.</summary>
        public static GeneralState Join(Game.Data.Defs.GeneralDef def)
        {
            var g = new GeneralState { GeneralId = def.Id, Level = 1 };
            if (def.InnateSkills != null) g.LearnedSkills.AddRange(def.InnateSkills);
            if (def.Traits != null) g.LearnedTraits.AddRange(def.Traits);
            return g;
        }

        public string GeneralId { get; set; }
        public int Level { get; set; } = 1;
        public long Exp { get; set; }
        /// <summary>한계 돌파 단계 (R-57, 0부터). 단계마다 레벨 상한이 오른다. 예전 세이브에는 없으므로 0.</summary>
        public int LimitBreak { get; set; }
        /// <summary>정의 능력치에 더해진 성장분 (능력치 id → 값).</summary>
        public Dictionary<string, Fix> StatGrowth { get; set; } = new Dictionary<string, Fix>();
        public List<string> LearnedSkills { get; set; } = new List<string>();
        public List<string> LearnedTraits { get; set; } = new List<string>();
        /// <summary>"available" / "lightInjury" / "seriousInjury" / "captured"</summary>
        public string Status { get; set; } = GeneralStatus.Available;
        /// <summary>부상 회복 시각 (경상·중상).</summary>
        public long StatusUntilUnixSeconds { get; set; }
        /// <summary>포로일 때: 갇힌 수용소 id, 잡힌 시각, 구출 기한 (captivity.md 2장).</summary>
        public string CampId { get; set; }
        public long CapturedAtUnixSeconds { get; set; }
        public long RescueDeadlineUnixSeconds { get; set; }
        /// <summary>기록 카운터 (일기토 승수 등, general-growth.md 2.3).</summary>
        public Dictionary<string, long> Records { get; set; } = new Dictionary<string, long>();
        /// <summary>성장 보상을 받은 횟수 (규칙 키 → 횟수). "몇마다"·"한 번만" 보상이 겹치지 않게.</summary>
        public Dictionary<string, long> GrantedRewards { get; set; } = new Dictionary<string, long>();
    }

    /// <summary>아군이 잡아 가둔 적 장수 (captivity.md 1.2).</summary>
    public sealed class PrisonerState
    {
        public string GeneralId { get; set; }
        public long CapturedAtUnixSeconds { get; set; }
        /// <summary>마지막 등용 시도 시각. 시도한 적 없으면 0.</summary>
        public long LastRecruitAttemptUnixSeconds { get; set; }
        /// <summary>잡은 전투의 논쟁에서 세뇌로 이긴 상대 → 등용 성공률 보너스.</summary>
        public bool BrainwashBonus { get; set; }
    }

    public static class GeneralStatus
    {
        public const string Available = "available";
        public const string LightInjury = "lightInjury";
        public const string SeriousInjury = "seriousInjury";
        public const string Captured = "captured";

        /// <summary>출전 가능 여부 (경상은 출전 가능, battle.md 2.5.3).</summary>
        public static bool CanDeploy(string status) => status == Available || status == LightInjury;
    }

    /// <summary>보유 부대 1개 (장수와 따로 보유, formation.md 2장).</summary>
    public sealed class UnitState
    {
        public string UnitId { get; set; }
        public string TroopType { get; set; }
        public long Troops { get; set; }
        public long Wounded { get; set; }
        public long WoundedRecoverAtUnixSeconds { get; set; }
        public Fix Training { get; set; }
        public Fix Cohesion { get; set; }
        /// <summary>승급으로 갈라져 나온 부대면 그 기본(위) 부대 id (troop-growth.md 2장, R-52). 기본 부대는 null.</summary>
        public string ParentUnitId { get; set; }
    }

    /// <summary>군단 편성: 장수 1명 + 소부대 칸 6개 (칸 id → 부대 id).</summary>
    public sealed class CorpsComposition
    {
        public string GeneralId { get; set; }
        public string Formation { get; set; }
        public string Role { get; set; }
        public Dictionary<string, string> Slots { get; set; } = new Dictionary<string, string>();
    }
}
