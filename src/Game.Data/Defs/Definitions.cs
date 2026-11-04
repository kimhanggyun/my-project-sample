using System.Collections.Generic;
using Game.Foundation.Math;

namespace Game.Data.Defs
{
    // 데이터 파일에서 읽은 정의. 모두 읽기 전용 (실행 중 바뀌는 값은 각 모듈의 상태 객체가 가진다).

    public sealed class StatDefinition
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        public Fix Min { get; internal set; }
        public Fix Max { get; internal set; }
        public Fix Default { get; internal set; }
    }

    /// <summary>이름-값 능력치 묶음. 없는 항목은 정의의 기본값.</summary>
    public sealed class StatBlock
    {
        private readonly IReadOnlyDictionary<string, Fix> _values;
        private readonly IReadOnlyDictionary<string, StatDefinition> _defs;

        internal StatBlock(IReadOnlyDictionary<string, Fix> values, IReadOnlyDictionary<string, StatDefinition> defs)
        {
            _values = values;
            _defs = defs;
        }

        public Fix Get(string statId)
        {
            if (_values.TryGetValue(statId, out var v)) return v;
            if (_defs.TryGetValue(statId, out var d)) return d.Default;
            throw new Json.DataException($"알 수 없는 능력치 '{statId}'.");
        }

        public IEnumerable<KeyValuePair<string, Fix>> Explicit => _values;
    }

    public sealed class TroopTypeDef
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        public int Tier { get; internal set; }
        public string Parent { get; internal set; }
        public IReadOnlyList<string> Tags { get; internal set; }
        public long SupplyPerDay { get; internal set; }
        public Fix AttackRange { get; internal set; }
        public StatBlock Stats { get; internal set; }
        public bool HasTag(string tag)
        {
            foreach (var t in Tags) if (t == tag) return true;
            return false;
        }
    }

    public sealed class FormationDef
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        /// <summary>"core"(상성 순환 6종) 또는 "situational".</summary>
        public string Group { get; internal set; }
        public string Tier { get; internal set; }
        /// <summary>칸 id → 장수 기준 위치.</summary>
        public IReadOnlyDictionary<string, SlotOffset> Slots { get; internal set; }
    }

    public sealed class GeneralDef
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        public string Grade { get; internal set; }
        public string Temperament { get; internal set; }
        public IReadOnlyList<string> Traits { get; internal set; }
        public IReadOnlyList<string> InnateSkills { get; internal set; }
        public bool Recruitable { get; internal set; }
        public StatBlock Stats { get; internal set; }
        /// <summary>성장률: 레벨업 1번에 오르는 능력치 (무작위 없음).</summary>
        public IReadOnlyDictionary<string, Fix> GrowthRates { get; internal set; }
    }

    public sealed class StartingUnitDef
    {
        public string Id { get; internal set; }
        public string TroopType { get; internal set; }
        public long Troops { get; internal set; }
    }

    public sealed class StartingCompositionDef
    {
        public string General { get; internal set; }
        public string Formation { get; internal set; }
        public string Role { get; internal set; }
        /// <summary>슬롯 id → 부대 id.</summary>
        public IReadOnlyDictionary<string, string> Slots { get; internal set; }
    }

    public sealed class StartingKitDef
    {
        public IReadOnlyList<string> Generals { get; internal set; }
        public IReadOnlyList<StartingUnitDef> Units { get; internal set; }
        public IReadOnlyList<StartingCompositionDef> Compositions { get; internal set; }
        public IReadOnlyDictionary<string, long> Wallet { get; internal set; }
        public IReadOnlyList<string> Formations { get; internal set; }
    }

    public sealed class ProductDef
    {
        public string Id { get; internal set; }
        public string Kind { get; internal set; }
        /// <summary>편의 기능(광고 제거 등)은 결제로만 얻어도 된다.</summary>
        public bool Convenience { get; internal set; }
        public IReadOnlyList<string> PlayPaths { get; internal set; }
    }

    public static class SlotIds
    {
        public const string Vanguard = "vanguard";
        public const string Center = "center";
        public const string LeftWing = "leftWing";
        public const string RightWing = "rightWing";
        public const string LeftGuard = "leftGuard";
        public const string RightGuard = "rightGuard";

        public static readonly IReadOnlyList<string> All = new[] { Vanguard, Center, LeftWing, RightWing, LeftGuard, RightGuard };

        public static bool IsValid(string id)
        {
            foreach (var s in All) if (s == id) return true;
            return false;
        }
    }
}
