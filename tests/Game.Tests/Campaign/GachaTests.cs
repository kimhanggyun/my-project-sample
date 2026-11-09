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
    }
}
