using System.Collections.Generic;
using Game.Data.Defs;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Unity.Battle3D
{
    /// <summary>
    /// 병사 겉모습 (R-149, 2026-11-24 사용자 "갑옷 세트를 입히면 몸통 · 모자까지 그 색, 금괴는 투구 · 결제는 깃 투구, 방패는 네모 · 무늬, 방패병은 긴 네모 방패").
    /// = 병종 기본 모양 (cosmetics.json troopLooks) + 입은 갑옷 세트의 look (cosmetics.json items[].look) + 말 색.
    /// look 값: "모양:색" · "모양" · "색" (색 이름은 CosmeticLooks). 키: body 몸통 · legs 다리 · hat 모자 (kasa · helmet · plume) · shield 방패 (round · square · tower) ·
    /// pattern 무늬 (none · rim · cross · boss) · trim 장식 색 (테 · 무늬 · 꼭지 · 어깨) · plume 깃 색 · helmet (예전 키 = 모자 색).
    /// 색이 없는 부위는 편 색 (몸통 · 모자) · 편의 어두운 색 (다리 · 방패 · 장식) — 아무것도 안 입으면 예전 모습과 같은 색.
    /// 겉모습만 — 전투 계산과 무관.
    /// </summary>
    public sealed class SoldierLook
    {
        public const string HatKasa = "kasa", HatHelmet = "helmet", HatPlume = "plume";
        public const string ShieldRound = "round", ShieldSquare = "square", ShieldTower = "tower";
        public const string PatternNone = "none", PatternRim = "rim", PatternCross = "cross", PatternBoss = "boss";
        private static readonly HashSet<string> Hats = new HashSet<string> { HatKasa, HatHelmet, HatPlume };
        private static readonly HashSet<string> Shields = new HashSet<string> { ShieldRound, ShieldSquare, ShieldTower };
        private static readonly HashSet<string> Patterns = new HashSet<string> { PatternNone, PatternRim, PatternCross, PatternBoss };

        public string Hat = HatKasa, Shield = ShieldRound, Pattern = PatternNone;
        public Color? Body, Legs, HatColor, ShieldColor, Accent, Plume, Horse;
        /// <summary>갑옷 세트를 입음 (어깨 덧댐을 붙임).</summary>
        public bool Armored;

        /// <summary>모양 열쇠 (메시 캐시).</summary>
        public string ShapeKey => Hat + "|" + Shield + "|" + Pattern;

        /// <summary>모양 + 색 열쇠 (재질 묶음 캐시).</summary>
        public string Key => ShapeKey + "|" + (Armored ? "a" : "") + "|" + C(Body) + C(Legs) + C(HatColor) + C(ShieldColor) + C(Accent) + C(Plume) + C(Horse);

        private static string C(Color? c) => c.HasValue ? ColorUtility.ToHtmlStringRGB(c.Value) : "-";

        /// <summary>병종 id · 갑옷 세트 id · 말 색 id로 겉모습 (모르는 id는 무시).</summary>
        public static SoldierLook Resolve(DefinitionSet defs, string troopId, string armorId, string horseId)
        {
            var look = new SoldierLook();
            if (defs?.CosmeticsDoc?["troopLooks"] is JObject troops && troopId != null && troops[troopId] is JObject t)
                foreach (var p in t.Properties()) if (p.Value.Type == JTokenType.String) look.Apply(p.Name, (string)p.Value);
            var armor = CosmeticLooks.Of(defs, armorId);
            if (armor != null)
            {
                // 병종이 정한 모양은 지킴 (2026-11-24 캡처: 근위병이 무쇠 세트를 입자 긴 방패 · 투구가 둥근 방패 · 삿갓으로 바뀜):
                // 방패 모양 = 병종 것 (방패병 → 긴 네모), 모자 = 병종 · 세트 중 높은 등급 (삿갓 < 투구 < 깃 투구), 무늬 = 세트에 있으면 세트. 색은 세트
                string troopShield = look.Shield, troopHat = look.Hat;
                bool troopShieldSet = look.Shield != ShieldRound;
                look.Armored = true;
                foreach (var kv in armor) look.Apply(kv.Key, kv.Value);
                if (troopShieldSet) look.Shield = troopShield;
                if (HatRank(troopHat) > HatRank(look.Hat)) look.Hat = troopHat;
            }
            var horse = CosmeticLooks.Of(defs, horseId);
            if (horse != null && horse.TryGetValue("horse", out var h) && CosmeticLooks.TryNamed(h, out var hc)) look.Horse = hc;
            return look;
        }

        private static int HatRank(string hat) => hat == HatPlume ? 2 : hat == HatHelmet ? 1 : 0;

        /// <summary>look 값 하나를 반영 ("모양:색" · "모양" · "색").</summary>
        public void Apply(string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            int colon = value.IndexOf(':');
            string first = colon >= 0 ? value.Substring(0, colon) : value;
            string color = colon >= 0 ? value.Substring(colon + 1) : value;
            bool hasColor = CosmeticLooks.TryNamed(color, out var c);
            switch (key)
            {
                case "body": if (hasColor) Body = c; break;
                case "legs": if (hasColor) Legs = c; break;
                case "helmet": if (hasColor) HatColor = c; break;
                case "hat":
                    if (Hats.Contains(first)) Hat = first;
                    if (hasColor && (colon >= 0 || !Hats.Contains(value))) HatColor = c;
                    break;
                case "shield":
                    if (Shields.Contains(first)) Shield = first;
                    if (hasColor && (colon >= 0 || !Shields.Contains(value))) ShieldColor = c;
                    break;
                case "pattern": if (Patterns.Contains(value)) Pattern = value; break;
                case "trim": if (hasColor) Accent = c; break;
                case "plume": if (hasColor) Plume = c; break;
            }
        }

        /// <summary>부위 색: 정한 색이 없으면 편 색 · 편의 어두운 색.</summary>
        public Color PartColor(int part, Color team, Color dark)
        {
            switch (part)
            {
                case SoldierMeshes.Team: return Body ?? team;
                case SoldierMeshes.Hat: return HatColor ?? Body ?? team;
                case SoldierMeshes.Shield: return ShieldColor ?? dark;
                case SoldierMeshes.Accent: return Accent ?? dark;
                case SoldierMeshes.Armor: return Accent ?? dark;
                case SoldierMeshes.Plume: return Plume ?? Accent ?? team;
                case SoldierMeshes.Dark: return Legs ?? dark;
                case SoldierMeshes.Horse: return Horse ?? SoldierMeshes.DarkColor;
                case SoldierMeshes.Skin: return SoldierMeshes.SkinColor;
                case SoldierMeshes.Metal: return SoldierMeshes.MetalColor;
                default: return team;
            }
        }
    }
}
