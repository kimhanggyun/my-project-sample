using Game.Campaign;
using Game.Data.Defs;
using Game.Flow;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>R-62 처음 안내: 자리마다 안 본 첫 안내, 본 기록은 행동(guide.seen)으로 세이브에, guide.reset으로 다시.</summary>
    public class GuideTests
    {
        [Fact]
        public void NextSeenAndReset()
        {
            var svc = new GuideService(Kit.D);
            var s = StartingKitService.CreateNewGame(Kit.D);
            var g = svc.Next(s, "screen.battle");
            Assert.NotNull(g);
            Assert.Equal("battle", g.Id);
            Assert.NotEmpty(g.Lines);
            Assert.Null(svc.Next(s, "screen.nowhere"));

            var runner = new PlayerActionRunner(Kit.D);
            Assert.True(runner.Apply(s, PlayerActionRunner.SeeGuide("battle")));
            Assert.Null(svc.Next(s, "screen.battle"));
            Assert.False(runner.Apply(s, PlayerActionRunner.SeeGuide("battle")));   // 이미 봄
            Assert.False(runner.Apply(s, PlayerActionRunner.SeeGuide("noSuch")));   // 없는 안내
            Assert.Contains("battle", SaveJson.Read(SaveJson.Write(s)).SeenGuides); // 세이브에 남음

            Assert.True(runner.Apply(s, PlayerActionRunner.ResetGuides()));
            Assert.NotNull(svc.Next(s, "screen.battle"));
            Assert.False(runner.Apply(s, PlayerActionRunner.ResetGuides()));        // 지울 것 없음
        }

        [Fact]
        public void EveryGuideLineHasString()
        {
            foreach (var g in Kit.D.Guides.Guides)
                foreach (var key in g.Lines) Assert.True(Kit.D.Strings.ContainsKey(key), g.Id + ": " + key);
        }

        [Fact]
        public void DuplicateIdRejected()
        {
            var o = JObject.Parse("{\"guides\":[{\"id\":\"a\",\"place\":\"x\",\"lines\":[\"k\"]},{\"id\":\"a\",\"place\":\"y\",\"lines\":[\"k\"]}]}");
            Assert.Throws<Game.Data.Json.DataException>(() => GuideRules.From(o));
            Assert.Throws<Game.Data.Json.DataException>(() => GuideRules.From(JObject.Parse("{\"guides\":[{\"id\":\"a\",\"place\":\"x\",\"lines\":[]}]}")));
        }
    }
}
