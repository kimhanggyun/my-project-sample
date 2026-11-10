using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Game.Tests
{
    /// <summary>화면 스타일 데이터 (Ui/styles.json, main-screen.md 2.2): 기본 스타일이 있고, 색은 #RRGGBB(AA), 이름 키는 문자열에 있다.</summary>
    public class UiStyleDataTests
    {
        [Fact]
        public void StylesAreValid()
        {
            var root = JObject.Parse(Campaign.Kit.Source.ReadText("Ui/styles.json"));
            var styles = (JObject)root["styles"];
            Assert.NotNull(styles[root.Value<string>("default")]);
            foreach (var p in styles.Properties())
            {
                var s = (JObject)p.Value;
                Assert.True(Campaign.Kit.D.Strings.ContainsKey(s.Value<string>("nameKey")), p.Name + ": nameKey");
                Assert.InRange(s.Value<double>("cornerRadius"), 0, 64);
                Assert.InRange(s.Value<double>("fontScale"), 0.5, 2.0);
                foreach (var c in ((JObject)s["colors"]).Properties())
                    Assert.Matches(new Regex("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$"), c.Value.Value<string>());
            }
        }
    }
}
