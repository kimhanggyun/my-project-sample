using Game.Data.Defs;
using Game.Foundation.Math;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>R-90 장수가 타는 것 (보기만).</summary>
    public class MountTests
    {
        [Fact(DisplayName = "R-90 탈것: 지력 ≥ 무력 + 10이면 가마, 아니면 말. 장수 정의의 mount가 있으면 그것")]
        public void Resolve()
        {
            var m = Kit.D.Mounts;
            Assert.Equal(Fix.FromInt(10), m.PalanquinIntellectOverMight);
            Assert.Equal(MountRules.Horse, m.Resolve(null, Fix.FromInt(45), Fix.FromInt(45)));
            Assert.Equal(MountRules.Horse, m.Resolve(null, Fix.FromInt(50), Fix.FromInt(59)));
            Assert.Equal(MountRules.Palanquin, m.Resolve(null, Fix.FromInt(50), Fix.FromInt(60)));
            Assert.Equal(MountRules.Horse, m.Resolve(MountRules.Horse, Fix.FromInt(38), Fix.FromInt(62)));
            Assert.Equal(MountRules.Palanquin, m.Resolve(Kit.D, "general_c", Fix.FromInt(38), Fix.FromInt(62))); // 책사
            Assert.Equal(MountRules.Horse, m.Resolve(Kit.D, "general_b", Fix.FromInt(62), Fix.FromInt(40)));
            Assert.Contains(MountRules.Palanquin, m.Kinds);
        }
    }
}
