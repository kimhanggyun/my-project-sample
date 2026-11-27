using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Game.Tests
{
    /// <summary>
    /// 부품 꾸미기 목록 (Ui/uiKit.json, docs/design/ui-kit.md) 과 styles.json parts가 맞는지:
    /// 역할의 종류 · 분류가 있고, parts 값의 변형 · 조절값 이름 · 범위 · 색 이름 · 그림 id · 선택지가 목록 안에 있다.
    /// </summary>
    public class UiKitDataTests
    {
        private static readonly Regex Hex = new Regex("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$");

        private static JObject Kit => JObject.Parse(Campaign.Kit.Source.ReadText("Ui/uiKit.json"));
        private static JObject Styles => JObject.Parse(Campaign.Kit.Source.ReadText("Ui/styles.json"));

        [Fact]
        public void CatalogIsConsistent()
        {
            var kit = Kit;
            var kinds = (JObject)kit["kinds"];
            var cats = new HashSet<string>(((JArray)kit["categories"]).Select(c => (string)c["id"]));
            var arts = (JObject)kit["arts"];
            foreach (var r in ((JObject)kit["roles"]).Properties())
            {
                Assert.True(kinds[(string)r.Value["kind"]] != null, r.Name + ": kind");
                Assert.Contains((string)r.Value["category"], cats);
            }
            foreach (var k in kinds.Properties())
            {
                Assert.NotEmpty((JArray)k.Value["variants"]);
                foreach (var f in ((JObject)k.Value["fields"]).Properties())
                {
                    string type = (string)f.Value["type"];
                    Assert.Contains(type, new[] { "color", "num", "choice", "art" });
                    if (type == "num") Assert.True((double)f.Value["min"] < (double)f.Value["max"] && (double)f.Value["step"] > 0, k.Name + "." + f.Name);
                    if (type == "choice") Assert.NotEmpty((JArray)f.Value["options"]);
                    if (type == "art") Assert.Contains(arts.Properties(), a => (string)a.Value["group"] == (string)f.Value["group"]);
                }
            }
            foreach (var t in ((JObject)kit["tabIcons"]).Properties())
                Assert.True(arts[(string)t.Value] != null && (string)arts[(string)t.Value]["group"] == "icon", "tabIcons." + t.Name);
        }

        [Fact]
        public void StylePartsFollowCatalog()
        {
            var kit = Kit;
            var styles = Styles;
            var themeColors = new HashSet<string>(((JArray)kit["themeColors"]).Select(c => (string)c));
            foreach (var st in ((JObject)styles["styles"]).Properties())
                foreach (var c in ((JObject)st.Value["colors"]).Properties())
                    Assert.True(themeColors.Contains(c.Name), st.Name + " 색 " + c.Name + " 가 uiKit themeColors에 없음");
            var roles = (JObject)kit["roles"];
            var kinds = (JObject)kit["kinds"];
            var arts = (JObject)kit["arts"];
            var bad = new List<string>();
            // 공통 parts + 스타일마다 parts (styles.<id>.parts)
            var all = new List<JProperty>(((JObject)styles["parts"]).Properties());
            foreach (var st in ((JObject)styles["styles"]).Properties()) if (st.Value["parts"] is JObject sp) all.AddRange(sp.Properties());
            foreach (var p in all)
            {
                if (roles[p.Name] == null) continue; // 페이지 덮어쓰기용 스타일 이름 등
                var kind = (JObject)kinds[(string)roles[p.Name]["kind"]];
                var fields = (JObject)kind["fields"];
                foreach (var v in ((JObject)p.Value).Properties())
                {
                    string where = p.Name + "." + v.Name;
                    if (v.Name == "variant")
                    {
                        if (!((JArray)kind["variants"]).Any(x => (string)x == (string)v.Value)) bad.Add(where + " = " + v.Value);
                        continue;
                    }
                    var f = fields[v.Name];
                    if (f == null) { bad.Add(where + ": 목록에 없는 값"); continue; }
                    switch ((string)f["type"])
                    {
                        case "num":
                            double d = (double)v.Value;
                            if (d < (double)f["min"] || d > (double)f["max"]) bad.Add(where + " 범위 밖: " + d);
                            break;
                        case "color":
                            string c = (string)v.Value;
                            if (!themeColors.Contains(c) && !Hex.IsMatch(c)) bad.Add(where + " 색: " + c);
                            break;
                        case "choice":
                            if (!((JArray)f["options"]).Any(x => (string)x == (string)v.Value)) bad.Add(where + " 선택지 밖: " + v.Value);
                            break;
                        case "art":
                            var a = arts[(string)v.Value];
                            if (a == null || (string)a["group"] != (string)f["group"]) bad.Add(where + " 그림: " + v.Value);
                            break;
                    }
                }
            }
            Assert.Empty(bad);
        }
    }
}
