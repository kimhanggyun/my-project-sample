using System.Collections.Generic;
using Game.Data.Json;
using Game.Foundation.Geo;
using Game.Online;
using Game.Server;
using Game.Territory;
using Xunit;

namespace Game.Server.Tests
{
    /// <summary>영토 장부 범위 (Firestore 1단계, db.md 4장): 요청마다 필요한 깃발만 읽고, 고친 · 더한 · 지운 것만 다시 쓴다.</summary>
    public class TerritoryScopeTests
    {
        private static readonly FlagDefs Fd = TerritoryDefs.Load(new FileDataSource(ServerPaths.DataRoot(null))).Flags;

        private static TerritoryOwner Flag(string owner, string at, long collected = 1000)
        {
            GeoPoint.TryParse(at, out var p);
            return new TerritoryOwner { RegionId = FlagDefs.KeyOf(Fd.CellOf(p)), OwnerAccountId = owner, CollectedUnixSeconds = collected, RadiusM = 1000 };
        }

        [Fact(DisplayName = "범위: 키 · 주인 · 주변 반지름 · 수입 기한 · 전부, 합집합")]
        public void Matches()
        {
            var a = Flag("a", "37.5665,126.978");
            var b = Flag("b", "37.60,126.978", collected: 5000);  // 약 3.8km 북쪽
            var region = new TerritoryOwner { RegionId = "dev_zone_a1", OwnerAccountId = "c" };
            GeoPoint.TryParse("37.5665,126.978", out var here);

            Assert.True(TerritoryScope.Keys(a.RegionId).Matches(a.RegionId, a));
            Assert.False(TerritoryScope.Keys(a.RegionId).Matches(b.RegionId, b));
            Assert.True(TerritoryScope.Owner("b").Matches(b.RegionId, b));
            Assert.True(TerritoryScope.Near(Fd, here, 5000).Matches(b.RegionId, b));
            Assert.False(TerritoryScope.Near(Fd, here, 3000).Matches(b.RegionId, b));
            Assert.False(TerritoryScope.Near(Fd, here, 100000).Matches(region.RegionId, region)); // 깃발이 아닌 지역은 주변 범위에 안 듦
            Assert.True(TerritoryScope.IncomeDue(1000).Matches(a.RegionId, a));
            Assert.False(TerritoryScope.IncomeDue(1000).Matches(b.RegionId, b));
            Assert.True(TerritoryScope.All().Matches(region.RegionId, region));
            var both = TerritoryScope.Owner("c").Or(TerritoryScope.Keys(a.RegionId));
            Assert.True(both.Matches(region.RegionId, region) && both.Matches(a.RegionId, a) && !both.Matches(b.RegionId, b));
        }

        [Fact(DisplayName = "저장소: 범위 밖은 규칙에 보이지 않고 그대로, 범위 안에서 고친 · 더한 · 지운 것은 반영")]
        public void WriteBack()
        {
            var store = new InMemoryStore();
            var a = Flag("a", "37.5665,126.978");
            var b = Flag("b", "37.60,126.978");
            var c = Flag("a", "37.70,126.978");
            store.WithTerritory("g", TerritoryScope.All(), o => { o[a.RegionId] = a; o[b.RegionId] = b; o[c.RegionId] = c; return 0; });

            int seen = store.WithTerritory("g", TerritoryScope.Owner("a"), o =>
            {
                Assert.False(o.ContainsKey(b.RegionId));        // 남의 깃발은 안 보임
                o[a.RegionId].CollectedUnixSeconds = 7777;       // 고침
                o.Remove(c.RegionId);                            // 지움
                o["flag:1,1"] = new TerritoryOwner { RegionId = "flag:1,1", OwnerAccountId = "a" }; // 더함
                return o.Count;
            });
            Assert.Equal(2, seen);
            var all = store.WithTerritory("g", TerritoryScope.All(), o => new Dictionary<string, TerritoryOwner>(o));
            Assert.Equal(7777, all[a.RegionId].CollectedUnixSeconds);
            Assert.False(all.ContainsKey(c.RegionId));
            Assert.True(all.ContainsKey("flag:1,1"));
            Assert.True(all.ContainsKey(b.RegionId));             // 범위 밖은 그대로
        }
    }
}
