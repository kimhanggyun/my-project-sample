using System.Collections.Generic;
using System.Text;
using Game.Data.Defs;
using Game.Flow;
using Game.Unity.Boot;
using UnityEngine;

namespace Game.Unity.Battle3D
{
    /// <summary>
    /// 병사 · 장수 겉모습 (R-150, 2026-11-24 사용자 "투구 · 갑옷 · 망토 · 무기 · 방패 · 말을 부품으로, 높은 등급일수록 그럴듯하게").
    /// = 부위마다 입은 부품 번호 (<see cref="LookPartLibrary"/>) + 색 자리마다 색.
    /// 차례: 병종 · 장수 기본 차림 (lookParts.json troops · generals) → 입은 갑옷 세트 (cosmetics.json items[].look) → 말 색 → 개발용 고르기 (<see cref="DevLooks"/>).
    /// 세트 look 값: body 몸 · legs 다리 · trim 장식 · plume 깃 · cape 망토 · horse 말 = 색 이름, hat · shield = "부품:색" (옛 모양 이름 kasa · helmet · round … 도 받음),
    /// armor · back = 부품 번호. 세트의 투구 · 방패 · 갑옷은 병종 것보다 등급이 높을 때만 바꾼다 (방패병의 긴 방패 · 근위병 투구를 지킴).
    /// 색이 없는 자리는 편 색 (몸 · 투구) · 편의 어두운 색 (장식 · 다리 · 망토) — 아무것도 안 입으면 편 색으로 갈림. 겉모습만 — 전투 계산과 무관.
    /// </summary>
    public sealed class SoldierLook
    {
        /// <summary>부위 → 부품 번호 (없으면 맨몸 · 맨손).</summary>
        public readonly Dictionary<string, string> Wear = new Dictionary<string, string>();
        /// <summary>색 자리 (main · trim · cape · plume · hat · shield · legs · horse) → 색.</summary>
        public readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>();
        /// <summary>말을 탐 (기병 계열 · 장수).</summary>
        public bool Mounted;
        /// <summary>사람 병종 (보병 · 창병 · 궁병 · 기병 · 정찰병 · 공병 계열). 아니면 (투석기 · 배) 부품을 쓰지 않음.</summary>
        public bool Person;
        /// <summary>장수 (크게, 장수 기본 말 색).</summary>
        public bool General;
        /// <summary>총대장 (군기를 더 붙임).</summary>
        public bool Commander;
        /// <summary>부품 목록 (조립할 때).</summary>
        public LookPartLibrary Library;

        /// <summary>색을 고를 수 있는 자리 (꾸미기 탭 차례).</summary>
        public static readonly string[] ColorSlots = { "main", "trim", "hat", "cape", "shield", "plume", "legs", "horse" };

        /// <summary>같은 차림 하나 더 (꾸미기 탭이 부품 하나만 바꿔 그려 볼 때).</summary>
        public SoldierLook Clone()
        {
            var o = new SoldierLook { Mounted = Mounted, General = General, Commander = Commander, Person = Person, Library = Library };
            foreach (var kv in Wear) o.Wear[kv.Key] = kv.Value;
            foreach (var kv in Colors) o.Colors[kv.Key] = kv.Value;
            return o;
        }

        public string Of(string slot) => Wear.TryGetValue(slot, out var id) ? id : null;

        public Color? ColorAt(string slot) => Colors.TryGetValue(slot, out var c) ? c : (Color?)null;
        public Color? Legs => ColorAt("legs");
        public Color? Horse => ColorAt("horse");

        /// <summary>모양 열쇠 (메시 캐시): 부위마다 부품 + 탐 · 장수 · 총대장.</summary>
        public string ShapeKey
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var s in LookPartLibrary.Slots) sb.Append(Of(s) ?? "-").Append('|');
                sb.Append(Mounted ? 'm' : '-').Append(General ? 'g' : '-').Append(Commander ? 'c' : '-');
                return sb.ToString();
            }
        }

        /// <summary>모양 + 색 열쇠 (재질 묶음 캐시).</summary>
        public string Key
        {
            get
            {
                var sb = new StringBuilder(ShapeKey);
                foreach (var s in ColorSlots) sb.Append('|').Append(Colors.TryGetValue(s, out var c) ? ColorUtility.ToHtmlStringRGB(c) : "-");
                return sb.ToString();
            }
        }

        /// <summary>부품 목록이 있어 부품으로 조립할 수 있음 (없으면 예전 모양).</summary>
        public bool UsesParts => Library != null && !Library.Empty;

        /// <summary>
        /// 병종 겉모습: 병종 기본 차림 (없으면 계열 — 보병 · 창병 … — 의 차림) → 갑옷 세트 → 말 색.
        /// </summary>
        public static SoldierLook Resolve(GameContext ctx, string troopId, string armorId, string horseId)
        {
            var defs = ctx?.Defs;
            var look = new SoldierLook { Library = LookPartLibrary.Of(ctx?.Data) };
            string line = defs != null && troopId != null ? CosmeticService.LineOf(defs, troopId) : null;
            look.Mounted = line == "cavalry" || troopId == "cavalry";
            look.Person = line != null || (troopId != null && look.Library.Troops.ContainsKey(troopId));
            var lib = look.Library;
            if (troopId != null && lib.Troops.TryGetValue(troopId, out var o)) look.ApplyOutfit(o);
            else if (line != null && lib.Troops.TryGetValue(line, out var lo)) look.ApplyOutfit(lo);
            look.ApplySet(CosmeticLooks.Of(defs, armorId));
            var horse = CosmeticLooks.Of(defs, horseId);
            if (horse != null && horse.TryGetValue("horse", out var h) && CosmeticLooks.TryNamed(h, out var hc)) look.Colors["horse"] = hc;
            return look;
        }

        /// <summary>장수 겉모습: 성향(warrior · standard · strategist)의 기본 차림, 늘 말을 탐.</summary>
        public static SoldierLook ForGeneral(GameContext ctx, string preset, bool commander)
        {
            var look = new SoldierLook { Library = LookPartLibrary.Of(ctx?.Data), Mounted = true, General = true, Commander = commander, Person = true };
            var lib = look.Library;
            if (preset != null && lib.Generals.TryGetValue(preset, out var o)) look.ApplyOutfit(o);
            else if (lib.Generals.TryGetValue("standard", out var so)) look.ApplyOutfit(so);
            return look;
        }

        /// <summary>차림 한 벌 (부위 → 번호, loadout = 무기 묶음 먼저 펼침).</summary>
        public void ApplyOutfit(IReadOnlyDictionary<string, string> outfit)
        {
            if (outfit == null) return;
            if (outfit.TryGetValue("loadout", out var lid)) ApplyLoadout(lid);
            foreach (var s in LookPartLibrary.Slots)
                if (outfit.TryGetValue(s, out var id)) Set(s, id);
        }

        /// <summary>무기 묶음: 오른손 · 왼손을 묶음대로 (없는 손은 비움), 등은 묶음에 있을 때만.</summary>
        public void ApplyLoadout(string loadoutId)
        {
            if (Library == null || loadoutId == null || !Library.Loadouts.TryGetValue(loadoutId, out var l)) return;
            Set(LookPartLibrary.SlotRight, l.Right);
            Set(LookPartLibrary.SlotLeft, l.Left);
            if (l.Back != null) Set(LookPartLibrary.SlotBack, l.Back);
        }

        /// <summary>부위에 부품 (null · "" · "-" = 비움). 부위가 맞지 않는 번호는 무시.</summary>
        public void Set(string slot, string id)
        {
            if (string.IsNullOrEmpty(id) || id == "-") { Wear.Remove(slot); return; }
            if (LookPartLibrary.SlotOf(id) != slot) return;
            if (Library != null && !Library.Empty && Library.Find(id) == null) return; // 지운 번호 (2026-11-24 사용자 "번호로 지움")
            Wear[slot] = id;
        }

        /// <summary>갑옷 세트의 look (위 설명).</summary>
        public void ApplySet(IReadOnlyDictionary<string, string> set)
        {
            if (set == null) return;
            foreach (var kv in set)
            {
                string v = kv.Value;
                if (string.IsNullOrEmpty(v)) continue;
                int colon = v.IndexOf(':');
                string first = colon >= 0 ? v.Substring(0, colon) : v;
                string color = colon >= 0 ? v.Substring(colon + 1) : v;
                bool hasColor = CosmeticLooks.TryNamed(color, out var c);
                switch (kv.Key)
                {
                    case "body": if (hasColor) Colors["main"] = c; break;
                    case "legs": case "trim": case "plume": case "cape": if (hasColor) Colors[kv.Key] = c; break;
                    case "helmet": if (hasColor) Colors["hat"] = c; break;
                    case "hat":
                        Upgrade(LookPartLibrary.SlotHat, Library?.LegacyHat.TryGetValue(first, out var lh) == true ? lh : first);
                        if (hasColor && colon >= 0 || hasColor && LookPartLibrary.SlotOf(first) == null && !(Library?.LegacyHat.ContainsKey(first) ?? false)) Colors["hat"] = c;
                        break;
                    case "shield":
                        // 방패를 든 병종만 (궁병에게 방패를 쥐여 주지 않음)
                        string left = Of(LookPartLibrary.SlotLeft);
                        if (left != null && left[0] == 'D') Upgrade(LookPartLibrary.SlotLeft, Library?.LegacyShield.TryGetValue(first, out var ls) == true ? ls : first);
                        if (hasColor && colon >= 0 || hasColor && LookPartLibrary.SlotOf(first) == null && !(Library?.LegacyShield.ContainsKey(first) ?? false)) Colors["shield"] = c;
                        break;
                    case "armor": Upgrade(LookPartLibrary.SlotArmor, first); break;
                    case "back": if (Of(LookPartLibrary.SlotBack) == null) Set(LookPartLibrary.SlotBack, first); break; // 화살통은 지킴
                }
            }
        }

        /// <summary>등급이 지금 것보다 높을 때만 바꿈 (같으면 병종 것).</summary>
        private void Upgrade(string slot, string id)
        {
            if (Library == null || LookPartLibrary.SlotOf(id) != slot || Library.Find(id) == null) return;
            if (Library.RankOf(id) > Library.RankOf(Of(slot))) Set(slot, id);
        }

        public static readonly Color WoodColor = new Color(0.5f, 0.34f, 0.18f);
        public static readonly Color GoldColor = new Color(0.9f, 0.72f, 0.25f);
        public static readonly Color WhiteColor = new Color(0.93f, 0.91f, 0.86f);
        public static readonly Color PlumeColor = new Color(0.75f, 0.15f, 0.12f);
        public static readonly Color HorseColor = new Color(0.46f, 0.31f, 0.19f);
        public static readonly Color GeneralHorseColor = new Color(0.9f, 0.88f, 0.84f);

        /// <summary>색 자리의 색 (<see cref="LookMeshes.ColorNames"/>): 고른 색이 없으면 편 색 · 편의 어두운 색 · 정한 색.</summary>
        public Color ColorFor(string slot, Color team, Color dark)
        {
            switch (slot)
            {
                case "main": return ColorAt("main") ?? team;
                case "hat": return ColorAt("hat") ?? ColorAt("main") ?? team;
                case "trim": return ColorAt("trim") ?? dark;
                case "cape": return ColorAt("cape") ?? dark;
                case "legs": return ColorAt("legs") ?? dark;
                case "shield": return ColorAt("shield") ?? WoodColor;
                case "plume": return ColorAt("plume") ?? PlumeColor;
                case "horse": return ColorAt("horse") ?? (General ? GeneralHorseColor : HorseColor);
                case "mane": return (ColorAt("horse") ?? (General ? GeneralHorseColor : HorseColor)) * 0.45f + new Color(0f, 0f, 0f, 0.55f);
                case "team": return team;
                case "skin": return SoldierMeshes.SkinColor;
                case "metal": return SoldierMeshes.MetalColor;
                case "dark": return SoldierMeshes.DarkColor;
                case "wood": return WoodColor;
                case "gold": return GoldColor;
                case "white": return WhiteColor;
                default: return team;
            }
        }
    }
}
