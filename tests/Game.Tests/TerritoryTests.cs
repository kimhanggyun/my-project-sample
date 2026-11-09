using System.Linq;
using Game.Data.Json;
using Game.Foundation.Geo;
using Game.Services;
using Game.Territory;
using Xunit;

namespace Game.Tests
{
    /// <summary>영토 첫 단계 (territory.md 2·3장, verification R-45): 위치 서비스, 지역 데이터, 좌표 → 지역, 공격 가능 지역.</summary>
    public class TerritoryTests
    {
        private static TerritoryDefs Defs() => TerritoryDefs.Load(Campaign.Kit.Source);
        private static GeoPoint P(string s) { Assert.True(GeoPoint.TryParse(s, out var p)); return p; }

        [Fact(DisplayName = "좌표 문자열: 백만분의 1도 정수로 (그 아래 버림), 범위 밖은 거절")]
        public void ParseGeo()
        {
            var p = P("37.5665,126.978");
            Assert.Equal((37_566_500L, 126_978_000L), (p.LatE6, p.LonE6));
            Assert.Equal(-1_234_567L, P("-1.2345679,0").LatE6);
            Assert.False(GeoPoint.TryParse("91,0", out _));
            Assert.False(GeoPoint.TryParse("abc", out _));
            Assert.Equal("37.566500,126.978000", p.ToString());
        }

        [Fact(DisplayName = "R-45 다각형 안: 반직선 교차(정수), 경계선 위는 안, 오목한 모양도")]
        public void Contains()
        {
            var square = new[] { P("0,0"), P("0,10"), P("10,10"), P("10,0") };
            Assert.True(RegionLocator.Contains(square, P("5,5")));
            Assert.True(RegionLocator.Contains(square, P("0,5"))); // 변 위
            Assert.True(RegionLocator.Contains(square, P("10,10"))); // 꼭짓점
            Assert.False(RegionLocator.Contains(square, P("10.000001,5")));
            // ㄷ자 (오목): 가운데 파인 곳은 밖
            var u = new[] { P("0,0"), P("0,10"), P("10,10"), P("10,7"), P("3,7"), P("3,3"), P("10,3"), P("10,0") };
            Assert.True(RegionLocator.Contains(u, P("1,5")));
            Assert.False(RegionLocator.Contains(u, P("6,5")));
            Assert.True(RegionLocator.Contains(u, P("6,8")));
        }

        [Fact(DisplayName = "R-45 좌표 → 지역: 큰 단계부터 상위 안에서만 찾아 가장 작은 지역까지, 밖이면 없음")]
        public void Locate()
        {
            var loc = new RegionLocator(Defs());
            var path = loc.Locate(FakeLocationService.Default);
            Assert.Equal(new[] { "dev_do", "dev_gu_west", "dev_dong_a", "dev_zone_a1" }, path.Select(r => r.Id));
            Assert.Equal("dev_zone_a2", loc.Current(P("37.58,126.98")).Id);
            Assert.Equal("dev_gu_east", loc.Current(P("37.5,127.1")).Id); // 아래 단계 데이터가 없는 곳
            Assert.Null(loc.Current(P("35.0,129.0")));
        }

        [Fact(DisplayName = "R-45 공격할 수 있는 지역 = 단계마다 지금 지역 + 이웃, 필요 군단 수는 지역 값 → 단계 기본값")]
        public void Attackable()
        {
            var defs = Defs();
            var list = new RegionLocator(defs).Attackable(FakeLocationService.Default).Select(r => r.Id).ToList();
            Assert.Equal(new[] { "dev_do", "dev_gu_west", "dev_gu_east", "dev_dong_a", "dev_dong_b", "dev_dong_c", "dev_zone_a1", "dev_zone_a2", "dev_zone_a3" }, list);
            Assert.Equal(1, defs.RequiredLegions(defs.Regions["dev_zone_a1"]));
            Assert.Equal(5, defs.RequiredLegions(defs.Regions["dev_dong_a"]));
            Assert.Equal(3, defs.RequiredLegions(defs.Regions["dev_dong_c"])); // 덮어쓰기
            Assert.True(defs.Levels[RegionLevels.Sido].SeasonOnly);
        }

        [Fact(DisplayName = "지역 데이터 검사: 이웃은 서로·같은 단계, 상위는 더 큰 단계, 경계 꼭짓점 3개 이상")]
        public void Validation()
        {
            string Doc(string regions) => "{\"levels\":{\"sido\":{\"requiredLegions\":30},\"sigungu\":{\"requiredLegions\":10},\"eupmyeondong\":{\"requiredLegions\":5},\"ri\":{\"requiredLegions\":1}},\"regions\":[" + regions + "]}";
            const string box = "\"boundary\":[[0,0],[0,1],[1,1]]";
            TerritoryDefs Load(string regions) => TerritoryDefs.Load(new MemorySource(TerritoryDefs.File, Doc(regions)));
            Load("{\"id\":\"a\",\"level\":\"sido\"," + box + "}");
            Assert.Throws<DataException>(() => Load("{\"id\":\"a\",\"level\":\"sido\",\"boundary\":[[0,0],[0,1]]}"));
            Assert.Throws<DataException>(() => Load("{\"id\":\"a\",\"level\":\"sido\"," + box + ",\"neighbors\":[\"b\"]},{\"id\":\"b\",\"level\":\"sido\"," + box + "}"));
            Assert.Throws<DataException>(() => Load("{\"id\":\"a\",\"level\":\"ri\",\"parent\":\"b\"," + box + "},{\"id\":\"b\",\"level\":\"ri\",\"parent\":\"a\"," + box + "}"));
            Assert.Throws<DataException>(() => Load("{\"id\":\"a\",\"level\":\"sigungu\"," + box + "}")); // 상위 없음
        }

        [Fact(DisplayName = "위치 서비스: 끔은 off로 실패, 가짜는 고정 좌표 (설정으로 바꿀 수 있다)")]
        public void LocationServices()
        {
            var reg = ServiceRegistry.WithBuiltIns();
            var off = reg.Create<ILocationService>(ServiceIds.Location, new ServiceConfig(new System.Collections.Generic.Dictionary<string, string>()));
            LocationResult r = null;
            off.Request(x => r = x);
            Assert.Equal((false, LocationReasons.Off), (r.Ok, r.Reason));
            var fake = (FakeLocationService)reg.Create<ILocationService>(ServiceIds.Location, new ServiceConfig(new System.Collections.Generic.Dictionary<string, string> { [ServiceIds.Location] = ServiceImpl.Fake }));
            fake.Point = P("37.58,126.98");
            fake.Request(x => r = x);
            Assert.True(r.Ok);
            Assert.Equal(37_580_000L, r.Point.LatE6);
        }

        private sealed class MemorySource : IDataSource
        {
            private readonly string _path, _text;
            public MemorySource(string path, string text) { _path = path; _text = text; }
            public bool Exists(string path) => path == _path;
            public string ReadText(string path) => _text;
        }
    }
}
