using System.Linq;
using Game.Campaign;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Economy;
using Game.Flow;
using Game.Foundation.Math;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>장수 모집 (generals.md 5장, verification R-51).</summary>
    public class GachaTests
    {
        private static CampaignState Rich(long gem = 1_000_000)
        {
            var s = StartingKitService.CreateNewGame(Kit.D);
            s.Wallet[ResourceIds.Gem] = gem;
            return s;
        }

        [Fact(DisplayName = "R-51 공개 확률: 장수마다 = 등급 확률 ÷ 그 등급 장수 수, 합 = 1")]
        public void Rates()
        {
            var rates = new GachaService(Kit.D).Rates("standard");
            long sum = rates.Aggregate(Fix.Zero, (a, r) => a + r.Rate).Raw;
            Assert.InRange(sum, Fix.One - rates.Count, Fix.One); // 장수별로 나누면 끝자리가 버려진다 (표시용)
            Assert.Equal(Fix.FromRaw(50), rates.Single(r => r.GeneralId == "general_h").Rate); // 전설 0.5% (한 명)
            Assert.Equal(Kit.D.Gacha.Banners["standard"].Pool.Count, rates.Count);
        }

        [Fact(DisplayName = "R-51 모집: 재화를 내고, 같은 시드면 같은 결과, 새 장수는 레벨 1로 들어오고 이미 있으면 조각")]
        public void DrawBasics()
        {
            var svc = new GachaService(Kit.D);
            var a = Rich(); var b = Rich();
            var ra = svc.Draw(a, "standard", 10, 42);
            var rb = svc.Draw(b, "standard", 10, 42);
            Assert.True(ra.Ok);
            Assert.Equal(ra.Pulls.Select(p => p.GeneralId), rb.Pulls.Select(p => p.GeneralId));
            Assert.Equal(1_000_000 - 900, new Wallet(a.Wallet).Get(ResourceIds.Gem));
            foreach (var p in ra.Pulls.Where(p => !p.Duplicate)) Assert.Equal(1, a.FindGeneral(p.GeneralId).Level);
            long shards = a.GeneralShards.Values.Sum();
            Assert.Equal(ra.Pulls.Count(p => p.Duplicate), shards);
            Assert.All(ra.Pulls.Where(p => p.GeneralId.StartsWith("general_") && "abc".Contains(p.GeneralId[8])), p => Assert.True(p.Duplicate)); // 시작 장수는 이미 있음

            var poor = Rich(50);
            var no = svc.Draw(poor, "standard", 1, 1);
            Assert.Equal((false, GachaService.NotEnough), (no.Ok, no.Reason));
            Assert.Equal(50, new Wallet(poor.Wallet).Get(ResourceIds.Gem));
            Assert.Equal(GachaService.BadCount, svc.Draw(Rich(), "standard", 3, 1).Reason);
            Assert.Equal(GachaService.NoBanner, svc.Draw(Rich(), "nope", 1, 1).Reason);
        }

        [Fact(DisplayName = "R-51 10회 보장: 10회마다 희귀 이상 1명 이상 (시드 200개)")]
        public void TenGuarantee()
        {
            var svc = new GachaService(Kit.D);
            int rareRank = Kit.D.Gacha.Grade("rare").Rank;
            for (ulong seed = 1; seed <= 200; seed++)
            {
                var r = svc.Draw(Rich(), "standard", 10, seed);
                Assert.Contains(r.Pulls, p => Kit.D.Gacha.Grade(p.Grade).Rank >= rareRank);
            }
        }

        [Fact(DisplayName = "R-51 천장: 전설이 99번 안 나오면 100번째는 전설, 나오면 다시 0부터")]
        public void Pity()
        {
            var svc = new GachaService(Kit.D);
            var s = Rich();
            s.GachaPity["standard"] = 99;
            var r = svc.Draw(s, "standard", 1, 7);
            Assert.Equal("legend", r.Pulls.Single().Grade);
            Assert.Equal(0, s.GachaPity["standard"]);
            s.GachaPity["standard"] = 10;
            var r2 = svc.Draw(s, "standard", 1, 8);
            Assert.Equal(r2.Pulls[0].Grade == "legend" ? 0 : 11, s.GachaPity["standard"]);
        }

        [Fact(DisplayName = "R-51 확률 분포: 1회 모집 20,000번이면 등급 비율이 데이터 확률 근처 (±1.5%p)")]
        public void Distribution()
        {
            var svc = new GachaService(Kit.D);
            var counts = Kit.D.Gacha.Grades.ToDictionary(g => g.Id, g => 0);
            var s = Rich(long.MaxValue / 2);
            const int n = 20000;
            for (int i = 0; i < n; i++)
            {
                s.GachaPity["standard"] = 0; // 천장은 따로 본다
                counts[svc.Draw(s, "standard", 1, (ulong)(i + 1) * 2654435761UL).Pulls[0].Grade]++;
            }
            foreach (var g in Kit.D.Gacha.Grades)
                Assert.InRange(counts[g.Id] * 10000L / n, g.Rate.Raw - 150, g.Rate.Raw + 150);
        }

        [Fact(DisplayName = "모집 데이터 검사: 확률 합 1, 확률 있는 등급마다 장수, 없는 장수·등급은 거절")]
        public void Validation()
        {
            var gens = Kit.D.Generals;
            JObject Doc(string grades, string pool) => JsonUtil.ParseObject("{\"grades\":[" + grades + "],\"banners\":[{\"id\":\"b\",\"nameKey\":\"k\",\"currency\":\"gem\",\"costOne\":1,\"costTen\":9,\"pool\":[" + pool + "]}]}");
            GachaRules.From(Doc("{\"id\":\"common\",\"rate\":1}", "\"enemy_general_01\""), gens);
            Assert.Throws<DataException>(() => GachaRules.From(Doc("{\"id\":\"common\",\"rate\":0.9}", "\"enemy_general_01\""), gens));
            Assert.Throws<DataException>(() => GachaRules.From(Doc("{\"id\":\"common\",\"rate\":0.5},{\"id\":\"legend\",\"rate\":0.5}", "\"enemy_general_01\""), gens));
            Assert.Throws<DataException>(() => GachaRules.From(Doc("{\"id\":\"common\",\"rate\":1}", "\"nobody\""), gens));
            Assert.Throws<DataException>(() => GachaRules.From(Doc("{\"id\":\"common\",\"rate\":1}", "\"general_h\""), gens)); // 등급 legend가 grades에 없음
        }

        [Fact(DisplayName = "모집은 행동(gacha.draw)으로: 기기와 서버가 같은 시드로 같은 결과, 세이브 v5에 천장·조각")]
        public void AsAction()
        {
            var a = Rich(); var b = SaveJson.Read(SaveJson.Write(a));
            var act = PlayerActionRunner.DrawGacha("standard", 10, 99);
            Assert.True(new PlayerActionRunner(Kit.D).Apply(a, act));
            Assert.True(new PlayerActionRunner(Kit.D).Apply(b, act));
            Assert.Equal(SaveJson.Write(a), SaveJson.Write(b));
            Assert.Equal(a.GachaPity["standard"], SaveJson.Read(SaveJson.Write(a)).GachaPity["standard"]);
        }
    
        [Fact(DisplayName = "R-57 한계 돌파: 조각 1·1·2·2·3개로 다섯 단계, 단계마다 레벨 상한 +5 (50 → 75). 모자라거나 끝 단계면 안 됨")]
        public void LimitBreak()
        {
            var svc = new LimitBreakService(Kit.D);
            var s = Rich();
            var g = s.FindGeneral("general_a");
            Assert.Equal(50, LimitBreakService.MaxLevelOf(Kit.D, g));
            Assert.Equal(LimitBreakService.NotEnoughShards, svc.Check(s, "general_a"));
            Assert.Equal(LimitBreakService.NoGeneral, svc.Check(s, "general_h"));
            s.GeneralShards["general_a"] = 10;
            long[] costs = { 1, 1, 2, 2, 3 };
            long left = 10;
            foreach (var c in costs)
            {
                Assert.Equal(c, svc.NextCost(g));
                Assert.True(svc.Apply(s, "general_a"));
                left -= c;
                Assert.Equal(left, LimitBreakService.Shards(s, "general_a"));
            }
            Assert.Equal(5, g.LimitBreak);
            Assert.Equal(75, LimitBreakService.MaxLevelOf(Kit.D, g));
            Assert.Equal(LimitBreakService.MaxStage, svc.Check(s, "general_a"));
            Assert.False(svc.Apply(s, "general_a"));
            Assert.Equal(1, LimitBreakService.Shards(s, "general_a"));
        }

        [Fact(DisplayName = "R-57 레벨 상한에 닿은 장수도 한계 돌파하면 다시 오른다 (전투 경험치도 같은 상한)")]
        public void LimitBreakRaisesCap()
        {
            var L = Kit.D.Levels;
            var rates = Kit.D.Generals["general_a"].GrowthRates;
            var atCap = Game.Progression.LevelEngine.AddExp(L, rates, L.MaxLevel, 0, 10_000);
            Assert.Equal((L.MaxLevel, 0), (atCap.Level, atCap.LevelsGained));
            var raised = Game.Progression.LevelEngine.AddExp(L, rates, L.MaxLevel, 0, L.ExpToNext(L.MaxLevel), L.MaxLevel + 5);
            Assert.Equal((L.MaxLevel + 1, 1), (raised.Level, raised.LevelsGained));
            var top = Game.Progression.LevelEngine.AddExp(L, rates, L.MaxLevel, 0, 10_000_000, L.MaxLevel + 5);
            Assert.Equal((L.MaxLevel + 5, 0L), (top.Level, top.Exp));
        }

        [Fact(DisplayName = "R-57 한계 돌파는 행동(general.limitBreak): 서버도 같은 규칙. 예전 세이브(단계 없음)는 0단계")]
        public void LimitBreakAction()
        {
            var runner = new PlayerActionRunner(Kit.D);
            var s = Rich();
            Assert.False(runner.Apply(s, PlayerActionRunner.LimitBreak("general_a")));
            s.GeneralShards["general_a"] = 1;
            Assert.True(runner.Apply(s, PlayerActionRunner.LimitBreak("general_a")));
            Assert.Equal(1, s.FindGeneral("general_a").LimitBreak);
            Assert.False(s.GeneralShards.ContainsKey("general_a"));

            var json = JObject.Parse(SaveJson.Write(s));
            foreach (JObject g in (JArray)json["campaign"]["Generals"]) g.Remove("LimitBreak");
            var old = SaveJson.Read(json.ToString());
            Assert.All(old.Generals, g => Assert.Equal(0, g.LimitBreak));
        }

        [Fact(DisplayName = "R-100 무료 모집: 하루 freeDaily번 재화 없이 1회, 다 쓰면 그날은 아무것도 안 바뀜, 다음 날 다시, 천장은 센다")]
        public void FreeDaily()
        {
            var svc = new GachaService(Kit.D);
            var b = Kit.D.Gacha.Banners["standard"];
            Assert.Equal(1, b.FreeDaily);
            var s = Rich(0);
            long day = 86400L * 20000;
            Assert.Equal(1, GachaService.FreeLeft(s, b, day + 10));
            var r = svc.DrawFree(s, "standard", 7, day + 10);
            Assert.True(r.Ok);
            Assert.Single(r.Pulls);
            Assert.Equal(0, s.Wallet.TryGetValue(ResourceIds.Gem, out var g) ? g : 0);
            Assert.Equal(1, s.GachaPity["standard"]);
            Assert.Equal(0, GachaService.FreeLeft(s, b, day + 500));
            var json = SaveJson.Write(s);
            var again = svc.DrawFree(s, "standard", 8, day + 500);
            Assert.False(again.Ok);
            Assert.Equal(GachaService.NoFree, again.Reason);
            Assert.Equal(json, SaveJson.Write(s));
            Assert.Equal(1, GachaService.FreeLeft(s, b, day + 86400));
            Assert.True(svc.DrawFree(s, "standard", 9, day + 86400).Ok);
        }

        [Fact(DisplayName = "R-100 무료 모집 행동: 같은 시드·시각이면 같은 결과, 하루 두 번째는 거절")]
        public void FreeAction()
        {
            var runner = new PlayerActionRunner(Kit.D);
            var a = Rich(0); var c = Rich(0);
            long now = 86400L * 20000 + 30;
            Assert.True(runner.Apply(a, PlayerActionRunner.DrawGachaFree("standard", 5, now)));
            Assert.True(runner.Apply(c, PlayerActionRunner.DrawGachaFree("standard", 5, now)));
            Assert.Equal(SaveJson.Write(a), SaveJson.Write(c));
            Assert.False(runner.Apply(a, PlayerActionRunner.DrawGachaFree("standard", 6, now + 60)));
        }
    }
}
