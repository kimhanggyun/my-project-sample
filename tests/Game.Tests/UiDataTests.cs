using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Game.Tests
{
    /// <summary>화면 데이터 (data/Ui) 검사.</summary>
    public class UiDataTests
    {
        [Fact(DisplayName = "메인 탭 목록: id가 겹치지 않고, 상태는 active/tobe/hidden, 이름 문구가 있으며, 첫 번째로 보이는 탭은 사용 중")]
        public void MainTabs()
        {
            var doc = JObject.Parse(File.ReadAllText(Path.Combine(TestData.DataRoot, "Ui", "mainTabs.json")));
            var strings = JObject.Parse(File.ReadAllText(Path.Combine(TestData.DataRoot, "Themes", TestData.Theme, "strings.ko.json")));
            var tabs = doc["tabs"].Cast<JObject>().ToList();
            Assert.NotEmpty(tabs);
            var ids = new HashSet<string>();
            foreach (var t in tabs)
            {
                string id = t.Value<string>("id");
                Assert.True(ids.Add(id), "겹치는 탭 id: " + id);
                Assert.Contains(t.Value<string>("status"), new[] { "active", "tobe", "hidden" });
                Assert.NotNull(strings[t.Value<string>("nameKey")]);
            }
            Assert.Equal("active", tabs.First(t => t.Value<string>("status") != "hidden").Value<string>("status"));
        }
    }
}
