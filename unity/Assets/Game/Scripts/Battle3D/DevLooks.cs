using System.Collections.Generic;
using Game.Flow;
using Game.Unity.Boot;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Unity.Battle3D
{
    /// <summary>
    /// 개발용 겉모습 고르기 (R-149 · R-150, [병사꾸미기] · [장수꾸미기] 탭 — 2026-11-24 사용자 "개발 단계에서만, 나중엔 병과 · 갑옷 세트를 사면 자동 적용").
    /// 병종 id (또는 "general:성향")마다: 입혀 볼 갑옷 세트 · 말 색 · 부위별 부품 바꿈 · 색 자리별 색을 이 기기에만 저장 (구매 · 입은 치장과 별개).
    /// 고른 것이 없으면 실제로 입은 치장. 샘플전투2의 우리 편 · 우리 장수가 이것으로 그린다.
    /// </summary>
    public static class DevLooks
    {
        private const string Key = "looks.dev.v2", OldKey = "looks.dev.v1";

        public sealed class Entry
        {
            public string Armor, Horse;
            /// <summary>부위 → 부품 번호 ("-" = 비움).</summary>
            public readonly Dictionary<string, string> Parts = new Dictionary<string, string>();
            /// <summary>색 자리 → 색 이름.</summary>
            public readonly Dictionary<string, string> Colors = new Dictionary<string, string>();
            public bool IsEmpty => Armor == null && Horse == null && Parts.Count == 0 && Colors.Count == 0;
        }

        private static readonly Dictionary<string, Entry> Picks = new Dictionary<string, Entry>();
        private static bool _loaded;

        public static string GeneralKey(string preset) => "general:" + preset;

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            string s = null;
            try { s = PlayerPrefs.GetString(Key, ""); } catch { }
            if (!string.IsNullOrEmpty(s))
            {
                try
                {
                    foreach (var p in JObject.Parse(s).Properties())
                    {
                        if (!(p.Value is JObject o)) continue;
                        var e = new Entry { Armor = (string)o["armor"], Horse = (string)o["horse"] };
                        if (o["parts"] is JObject parts) foreach (var kv in parts.Properties()) e.Parts[kv.Name] = (string)kv.Value;
                        if (o["colors"] is JObject cols) foreach (var kv in cols.Properties()) e.Colors[kv.Name] = (string)kv.Value;
                        Picks[p.Name] = e;
                    }
                }
                catch { }
                return;
            }
            // 예전 저장 (R-149: "병종=세트|말;…")
            try { s = PlayerPrefs.GetString(OldKey, ""); } catch { }
            if (string.IsNullOrEmpty(s)) return;
            foreach (var part in s.Split(';'))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;
                var v = kv[1].Split('|');
                Picks[kv[0]] = new Entry { Armor = v.Length > 0 && v[0].Length > 0 ? v[0] : null, Horse = v.Length > 1 && v[1].Length > 0 ? v[1] : null };
            }
        }

        private static void Save()
        {
            var root = new JObject();
            foreach (var kv in Picks)
            {
                if (kv.Value.IsEmpty) continue;
                var o = new JObject();
                if (kv.Value.Armor != null) o["armor"] = kv.Value.Armor;
                if (kv.Value.Horse != null) o["horse"] = kv.Value.Horse;
                if (kv.Value.Parts.Count > 0) o["parts"] = JObject.FromObject(kv.Value.Parts);
                if (kv.Value.Colors.Count > 0) o["colors"] = JObject.FromObject(kv.Value.Colors);
                root[kv.Key] = o;
            }
            try { PlayerPrefs.SetString(Key, root.ToString(Newtonsoft.Json.Formatting.None)); PlayerPrefs.Save(); } catch { }
        }

        /// <summary>그 열쇠(병종 id · "general:성향")의 고른 것 (없으면 빈 것).</summary>
        public static Entry Get(string key)
        {
            Load();
            if (key == null) return new Entry();
            if (!Picks.TryGetValue(key, out var e)) Picks[key] = e = new Entry();
            return e;
        }

        /// <summary>예전 호출 (갑옷 세트 · 말 색).</summary>
        public static (string armor, string horse) Pick(string troopId) { var e = Get(troopId); return (e.Armor, e.Horse); }

        public static void SetArmor(string key, string armorId) { Get(key).Armor = armorId; Save(); }
        public static void SetHorse(string key, string horseId) { Get(key).Horse = horseId; Save(); }

        /// <summary>부위에 부품 (null = 기본 차림대로, "-" = 비움).</summary>
        public static void SetPart(string key, string slot, string partId)
        {
            var e = Get(key);
            if (partId == null) e.Parts.Remove(slot); else e.Parts[slot] = partId;
            Save();
        }

        /// <summary>무기 묶음: 오른손 · 왼손 (없으면 비움) · 등 (묶음에 있으면).</summary>
        public static void SetLoadout(string key, LookLoadout l)
        {
            if (l == null) return;
            var e = Get(key);
            e.Parts[LookPartLibrary.SlotRight] = l.Right ?? "-";
            e.Parts[LookPartLibrary.SlotLeft] = l.Left ?? "-";
            if (l.Back != null) e.Parts[LookPartLibrary.SlotBack] = l.Back;
            Save();
        }

        /// <summary>색 자리에 색 이름 (null = 기본).</summary>
        public static void SetColor(string key, string slot, string colorName)
        {
            var e = Get(key);
            if (colorName == null) e.Colors.Remove(slot); else e.Colors[slot] = colorName;
            Save();
        }

        /// <summary>그 열쇠의 고른 것을 모두 지움 (기본 차림으로).</summary>
        public static void Reset(string key) { Load(); Picks.Remove(key); Save(); }

        /// <summary>고른 것 전부 (캡처 투어가 되돌릴 때).</summary>
        public static string Snapshot() { Load(); Save(); try { return PlayerPrefs.GetString(Key, ""); } catch { return ""; } }

        public static void Restore(string snapshot)
        {
            try { PlayerPrefs.SetString(Key, snapshot ?? ""); PlayerPrefs.Save(); } catch { }
            Picks.Clear();
            _loaded = false;
        }

        private static void ApplyOverrides(SoldierLook look, Entry e)
        {
            foreach (var kv in e.Parts) look.Set(kv.Key, kv.Value);
            foreach (var kv in e.Colors) if (CosmeticLooks.TryNamed(kv.Value, out var c)) look.Colors[kv.Key] = c;
        }

        /// <summary>그 병종의 지금 겉모습: 고른 세트 → 없으면 실제로 입은 치장, 그 위에 고른 부품 · 색.</summary>
        public static SoldierLook Resolve(GameContext ctx, string troopId)
        {
            var e = Get(troopId);
            string armor = e.Armor, horse = e.Horse;
            if (ctx?.State != null && ctx.Defs != null && (armor == null || horse == null))
            {
                string line = CosmeticService.LineOf(ctx.Defs, troopId);
                if (line != null)
                {
                    armor = armor ?? CosmeticService.Worn(ctx.State, line, CosmeticKinds.Armor);
                    horse = horse ?? CosmeticService.Worn(ctx.State, line, CosmeticKinds.Horse);
                }
            }
            var look = SoldierLook.Resolve(ctx, troopId, armor, horse);
            ApplyOverrides(look, e);
            return look;
        }

        /// <summary>장수 겉모습: 성향 기본 차림 + 고른 세트 · 부품 · 색.</summary>
        public static SoldierLook ResolveGeneral(GameContext ctx, string preset, bool commander)
        {
            var e = Get(GeneralKey(preset));
            var look = SoldierLook.ForGeneral(ctx, preset, commander);
            look.ApplySet(CosmeticLooks.Of(ctx?.Defs, e.Armor));
            var horse = CosmeticLooks.Of(ctx?.Defs, e.Horse);
            if (horse != null && horse.TryGetValue("horse", out var h) && CosmeticLooks.TryNamed(h, out var hc)) look.Colors["horse"] = hc;
            ApplyOverrides(look, e);
            return look;
        }

        /// <summary>
        /// 지금 차림을 상점 세트 후보로 (2026-11-24 사용자 "샌드박스에서 조합 → 캡처 → 상점 세트"): 부위별 번호 + 고른 색 이름.
        /// [세트로 저장]이 복사 · 로그에 남긴다.
        /// </summary>
        public static string Export(string key, SoldierLook look)
        {
            var o = new JObject { ["for"] = key };
            foreach (var s in LookPartLibrary.Slots) o[s] = look.Of(s);
            var e = Get(key);
            var cols = new JObject();
            foreach (var kv in e.Colors) cols[kv.Key] = kv.Value;
            o["colors"] = cols;
            if (e.Armor != null) o["baseSet"] = e.Armor;
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
