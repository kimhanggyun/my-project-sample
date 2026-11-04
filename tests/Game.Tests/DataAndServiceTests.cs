using System;
using System.Collections.Generic;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Campaign.Save;
using Game.Data.Json;
using Game.Data.Validation;
using Game.Economy;
using Game.Foundation.Math;
using Game.Foundation.Save;
using Game.Foundation.Time;
using Game.Services;
using Newtonsoft.Json.Linq;
using Xunit;
using static Game.Tests.TestData;

namespace Game.Tests
{
    public class DataTests
    {
        [Fact(DisplayName = "실제 데이터 전체가 데이터 검사를 통과한다")]
        public void RealDataIsValid() => Assert.Empty(DataValidator.Validate(Real()));

        [Fact(DisplayName = "새 게임 초기 지급: 장수 3명, 편성이 모두 유효")]
        public void StartingKit()
        {
            var d = Real();
            var state = StartingKitService.CreateNewGame(d);
            Assert.Equal(3, state.Generals.Count);
            Assert.Empty(CorpsValidator.Validate(d, state, state.Compositions));
            Assert.True(state.Wallet[ResourceIds.Supply] > 0);
            Assert.All(state.Generals, g => Assert.NotEmpty(g.LearnedSkills));
        }

        [Fact(DisplayName = "데이터 키는 영문 id, 화면 문구는 문자열 표에 있다")]
        public void Strings()
        {
            var d = Real();
            Assert.Equal("보병", d.Strings[d.TroopTypes["infantry"].NameKey]);
        }
    }

    public class FixTests
    {
        [Fact(DisplayName = "십진 문자열을 정확히 읽는다")]
        public void Parse()
        {
            Assert.Equal(Fix.FromRatio(145, 2), Fix.Parse("72.5"));
            Assert.Equal("72.5", Fix.Parse("72.5").ToString());
        }

        [Fact(DisplayName = "내림·올림·반올림 (음수 포함)")]
        public void Rounding()
        {
            Assert.Equal(-2, Fix.Parse("-1.5").FloorToLong());
            Assert.Equal(-1, Fix.Parse("-1.5").CeilToLong());
            Assert.Equal(2, Fix.Parse("1.5").RoundToLong());
            Assert.Equal(2, Fix.Parse("1.25").CeilToLong());
        }

        [Fact(DisplayName = "넘침은 숨기지 않고 예외로 드러낸다")]
        public void Overflow() => Assert.Throws<OverflowException>(() => Fix.FromInt(long.MaxValue / 2));

        [Fact(DisplayName = "10진이라 0.4가 정확하다: 1,000 × 0.4 = 400")]
        public void DecimalExact() => Assert.Equal(400, (Fix.Parse("0.4") * 1000).FloorToLong());
    }

    public class SaveTests
    {
        [Fact(DisplayName = "세이브 저장 후 불러오면 같은 상태")]
        public void RoundTrip()
        {
            var d = Real();
            var state = StartingKitService.CreateNewGame(d);
            state.Generals[0].StatGrowth["might"] = Fix.Parse("1.5");
            state.Units[0].Cohesion = Fix.Parse("12.25");
            var svc = new CampaignSaveService(new InMemorySaveStore());
            svc.Save(state);
            var loaded = svc.Load();
            Assert.Equal(state.Generals.Count, loaded.Generals.Count);
            Assert.Equal(Fix.Parse("1.5"), loaded.Generals[0].StatGrowth["might"]);
            Assert.Equal(Fix.Parse("12.25"), loaded.Units[0].Cohesion);
            Assert.Equal(state.Wallet[ResourceIds.Supply], loaded.Wallet[ResourceIds.Supply]);
            Assert.Equal(state.Compositions[0].Slots["vanguard"], loaded.Compositions[0].Slots["vanguard"]);
        }

        private sealed class V0ToV1 : ISaveMigration
        {
            public int FromVersion => 0;
            public void Apply(JObject save) { save["campaign"]["Wallet"] = new JObject { ["supply"] = 7 }; }
        }

        [Fact(DisplayName = "예전 세이브는 변환기를 차례로 적용한다")]
        public void Migration()
        {
            var store = new InMemorySaveStore();
            store.Write("campaign", "{\"version\":0,\"campaign\":{\"Generals\":[]}}");
            var loaded = new CampaignSaveService(store, new[] { new V0ToV1() }).Load();
            Assert.Equal(7, loaded.Wallet["supply"]);
        }

        [Fact(DisplayName = "게임보다 새 버전 세이브, 변환기 없는 버전은 거부한다")]
        public void RejectsUnknownVersions()
        {
            var store = new InMemorySaveStore();
            store.Write("campaign", "{\"version\":99,\"campaign\":{}}");
            Assert.Throws<InvalidOperationException>(() => new CampaignSaveService(store).Load());
            store.Write("campaign", "{\"version\":0,\"campaign\":{}}");
            Assert.Throws<InvalidOperationException>(() => new CampaignSaveService(store).Load());
        }

        [Fact(DisplayName = "파일 세이브: 쓰고 다시 읽는다")]
        public void FileStore()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "save-" + Guid.NewGuid().ToString("N"));
            var store = new FileSaveStore(dir);
            store.Write("a", "1");
            store.Write("a", "2");
            Assert.Equal("2", store.Read("a"));
        }
    }

    public class ServiceTests
    {
        private static ServiceConfig Load(string file)
        {
            var obj = JsonUtil.ParseObject(System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot, "config", file)));
            var map = new Dictionary<string, string>();
            foreach (var p in obj.Properties()) if (!p.Name.StartsWith("_")) map[p.Name] = p.Value.Value<string>();
            return new ServiceConfig(map);
        }

        [Fact(DisplayName = "개발 설정: 가짜 결제는 성공, 가짜 광고는 끝까지 봄")]
        public void Development()
        {
            var cfg = Load("services.development.json");
            var reg = ServiceRegistry.WithBuiltIns();
            PurchaseOutcome p = PurchaseOutcome.Failed;
            reg.Create<IStoreService>(ServiceIds.Store, cfg).Purchase("gold_pack_small", o => p = o);
            Assert.Equal(PurchaseOutcome.Succeeded, p);
            AdOutcome a = AdOutcome.Skipped;
            reg.Create<IRewardedAdService>(ServiceIds.RewardedAd, cfg).Show("supply", o => a = o);
            Assert.Equal(AdOutcome.Completed, a);
        }

        [Fact(DisplayName = "서비스를 꺼도 예외 없이 '사용 불가'로 답한다")]
        public void OffServicesAreSafe()
        {
            var cfg = Load("services.production.json");
            var reg = ServiceRegistry.WithBuiltIns();
            var store = reg.Create<IStoreService>(ServiceIds.Store, cfg);
            Assert.False(store.IsAvailable);
            PurchaseOutcome p = PurchaseOutcome.Succeeded;
            store.Purchase("x", o => p = o);
            Assert.Equal(PurchaseOutcome.Unavailable, p);
            var banner = reg.Create<IBannerAdService>(ServiceIds.BannerAd, cfg);
            banner.Show();
            Assert.Equal(0f, banner.HeightPixels);
        }

        [Fact(DisplayName = "등록되지 않은 구현 이름은 바로 드러낸다")]
        public void UnknownImpl()
        {
            var cfg = new ServiceConfig(new Dictionary<string, string> { [ServiceIds.Store] = "googlePlay" });
            Assert.Throws<InvalidOperationException>(() => ServiceRegistry.WithBuiltIns().Create<IStoreService>(ServiceIds.Store, cfg));
        }

        [Fact(DisplayName = "시간은 ITimeProvider로만: 수동 시계로 기한을 앞당길 수 있다")]
        public void ManualTime()
        {
            var t = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            t.Advance(TimeSpan.FromHours(3));
            Assert.Equal(3, t.UtcNow.Hour);
        }
    }

    public class WalletTests
    {
        [Fact(DisplayName = "지갑: 모자라면 쓰지 않는다")]
        public void Spend()
        {
            var w = new Wallet(new Dictionary<string, long> { [ResourceIds.Gold] = 10 });
            Assert.False(w.TrySpend(ResourceIds.Gold, 11));
            Assert.Equal(10, w.Get(ResourceIds.Gold));
            Assert.True(w.TrySpend(ResourceIds.Gold, 10));
            Assert.Equal(0, w.Get(ResourceIds.Gold));
        }
    }
}
