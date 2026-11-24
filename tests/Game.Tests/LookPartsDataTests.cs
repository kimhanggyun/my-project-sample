using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Game.Tests
{
    /// <summary>
    /// 겉모습 부품 (R-150, data/Ui/lookParts.json) 검사 — 원격이 부품을 데이터로만 늘려도 여기서 걸러진다 (만드는 법: docs/design/content/look-parts.md).
    /// 겉모습만이라 수치 규칙은 없고, 모양 · 자리 · 색 · 크기 · 번호 · 참조만 본다.
    /// </summary>
    public class LookPartsDataTests
    {
        private static readonly string[] Shapes = { "box", "sphere", "cylinder", "capsule", "cone", "dome" };
        private static readonly string[] Colors = { "main", "skin", "metal", "dark", "trim", "horse", "wood", "gold", "plume", "cape", "hat", "shield", "legs", "mane", "team", "white" };
        private static readonly string[] Grades = { "basic", "coin", "gem", "cash", "general" };
        private static readonly string[] HorseAnchors = { "saddle", "horseHead", "horseNeck", "horseBody", "horseRump", "horseChest" };
        /// <summary>번호 첫 글자 → 붙을 수 있는 자리.</summary>
        private static readonly Dictionary<char, string[]> AnchorsFor = new Dictionary<char, string[]>
        {
            ['H'] = new[] { "head" },
            ['A'] = new[] { "chest", "waist", "back", "feet" },
            ['C'] = new[] { "back", "chest" },
            ['S'] = new[] { "rightHand" },
            ['P'] = new[] { "rightHand" },
            ['B'] = new[] { "leftHand" },
            ['D'] = new[] { "leftHand" },
            ['M'] = HorseAnchors,
        };
        private const int MaxPieces = 24;

        private static JObject Doc() => JObject.Parse(File.ReadAllText(Path.Combine(TestData.DataRoot, "Ui", "lookParts.json")));

        private static Dictionary<string, JObject> PartsById(JObject doc) => doc["parts"].Cast<JObject>().ToDictionary(p => (string)p["id"]);

        [Fact(DisplayName = "겉모습 부품 (R-150): 번호는 부위 글자 + 두세 자리 숫자로 겹치지 않고, 이름 · 등급이 있으며, 도형 1~24개는 아는 모양 · 부위에 맞는 자리 · 아는 색 · 알맞은 크기")]
        public void Parts()
        {
            var doc = Doc();
            var ids = new HashSet<string>();
            foreach (JObject p in doc["parts"])
            {
                string id = (string)p["id"];
                Assert.True(id != null && Regex.IsMatch(id, "^[HACSPBDM][0-9]{2,3}$"), "부품 번호 모양: " + id);
                Assert.True(ids.Add(id), "겹치는 부품 번호: " + id);
                Assert.False(string.IsNullOrWhiteSpace((string)p["name"]), id + ": 이름이 없음");
                Assert.Contains((string)p["grade"], Grades);
                var pieces = p["pieces"] as JArray;
                Assert.True(pieces != null && pieces.Count >= 1 && pieces.Count <= MaxPieces, id + ": 도형은 1~" + MaxPieces + "개");
                foreach (JObject q in pieces)
                {
                    string where = id + " 도형 " + q.ToString(Newtonsoft.Json.Formatting.None);
                    Assert.True(Shapes.Contains((string)q["shape"]), where + ": 모양은 " + string.Join(" · ", Shapes));
                    Assert.True(AnchorsFor[id[0]].Contains((string)q["at"]), where + ": " + id[0] + " 부품이 붙는 자리는 " + string.Join(" · ", AnchorsFor[id[0]]));
                    Assert.True(Colors.Contains((string)q["color"]), where + ": 색 자리는 " + string.Join(" · ", Colors));
                    var pos = Vec(q["pos"], where + " pos");
                    var size = Vec(q["size"], where + " size");
                    foreach (var v in pos) Assert.True(System.Math.Abs(v) <= 0.6, where + ": pos는 −0.6~0.6");
                    foreach (var v in size) Assert.True(v > 0 && v <= 0.8, where + ": size는 0보다 크고 0.8 이하");
                    if (q["rot"] != null) foreach (var v in Vec(q["rot"], where + " rot")) Assert.True(System.Math.Abs(v) <= 360, where + ": rot는 −360~360도");
                    if (q["mirror"] != null) Assert.Equal(JTokenType.Boolean, q["mirror"].Type);
                }
            }
            Assert.NotEmpty(ids);
        }

        private static double[] Vec(JToken t, string where)
        {
            var a = t as JArray;
            Assert.True(a != null && a.Count == 3 && a.All(x => x.Type == JTokenType.Float || x.Type == JTokenType.Integer), where + ": 숫자 세 개 [x, y, z]");
            return a.Select(x => (double)x).ToArray();
        }

        [Fact(DisplayName = "겉모습 부품 (R-150): 무기 묶음 L은 오른손 = S · P, 왼손 = B · D, 등 = C 번호이고, 병종 · 장수 기본 차림은 있는 병종 · 성향과 있는 부품 · 묶음을 가리킴")]
        public void LoadoutsAndOutfits()
        {
            var doc = Doc();
            var parts = PartsById(doc);
            var loads = new HashSet<string>();
            foreach (JObject l in doc["loadouts"])
            {
                string id = (string)l["id"];
                Assert.True(id != null && Regex.IsMatch(id, "^L[0-9]{2,3}$"), "묶음 번호 모양: " + id);
                Assert.True(loads.Add(id), "겹치는 묶음 번호: " + id);
                Assert.False(string.IsNullOrWhiteSpace((string)l["name"]), id + ": 이름이 없음");
                Ref(parts, (string)l["right"], "SP", id + ".right");
                Ref(parts, (string)l["left"], "BD", id + ".left");
                Ref(parts, (string)l["back"], "C", id + ".back");
                Assert.True(l["right"] != null || l["left"] != null, id + ": 손에 든 것이 없음");
            }
            var troops = JObject.Parse(File.ReadAllText(Path.Combine(TestData.DataRoot, "Rules", "troopTypes.json")));
            var troopIds = new HashSet<string>(((JArray)troops["troopTypes"]).Select(t => (string)t["id"]));
            var crowd = JObject.Parse(File.ReadAllText(Path.Combine(TestData.DataRoot, "Rules", "crowd.json")));
            var presets = new HashSet<string>(((JObject)crowd["generalPresets"]).Properties().Select(p => p.Name));
            foreach (var t in ((JObject)doc["troops"]).Properties())
            {
                Assert.True(troopIds.Contains(t.Name), "lookParts troops: 모르는 병종 " + t.Name);
                Outfit(parts, loads, (JObject)t.Value, "troops." + t.Name);
            }
            foreach (var g in ((JObject)doc["generals"]).Properties())
            {
                Assert.True(presets.Contains(g.Name), "lookParts generals: 모르는 장수 성향 " + g.Name);
                Outfit(parts, loads, (JObject)g.Value, "generals." + g.Name);
            }
            foreach (var p in presets) Assert.True(doc["generals"][p] != null, "장수 성향 " + p + "의 기본 차림이 없음");
            Ref(parts, (string)doc["commanderExtra"], "C", "commanderExtra");
        }

        private static void Outfit(Dictionary<string, JObject> parts, HashSet<string> loads, JObject o, string where)
        {
            Ref(parts, (string)o["hat"], "H", where + ".hat");
            Ref(parts, (string)o["armor"], "A", where + ".armor");
            Ref(parts, (string)o["back"], "C", where + ".back");
            Ref(parts, (string)o["right"], "SP", where + ".right");
            Ref(parts, (string)o["left"], "BD", where + ".left");
            Ref(parts, (string)o["tack"], "M", where + ".tack");
            if (o["loadout"] != null) Assert.True(loads.Contains((string)o["loadout"]), where + ".loadout: 없는 묶음 " + o["loadout"]);
        }

        private static void Ref(Dictionary<string, JObject> parts, string id, string letters, string where)
        {
            if (id == null) return;
            Assert.True(letters.Contains(id[0]), where + ": " + id + "는 " + letters + " 부품이어야 함");
            Assert.True(parts.ContainsKey(id), where + ": 없는 부품 " + id + " (지운 번호면 가리키는 곳도 고칠 것)");
        }

        [Fact(DisplayName = "겉모습 부품 (R-150): 갑옷 세트 look의 투구 · 방패 · 갑옷 · 등 부품 번호는 lookParts.json에 있고 부위가 맞음")]
        public void CosmeticSetsPointToParts()
        {
            var parts = PartsById(Doc());
            var cos = JObject.Parse(File.ReadAllText(Path.Combine(TestData.DataRoot, "Rules", "cosmetics.json")));
            foreach (JObject item in cos["items"])
            {
                if (!(item["look"] is JObject look)) continue;
                string id = (string)item["id"];
                foreach (var (key, letters) in new[] { ("hat", "H"), ("shield", "D"), ("armor", "A"), ("back", "C") })
                {
                    string v = (string)look[key];
                    if (v == null) continue;
                    string first = v.Split(':')[0];
                    if (!Regex.IsMatch(first, "^[A-Z][0-9]{2,3}$")) continue; // 옛 모양 이름 · 색 이름
                    Ref(parts, first, letters, id + ".look." + key);
                }
            }
        }
    }
}
