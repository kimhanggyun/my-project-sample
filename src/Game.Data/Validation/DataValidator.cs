using System.Collections.Generic;
using Game.Data.Defs;

namespace Game.Data.Validation
{
    /// <summary>
    /// 데이터 검사. 참조 id 존재, 상성표 대칭(R-14), 핵심 진형 2/2(R-15),
    /// 상점 상품 플레이 경로(R-30), 화면 문구 키 존재 등. 문제 목록을 돌려준다 (비어 있으면 통과).
    /// </summary>
    public static class DataValidator
    {
        public static List<string> Validate(DefinitionSet d)
        {
            var errors = new List<string>();

            foreach (var t in d.TroopTypes.Values)
            {
                if (t.Parent != null && !d.TroopTypes.ContainsKey(t.Parent))
                    errors.Add($"병종 '{t.Id}': 없는 상위 병종 '{t.Parent}'.");
                if (t.Tags.Count == 0) errors.Add($"병종 '{t.Id}': 태그가 없습니다.");
                if (t.SupplyPerDay < 1) errors.Add($"병종 '{t.Id}': 하루 군량은 1 이상이어야 합니다.");
                CheckStatRanges(d, t.Stats, "병종 " + t.Id, errors);
                CheckString(d, t.NameKey, errors);
            }

            foreach (var kv in d.FormationWins)
            {
                if (!d.Formations.ContainsKey(kv.Key)) errors.Add($"진형 상성: 없는 진형 '{kv.Key}'.");
                foreach (var target in kv.Value)
                {
                    if (!d.Formations.ContainsKey(target)) errors.Add($"진형 상성: 없는 진형 '{target}'.");
                    if (target == kv.Key) errors.Add($"진형 상성: '{kv.Key}'가 자기 자신에게 유리.");
                    // R-14: A가 B에 유리하면 B가 A에 유리할 수 없다
                    if (d.FormationWins.TryGetValue(target, out var back) && back.Contains(kv.Key))
                        errors.Add($"진형 상성 대칭 위반: '{kv.Key}'와 '{target}'가 서로 유리.");
                }
            }
            // R-15: 핵심 진형끼리 유리 2, 불리 2
            var core = new List<string>();
            foreach (var f in d.Formations.Values) { if (f.Group == "core") core.Add(f.Id); CheckString(d, f.NameKey, errors); }
            foreach (var a in core)
            {
                int w = 0, l = 0;
                foreach (var b in core)
                {
                    if (a == b) continue;
                    if (Wins(d, a, b)) w++;
                    if (Wins(d, b, a)) l++;
                }
                if (w != 2 || l != 2) errors.Add($"핵심 진형 '{a}': 유리 {w}, 불리 {l} (2/2여야 함).");
            }

            foreach (var g in d.Generals.Values)
            {
                CheckStatRanges(d, g.Stats, "장수 " + g.Id, errors);
                CheckString(d, g.NameKey, errors);
            }

            foreach (var p in d.Products.Values)
                if (!p.Convenience && p.PlayPaths.Count == 0)
                    errors.Add($"상품 '{p.Id}': 편의 기능이 아닌데 플레이로 얻는 경로가 없습니다 (R-30).");

            var kit = d.StartingKit;
            var kitUnits = new HashSet<string>();
            foreach (var u in kit.Units)
            {
                if (!kitUnits.Add(u.Id)) errors.Add($"초기 지급: 부대 id '{u.Id}' 중복.");
                if (!d.TroopTypes.ContainsKey(u.TroopType)) errors.Add($"초기 지급: 부대 '{u.Id}'의 없는 병종 '{u.TroopType}'.");
                if (u.Troops < 0 || u.Troops > d.Balance.UnitMaxTroops) errors.Add($"초기 지급: 부대 '{u.Id}' 병력 {u.Troops}가 범위를 벗어남.");
            }
            foreach (var g in kit.Generals)
                if (!d.Generals.ContainsKey(g)) errors.Add($"초기 지급: 없는 장수 '{g}'.");
            foreach (var f in kit.Formations)
                if (!d.Formations.ContainsKey(f)) errors.Add($"초기 지급: 없는 진형 '{f}'.");
            foreach (var c in kit.Compositions)
            {
                if (!d.Generals.ContainsKey(c.General)) errors.Add($"초기 편성: 없는 장수 '{c.General}'.");
                if (!d.Formations.ContainsKey(c.Formation)) errors.Add($"초기 편성: 없는 진형 '{c.Formation}'.");
                foreach (var s in c.Slots)
                {
                    if (!SlotIds.IsValid(s.Key)) errors.Add($"초기 편성: 없는 칸 '{s.Key}'.");
                    if (!kitUnits.Contains(s.Value)) errors.Add($"초기 편성: 없는 부대 '{s.Value}'.");
                }
            }

            foreach (var s in d.Stats.Values) CheckString(d, s.NameKey, errors);

            // 전투 데이터
            foreach (var f in d.Formations.Values)
                foreach (var slot in SlotIds.All)
                    if (!f.Slots.ContainsKey(slot)) errors.Add($"진형 '{f.Id}': 칸 '{slot}' 위치가 없습니다.");
            foreach (var f in d.Formations.Values)
                foreach (var slot in f.Slots.Keys)
                    if (!SlotIds.IsValid(slot)) errors.Add($"진형 '{f.Id}': 없는 칸 '{slot}'.");
            foreach (var g in d.Generals.Values)
                if (!d.Temperaments.ContainsKey(g.Temperament)) errors.Add($"장수 '{g.Id}': 없는 성향 '{g.Temperament}'.");
            foreach (var sk in d.Skills.Values)
            {
                CheckString(d, sk.NameKey, errors);
                if (sk.Cost < Foundation.Math.Fix.Zero) errors.Add($"스킬 '{sk.Id}': 비용이 음수.");
                if (sk.UsesPerBattle < 1) errors.Add($"스킬 '{sk.Id}': 전투당 횟수는 1 이상.");
                if (sk.Effects.Count == 0) errors.Add($"스킬 '{sk.Id}': 효과가 없습니다.");
                foreach (var r in sk.Requires)
                    if (!d.Stats.ContainsKey(r.Stat)) errors.Add($"스킬 '{sk.Id}': 없는 능력치 '{r.Stat}'.");
            }
            foreach (var g in d.Generals.Values)
                foreach (var sk in g.InnateSkills)
                    if (!d.Skills.ContainsKey(sk)) errors.Add($"장수 '{g.Id}': 없는 기본 스킬 '{sk}'.");
            foreach (var t in d.GrowthTracks)
                foreach (var r in t.Rules)
                {
                    if (r.RewardStat != null && !d.Stats.ContainsKey(r.RewardStat)) errors.Add($"성장 '{t.Track}': 없는 능력치 '{r.RewardStat}'.");
                    if (r.Every <= 0 && r.AtLeast == null) errors.Add($"성장 '{t.Track}': 규칙 '{r.Counter}'에 every 또는 atLeast가 필요합니다.");
                }
            foreach (var (_, diff) in d.Ai.DelegationLevels)
                if (!d.Ai.Difficulties.ContainsKey(diff)) errors.Add($"위임: 없는 난이도 '{diff}'.");
            if (d.Ai.DelegationLevels.Count == 0) errors.Add("위임: 판단 수준이 없습니다.");
            foreach (var s in d.Ai.DelegationStats) if (!d.Stats.ContainsKey(s)) errors.Add($"위임: 없는 능력치 '{s}'.");
            var secretIds = new HashSet<string>();
            foreach (var sc in d.Secrets)
            {
                if (!secretIds.Add(sc.Id)) errors.Add($"숨은 보상 id '{sc.Id}' 중복.");
                if (sc.RewardSkill != null && !d.Skills.ContainsKey(sc.RewardSkill)) errors.Add($"숨은 보상 '{sc.Id}': 없는 스킬 '{sc.RewardSkill}'.");
                if (sc.RewardSkill == null && sc.RewardTrait == null) errors.Add($"숨은 보상 '{sc.Id}': 보상이 없습니다.");
                if (sc.CasterSkill != null && !d.Skills.ContainsKey(sc.CasterSkill)) errors.Add($"숨은 보상 '{sc.Id}': 없는 스킬 '{sc.CasterSkill}'.");
                if (sc.HintKey != null) CheckString(d, sc.HintKey, errors);
            }
            if (d.Levels.MaxLevel < 1 || d.Levels.ExpBase < 1) errors.Add("레벨: 최대 레벨·기본 경험치는 1 이상.");
            foreach (var sp in d.Speech.Situations.Values)
            {
                CheckString(d, sp.LineKey, errors);
                if (!d.Speech.KindPriority.ContainsKey(sp.Kind)) errors.Add($"말풍선 '{sp.Event}': 없는 종류 '{sp.Kind}'.");
            }
            var baseActions = new[] { "attack", "evade", "charge", "bow", "retreat", ContestDef.Special };
            foreach (var ct in d.Contests.Values)
            {
                if (!d.Stats.ContainsKey(ct.Stat)) errors.Add($"대결 '{ct.Id}': 없는 능력치 '{ct.Stat}'.");
                CheckString(d, ct.ResourceKey, errors);
                CheckString(d, ct.ChargeKey, errors);
                foreach (var a in ct.Actions)
                {
                    CheckString(d, a.NameKey, errors);
                    if (System.Array.IndexOf(baseActions, a.Base) < 0) errors.Add($"대결 '{ct.Id}': 행동 '{a.Id}'의 기본 행동 '{a.Base}'가 없습니다.");
                }
                // 결과표는 모든 기본 행동 쌍을 채워야 한다
                foreach (var mine in baseActions)
                    foreach (var theirs in baseActions)
                        if (!ct.Outcomes.TryGetValue(mine, out var row) || !row.ContainsKey(theirs))
                            errors.Add($"대결 '{ct.Id}': 결과표 [{mine}][{theirs}]가 없습니다.");
            }
            foreach (var t in d.TerrainRules.Values)
            {
                if (t.Flammable && (t.BurnsTo == null || !d.TerrainRules.ContainsKey(t.BurnsTo)))
                    errors.Add($"지형 '{t.Id}': 불탄 뒤 지형 '{t.BurnsTo}'가 이동 규칙에 없습니다.");
                if (t.Flammable && t.BurnSec <= Foundation.Math.Fix.Zero) errors.Add($"지형 '{t.Id}': 타는 시간은 0보다 커야 합니다.");
            }
            foreach (var t in d.TerrainRules.Values)
                if (t.Cost <= Foundation.Math.Fix.Zero) errors.Add($"지형 '{t.Id}': 이동 비용은 0보다 커야 합니다.");
            return errors;
        }

        /// <summary>스텝 검사: 맵·적 장수·진형·병종·칸·AI·날씨가 데이터에 있고, 적 군단 병력이 한도 안이다.</summary>
        public static List<string> ValidateStep(DefinitionSet d, StepDef s, MapDef map)
        {
            var errors = ValidateMap(d, map);
            string c = $"스텝 '{s.Id}'";
            if (!d.Weathers.ContainsKey(s.Weather)) errors.Add($"{c}: 없는 날씨 '{s.Weather}'.");
            if (!d.TimesOfDay.ContainsKey(s.TimeOfDay)) errors.Add($"{c}: 없는 시간대 '{s.TimeOfDay}'.");
            CheckString(d, s.NameKey, errors);
            if (s.Enemy.Count == 0) errors.Add($"{c}: 적 군단이 없습니다.");
            var ids = new HashSet<string>();
            foreach (var e in s.Enemy)
            {
                if (!ids.Add(e.Id)) errors.Add($"{c}: 적 군단 id '{e.Id}' 중복.");
                if (!d.Generals.ContainsKey(e.General)) errors.Add($"{c}: 없는 적 장수 '{e.General}'.");
                if (!d.Formations.ContainsKey(e.Formation)) errors.Add($"{c}: 없는 진형 '{e.Formation}'.");
                if (!d.Ai.Difficulties.ContainsKey(e.Difficulty)) errors.Add($"{c}: 없는 AI 난이도 '{e.Difficulty}'.");
                foreach (var f in e.KnownFormations) if (!d.Formations.ContainsKey(f)) errors.Add($"{c}: 없는 진형 '{f}'.");
                var slots = new HashSet<string>();
                foreach (var u in e.Units)
                {
                    if (!SlotIds.IsValid(u.Slot) || !slots.Add(u.Slot)) errors.Add($"{c}: 군단 '{e.Id}'의 칸 '{u.Slot}'가 없거나 중복.");
                    if (!d.TroopTypes.ContainsKey(u.TroopType)) errors.Add($"{c}: 없는 병종 '{u.TroopType}'.");
                    if (u.Troops < 0 || u.Troops > d.Balance.UnitMaxTroops) errors.Add($"{c}: 군단 '{e.Id}' 부대 병력 {u.Troops}가 범위를 벗어남.");
                }
            }
            var corpsIds = new HashSet<string>(ids);
            foreach (var ev in s.Events)
                foreach (var a in ev.Actions)
                {
                    if (a.Corps != null && !corpsIds.Contains(a.Corps)) errors.Add($"{c}: 연출 '{ev.Id}'의 없는 군단 '{a.Corps}'.");
                    if (a.Key != null) CheckString(d, a.Key, errors);
                }
            foreach (var f in s.RewardFormations) if (!d.Formations.ContainsKey(f)) errors.Add($"{c}: 보상 진형 '{f}'가 없습니다.");
            foreach (var k in s.RewardSkills) if (!d.Skills.ContainsKey(k)) errors.Add($"{c}: 보상 스킬 '{k}'가 없습니다.");
            return errors;
        }

        /// <summary>맵 검사: 지형 id가 이동 규칙에 있고, 시작 위치가 맵 안의 지나갈 수 있는 칸이며, 퇴각 방향이 올바르다.</summary>
        public static List<string> ValidateMap(DefinitionSet d, MapDef map)
        {
            var errors = new List<string>();
            foreach (var t in map.TerrainIds)
                if (!d.TerrainRules.ContainsKey(t)) errors.Add($"맵 '{map.Id}': 이동 규칙이 없는 지형 '{t}'.");
            foreach (var kv in map.Spawns)
            {
                int x = (int)kv.Value.X.FloorToLong(), y = (int)kv.Value.Y.FloorToLong();
                if (!map.InBounds(x, y)) { errors.Add($"맵 '{map.Id}': '{kv.Key}' 시작 위치가 맵 밖."); continue; }
                if (d.TerrainRules.TryGetValue(map.TerrainAt(x, y), out var rule) && rule.BlocksEveryone)
                    errors.Add($"맵 '{map.Id}': '{kv.Key}' 시작 위치가 지나갈 수 없는 칸.");
                if (!map.RetreatEdges.ContainsKey(kv.Key)) errors.Add($"맵 '{map.Id}': '{kv.Key}' 퇴각 방향이 없습니다.");
            }
            foreach (var kv in map.RetreatEdges)
            {
                bool ok = false;
                foreach (var e in MapDef.Edges) if (e == kv.Value) ok = true;
                if (!ok) errors.Add($"맵 '{map.Id}': '{kv.Key}'의 퇴각 방향 '{kv.Value}'가 올바르지 않습니다.");
            }
            return errors;
        }

        public static bool Wins(DefinitionSet d, string attacker, string defender) =>
            d.FormationWins.TryGetValue(attacker, out var set) && set.Contains(defender);

        private static void CheckStatRanges(DefinitionSet d, StatBlock block, string context, List<string> errors)
        {
            foreach (var kv in block.Explicit)
            {
                var def = d.Stats[kv.Key];
                if (kv.Value < def.Min || kv.Value > def.Max)
                    errors.Add($"{context}: 능력치 '{kv.Key}' {kv.Value}가 범위({def.Min}~{def.Max})를 벗어남.");
            }
        }

        private static void CheckString(DefinitionSet d, string key, List<string> errors)
        {
            if (!d.Strings.ContainsKey(key)) errors.Add($"화면 문구 '{key}'가 문자열 표에 없습니다.");
        }
    }
}
