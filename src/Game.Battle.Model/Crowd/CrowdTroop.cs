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
    public sealed class CrowdTroop
    {
        public string Id = "infantry";
        /// <summary>1단계 뿌리 병종 (infantry · spearman · archer · cavalry · scout) — 화면 모형.</summary>
        public string BaseId = "infantry";
        public IReadOnlyList<string> Tags = new[] { "infantry", "melee" };
        public Fix Attack = Fix.FromInt(50), Defense = Fix.FromInt(50), Speed = Fix.FromInt(45);
        public Fix AttackRange = Fix.OneValue;

        public bool Has(string tag) { foreach (var t in Tags) if (t == tag) return true; return false; }
        public bool Ranged => Has("ranged");
        public bool Mounted => Has("mounted");
        public bool Polearm => Has("polearm");

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
