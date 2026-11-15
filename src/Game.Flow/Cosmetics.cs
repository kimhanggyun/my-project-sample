using System.Collections.Generic;
using Game.Campaign;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Economy;
using Newtonsoft.Json.Linq;

namespace Game.Flow
{
    /// <summary>치장 종류 (cosmetics.md 2장).</summary>
    public static class CosmeticKinds
    {
        /// <summary>갑옷 한 벌: 투구 · 덧옷 · 망토 · 방패 · 무기 색 (팀 색 몸통은 그대로).</summary>
        public const string Armor = "armor";
        /// <summary>말 색 (말 탄 계열만).</summary>
        public const string Horse = "horse";
    }

    /// <summary>치장 하나 (data/Rules/cosmetics.json items). 그림 값(Look)은 규칙이 읽지 않는다.</summary>
    public sealed class CosmeticDef
    {
        public string Id { get; internal set; }
        public string Kind { get; internal set; }
        public string NameKey { get; internal set; }
        /// <summary>입을 수 있는 계열 (null = 그 종류를 입는 모든 계열).</summary>
        public IReadOnlyList<string> Lines { get; internal set; }
        /// <summary>그림에 넘기는 값: 요소(helmet · trim · cape · shield · weapon · mount · horse) → "모양:색" 또는 "색".</summary>
        public IReadOnlyDictionary<string, string> Look { get; internal set; } = new Dictionary<string, string>();
        /// <summary>상점 가격 (하나만): 엽전 · 금괴 · 결제(원). 없으면 상점에 없음.</summary>
        public long? PriceGold { get; internal set; }
        public long? PriceGem { get; internal set; }
        public long? PriceKRW { get; internal set; }
        /// <summary>플레이로 얻는 길 (story:stepXX · secret:id …).</summary>
        public IReadOnlyList<string> PlayPaths { get; internal set; } = new List<string>();

        /// <summary>플레이로 얻을 수 있다: 엽전으로 사거나(엽전은 보상으로 번다) 플레이 경로가 있다 (cosmetics.md 9장 1).</summary>
        public bool PlayObtainable => PriceGold != null || PlayPaths.Count > 0;
    }

    /// <summary>
    /// 치장 (cosmetics.md, verification R-108): 병종 계열마다 갑옷 한 벌 · 말 색. 겉모습만 — 전투 계산 · 상태 해시에 들어가지 않는다.
    /// 사기는 엽전 · 금괴 (결제 상품은 상점 결제 흐름에서, 1단계 첫 목록은 엽전만), 하나 사면 입을 수 있는 모든 계열에 입힌다.
    /// </summary>
    public sealed class CosmeticService
    {
        public const string File = "Rules/cosmetics.json";
        /// <summary>사람 병사 계열 (1단계 병종 + 공병). 투석기 · 배는 치장 없음.</summary>
        public static readonly IReadOnlyList<string> PersonLines = new[] { "infantry", "spearman", "archer", "cavalry", "scout", "engineer" };
        /// <summary>말 탄 계열.</summary>
        public static readonly IReadOnlyList<string> MountedLines = new[] { "cavalry" };

        private readonly DefinitionSet _defs;
        private readonly Dictionary<string, CosmeticDef> _items = new Dictionary<string, CosmeticDef>(System.StringComparer.Ordinal);
        private readonly List<CosmeticDef> _order = new List<CosmeticDef>();

        public IReadOnlyList<CosmeticDef> Items => _order;

        public CosmeticService(DefinitionSet defs)
        {
            _defs = defs;
            var o = defs?.CosmeticsDoc;
            if (o == null || !(o["items"] is JArray arr)) return;
            foreach (var t in arr)
            {
                if (!(t is JObject j)) continue;
                var d = new CosmeticDef
                {
                    Id = (string)j["id"],
                    Kind = (string)j["kind"],
                    NameKey = (string)j["nameKey"] ?? "cos." + (string)j["id"],
                    Lines = j["lines"] is JArray ? JsonUtil.StringList(j, "lines") : null,
                    PlayPaths = j["obtain"]?["playPaths"] is JArray ? JsonUtil.StringList((JObject)j["obtain"], "playPaths") : new List<string>(),
                };
                var look = new Dictionary<string, string>();
                if (j["look"] is JObject lk) foreach (var kv in lk) if (kv.Value?.Type == JTokenType.String) look[kv.Key] = (string)kv.Value;
                d.Look = look;
                if (j["obtain"]?["shop"] is JObject shop)
                {
                    if (shop["gold"] != null) d.PriceGold = (long)shop["gold"];
                    if (shop["gem"] != null) d.PriceGem = (long)shop["gem"];
                    if (shop["priceKRW"] != null) d.PriceKRW = (long)shop["priceKRW"];
                }
                if (d.Id == null || _items.ContainsKey(d.Id)) continue;
                _items[d.Id] = d;
                _order.Add(d);
            }
        }

        public CosmeticDef Find(string id) => id != null && _items.TryGetValue(id, out var d) ? d : null;

        /// <summary>
        /// 병종의 치장 계열: 부모를 따라 올라간 1단계 병종 (공병 태그면 engineer). 사람 병사 계열이 아니면 null (투석기 · 배).
        /// 승급한 병종(2·3단계)도 계열의 치장을 그대로 입는다.
        /// </summary>
        public static string LineOf(DefinitionSet defs, string troopType)
        {
            string t = troopType;
            for (int guard = 0; guard < 8 && t != null; guard++)
            {
                if (!defs.TroopTypes.TryGetValue(t, out var d)) return null;
                if (d.HasTag("engineer")) return "engineer";
                if (d.Parent == null) break;
                t = d.Parent;
            }
            return t != null && Contains(PersonLines, t) ? t : null;
        }

        /// <summary>그 계열이 입을 수 있나: 갑옷 = 사람 계열, 말 = 말 탄 계열, 그리고 lines 안.</summary>
        public static bool CanWear(CosmeticDef d, string line)
        {
            if (d == null || line == null) return false;
            var lines = d.Kind == CosmeticKinds.Horse ? MountedLines : d.Kind == CosmeticKinds.Armor ? PersonLines : null;
            if (lines == null || !Contains(lines, line)) return false;
            return d.Lines == null || Contains(d.Lines, line);
        }

        public static bool Owns(CampaignState s, string id) => s.OwnedCosmetics.Contains(id);

        /// <summary>엽전 · 금괴로 산다. 이미 가졌거나, 그 재화 가격이 없거나(결제 상품), 모자라면 false.</summary>
        public bool Buy(CampaignState s, string id)
        {
            var d = Find(id);
            if (d == null || Owns(s, id)) return false;
            var w = new Wallet(s.Wallet);
            if (d.PriceGold != null) { if (!w.TrySpend(ResourceIds.Gold, d.PriceGold.Value)) return false; }
            else if (d.PriceGem != null) { if (!w.TrySpend(ResourceIds.Gem, d.PriceGem.Value)) return false; }
            else return false;
            s.OwnedCosmetics.Add(id);
            return true;
        }

        /// <summary>얻기 (보상 · 결제): 이미 가졌으면 false (중복 보상 바꾸기는 나중).</summary>
        public bool Grant(CampaignState s, string id)
        {
            if (Find(id) == null || Owns(s, id)) return false;
            s.OwnedCosmetics.Add(id);
            return true;
        }

        /// <summary>
        /// 입히기: id가 그 종류의 치장이면 그 계열에 입힌다 (가진 것 · 입을 수 있는 것만). 벗기기 = kind + id null.
        /// 이미 그 모습이면 false (바뀐 것이 없음).
        /// </summary>
        public bool Equip(CampaignState s, string line, string kind, string id)
        {
            if (line == null || (kind != CosmeticKinds.Armor && kind != CosmeticKinds.Horse)) return false;
            var map = kind == CosmeticKinds.Horse ? s.EquippedHorse : s.EquippedArmor;
            if (id == null)
            {
                if (!map.ContainsKey(line)) return false;
                map.Remove(line);
                return true;
            }
            var d = Find(id);
            if (d == null || d.Kind != kind || !Owns(s, id) || !CanWear(d, line)) return false;
            if (map.TryGetValue(line, out var now) && now == id) return false;
            map[line] = id;
            return true;
        }

        /// <summary>그 계열이 입은 것 (없으면 null = 기본).</summary>
        public static string Worn(CampaignState s, string line, string kind)
        {
            if (line == null) return null;
            var map = kind == CosmeticKinds.Horse ? s.EquippedHorse : s.EquippedArmor;
            return map.TryGetValue(line, out var id) ? id : null;
        }

        /// <summary>데이터 검사 (cosmetics.md 3장): 종류 · 계열 · 가격 하나 · 플레이로 얻는 공용 갑옷 하나 이상.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            bool playArmor = _order.Count == 0;
            foreach (var d in _order)
            {
                if (d.Kind != CosmeticKinds.Armor && d.Kind != CosmeticKinds.Horse) errors.Add($"치장 '{d.Id}': 종류가 armor · horse가 아님");
                var allowed = d.Kind == CosmeticKinds.Horse ? MountedLines : PersonLines;
                if (d.Lines != null) foreach (var l in d.Lines) if (!Contains(allowed, l)) errors.Add($"치장 '{d.Id}': 입을 수 없는 계열 '{l}'");
                int prices = (d.PriceGold != null ? 1 : 0) + (d.PriceGem != null ? 1 : 0) + (d.PriceKRW != null ? 1 : 0);
                if (prices > 1) errors.Add($"치장 '{d.Id}': 가격은 하나만");
                if ((d.PriceGold ?? 1) <= 0 || (d.PriceGem ?? 1) <= 0 || (d.PriceKRW ?? 1) <= 0) errors.Add($"치장 '{d.Id}': 가격은 0보다 커야 함");
                if (prices == 0 && d.PlayPaths.Count == 0) errors.Add($"치장 '{d.Id}': 얻는 길이 없음");
                if (d.Kind == CosmeticKinds.Armor && d.Lines == null && d.PlayObtainable) playArmor = true;
            }
            if (!playArmor) errors.Add("치장: 플레이로 얻는(엽전 · 보상) 공용 갑옷이 하나 이상 있어야 함");
            return errors;
        }

        private static bool Contains(IReadOnlyList<string> list, string x)
        {
            foreach (var v in list) if (v == x) return true;
            return false;
        }
    }
}
