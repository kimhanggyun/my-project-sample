using System.Collections.Generic;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Battle.Model.Crowd
{
    /// <summary>
    /// 샘플전투2 병종 (R-127): 기존 전투의 병종 데이터(troopTypes.json)에서 병사 단위 전투에 쓸 값을 뽑는다.
    /// 빠르기 = 기본 걸음 × speed / 기준 빠르기, 닿는 거리 = 기본 + (attackRange − 1) × 긴 무기 칸, 원거리 = attackRange × rangeScale 칸 안이면 활을 쏜다.
    /// 병종 데이터가 없으면 보병 기본값.
    /// </summary>
    /// <summary>샘플전투2 전용 병종 조정 (R-154): 정한 값만 덮어씀.</summary>
    public sealed class CrowdTroopTuning
    {
        public Fix? Attack, Defense, AttackSeconds, ShootSeconds;
    }

    public sealed class CrowdTroop
    {
        public string Id = "infantry";
        /// <summary>1단계 뿌리 병종 (infantry · spearman · archer · cavalry · scout) — 화면 모형.</summary>
        public string BaseId = "infantry";
        public IReadOnlyList<string> Tags = new[] { "infantry", "melee" };
        public Fix Attack = Fix.FromInt(50), Defense = Fix.FromInt(50), Speed = Fix.FromInt(45);
        public Fix AttackRange = Fix.OneValue;
        /// <summary>병종별 근접 공격 간격 · 쏘기 간격 (초, R-154 crowd.json troopTuning — 없으면 공통 attackSeconds · shootSeconds).</summary>
        public Fix? AttackSeconds, ShootSeconds;
        /// <summary>
        /// 자기 편 닫힌 성문을 지나감 (R-153, 2026-11-26 사용자 "성문이 열리기 전에 모든 병과가 미리 나가서 복병 등"): 지키는 쪽 부대만 (쪽문처럼).
        /// 공격 쪽은 그대로 막힘. 맞붙기 판정(성문 너머로 치기)은 <see cref="Plain"/>으로 — 문을 사이에 두고 치지는 못함.
        /// </summary>
        public bool GatePass;
        /// <summary>성문을 지나가지 않는 원래 병종 (GatePass면 복사 전 것, 아니면 자기).</summary>
        public CrowdTroop Plain => _plain ?? this;
        private CrowdTroop _plain;
        /// <summary>길 · 칸 캐시 열쇠 (성문 통과 여부까지).</summary>
        public string Key => GatePass ? Id + "#gate" : Id;

        /// <summary>자기 편 성문을 지나가는 복사본 (R-153).</summary>
        public CrowdTroop WithGatePass() => new CrowdTroop
        {
            Id = Id, BaseId = BaseId, Tags = Tags, Attack = Attack, Defense = Defense, Speed = Speed, AttackRange = AttackRange,
            AttackSeconds = AttackSeconds, ShootSeconds = ShootSeconds,
            GatePass = true, _plain = this,
        };

        public bool Has(string tag) { foreach (var t in Tags) if (t == tag) return true; return false; }
        public bool Ranged => Has("ranged");
        public bool Mounted => Has("mounted");
        public bool Polearm => Has("polearm");

        /// <summary>병종 데이터 + 샘플전투2 전용 조정 (R-154, crowd.json troopTuning: 병종 id → attack · defense · attackSeconds · shootSeconds). 옛 전투와 무관.</summary>
        public static CrowdTroop Of(string id, IReadOnlyDictionary<string, TroopTypeDef> types, IReadOnlyDictionary<string, CrowdTroopTuning> tuning)
        {
            var t = Of(id, types);
            if (tuning != null && t.Id != null && tuning.TryGetValue(t.Id, out var u))
            {
                if (u.Attack.HasValue) t.Attack = u.Attack.Value;
                if (u.Defense.HasValue) t.Defense = u.Defense.Value;
                t.AttackSeconds = u.AttackSeconds;
                t.ShootSeconds = u.ShootSeconds;
            }
            return t;
        }

        public static CrowdTroop Of(string id, IReadOnlyDictionary<string, TroopTypeDef> types)
        {
            var t = new CrowdTroop { Id = id ?? "infantry", BaseId = id ?? "infantry" };
            if (types == null || id == null || !types.TryGetValue(id, out var def)) return t;
            t.Tags = def.Tags;
            t.Attack = def.Stats.Get("attack");
            t.Defense = def.Stats.Get("defense");
            t.Speed = def.Stats.Get("speed");
            t.AttackRange = def.AttackRange;
            string b = id;
            while (types.TryGetValue(b, out var d) && d.Parent != null) b = d.Parent;
            t.BaseId = b;
            return t;
        }
    }
}
