using System.Collections.Generic;
using System.Text;
using Game.Flow;
using Game.Unity.Boot;
using UnityEngine;

namespace Game.Unity.Battle3D
{
    /// <summary>
    /// 개발용 겉모습 고르기 (R-149, [꾸미기] 탭 — 2026-11-24 사용자 "개발 단계에서만, 나중엔 병과 · 갑옷 세트를 사면 자동 적용").
    /// 병종마다 입혀 볼 갑옷 세트 · 말 색을 이 기기에만 저장 (구매 · 입은 치장과 별개). 고른 것이 없으면 실제로 입은 치장.
    /// 샘플전투2의 우리 편이 이것으로 그린다.
    /// </summary>
    public static class DevLooks
    {
        private const string Key = "looks.dev.v1";
        private static readonly Dictionary<string, (string armor, string horse)> Picks = new Dictionary<string, (string, string)>();
        private static bool _loaded;

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            string s = null;
            try { s = PlayerPrefs.GetString(Key, ""); } catch { }
            if (string.IsNullOrEmpty(s)) return;
            foreach (var part in s.Split(';'))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;
                var v = kv[1].Split('|');
                Picks[kv[0]] = (v.Length > 0 && v[0].Length > 0 ? v[0] : null, v.Length > 1 && v[1].Length > 0 ? v[1] : null);
            }
        }

        private static void Save()
        {
            var sb = new StringBuilder();
            foreach (var kv in Picks)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key).Append('=').Append(kv.Value.armor ?? "").Append('|').Append(kv.Value.horse ?? "");
            }
            try { PlayerPrefs.SetString(Key, sb.ToString()); PlayerPrefs.Save(); } catch { }
        }

        /// <summary>그 병종에 고른 갑옷 세트 · 말 색 (없으면 null).</summary>
        public static (string armor, string horse) Pick(string troopId)
        {
            Load();
            return troopId != null && Picks.TryGetValue(troopId, out var p) ? p : (null, null);
        }

        public static void SetArmor(string troopId, string armorId) { Load(); var p = Pick(troopId); Picks[troopId] = (armorId, p.horse); Save(); }
        public static void SetHorse(string troopId, string horseId) { Load(); var p = Pick(troopId); Picks[troopId] = (p.armor, horseId); Save(); }

        /// <summary>그 병종의 지금 겉모습: 고른 것 → 없으면 실제로 입은 치장 → 없으면 병종 기본.</summary>
        public static SoldierLook Resolve(GameContext ctx, string troopId)
        {
            var (armor, horse) = Pick(troopId);
            if (ctx?.State != null && ctx.Defs != null && (armor == null || horse == null))
            {
                string line = CosmeticService.LineOf(ctx.Defs, troopId);
                if (line != null)
                {
                    armor = armor ?? CosmeticService.Worn(ctx.State, line, CosmeticKinds.Armor);
                    horse = horse ?? CosmeticService.Worn(ctx.State, line, CosmeticKinds.Horse);
                }
            }
            return SoldierLook.Resolve(ctx?.Defs, troopId, armor, horse);
        }
    }
}
