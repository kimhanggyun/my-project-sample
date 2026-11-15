using Game.Territory;
using Game.Tests.Campaign;
using Xunit;

namespace Game.Tests.Territory
{
    /// <summary>영토 지도 바탕 (Natural Earth 타일, data/Territory/geo).</summary>
    public class GeoTilesTests
    {
        [Fact(DisplayName = "지도 바탕: 칸 번호 · 서울은 육지, 서해 바다는 아님, 파일이 없는 칸은 빈 바다, 서울 칸에 강 선이 있다")]
        public void SeoulAndSea()
        {
            Assert.Equal((12, 30), GeoTiles.IndexOf(37_566_500, 126_978_000));
            Assert.Equal((0, 0), GeoTiles.IndexOf(-90_000_000, -180_000_000));
            var t = GeoTiles.Load(Kit.Source, 12, 30);
            Assert.NotEmpty(t.Land);
            Assert.NotEmpty(t.River);
            Assert.True(GeoTiles.OnLand(t, 37_566_500, 126_978_000));   // 서울 시청
            Assert.False(GeoTiles.OnLand(t, 37_000_000, 125_000_000));  // 서해
            Assert.True(GeoTiles.OnLand(t, 35_179_600, 129_075_600));   // 부산
            Assert.Empty(GeoTiles.Load(Kit.Source, 99, 99).Land);
            Assert.Equal(4, GeoTiles.ForView(Kit.Source, 39_000_000, 129_000_000, 41_000_000, 131_000_000).Count); // 40도 · 130도 모서리에 걸침
        }
    }
}
