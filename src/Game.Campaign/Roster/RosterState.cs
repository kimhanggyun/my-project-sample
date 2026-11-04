using System.Collections.Generic;
using Game.Foundation.Math;

namespace Game.Campaign.Roster
{
    /// <summary>보유 장수 1명의 상태 (세이브됨). 정의(GeneralDef)는 데이터에, 바뀌는 값만 여기.</summary>
    public sealed class GeneralState
    {
        public string GeneralId { get; set; }
        public int Level { get; set; } = 1;
        public long Exp { get; set; }
        /// <summary>정의 능력치에 더해진 성장분 (능력치 id → 값).</summary>
        public Dictionary<string, Fix> StatGrowth { get; set; } = new Dictionary<string, Fix>();
        public List<string> LearnedSkills { get; set; } = new List<string>();
        public List<string> LearnedTraits { get; set; } = new List<string>();
        /// <summary>"available" / "lightInjury" / "seriousInjury" / "captured"</summary>
        public string Status { get; set; } = GeneralStatus.Available;
        public long StatusUntilUnixSeconds { get; set; }
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
