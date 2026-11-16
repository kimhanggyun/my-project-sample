using System.Collections.Generic;
using Game.Flow;
using Game.Foundation.Geo;
using Game.Online;
using Game.Territory;
using Game.Tests.Campaign;
using Xunit;

namespace Game.Tests.Territory
{
    /// <summary>푸시 알림 보낼 대상 규칙 (R-119).</summary>
    public class PushRulesTests
    {
        private const long Now = 1_800_000_000;
        private static TerritoryDefs Defs => TerritoryDefs.Load(Kit.Source);

        private static TerritoryOwner Flag(string owner, string at, long collected)
        {
            GeoPoint.TryParse(at, out var p);
            var d = Defs.Flags;
            return new TerritoryOwner { RegionId = FlagDefs.KeyOf(d.CellOf(p)), OwnerAccountId = owner, CollectedUnixSeconds = collected, SinceUnixSeconds = collected };
        }

        [Fact(DisplayName = "R-119 공성전 준비 시작: 깃발 주인에게 attacked, 주인의 연합원(주인 · 공격자 빼고)에게 allyHelp")]
        public void SiegeStart()
        {
            var flag = Flag("def", "37.56,126.97", Now);
            var ally = new Alliance { Id = "al", Members = { "def", "m1", "atk", "m2" } };
            var list = PushRules.ForSiegeStart(flag, "atk", ally, null);
            Assert.Equal(new[] { ("def", PushKinds.Attacked), ("m1", PushKinds.AllyHelp), ("m2", PushKinds.AllyHelp) }, list.ConvertAll(m => (m.AccountId, m.Kind)));
            Assert.All(list, m => Assert.Equal(flag.RegionId, m.RegionId));
            Assert.Single(PushRules.ForSiegeStart(flag, "atk", null, null));
        }

        [Fact(DisplayName = "R-119 수입 가득: 최대 시간에 닿은 깃발만, 주인별로 한 통 (깃발 수), 한 번만 · 받은 뒤 다시 차면 또")]
        public void IncomeFull()
        {
            long cap = Defs.Flags.IncomeCapHours * 3600;
            var d = Defs;
            var a1 = Flag("a", "37.56,126.97", Now - cap);
            var a2 = Flag("a", "37.58,126.97", Now - cap - 10);
            var b1 = Flag("b", "37.60,126.97", Now - cap + 1); // 아직
            var owners = new Dictionary<string, TerritoryOwner> { [a1.RegionId] = a1, [a2.RegionId] = a2, [b1.RegionId] = b1 };
            var first = PushRules.IncomeFullDue(d, owners, Now);
            var m = Assert.Single(first);
            Assert.Equal(("a", PushKinds.IncomeFull, "2"), (m.AccountId, m.Kind, m.Args[0]));
            Assert.Equal("b", Assert.Single(PushRules.IncomeFullDue(d, owners, Now + 1)).AccountId); // a는 이미 알림 → b만
            Assert.Empty(PushRules.IncomeFullDue(d, owners, Now + 100)); // 한 번만
            a1.CollectedUnixSeconds = Now + 200; // 받음 → 다시 차면 또
            Assert.Empty(PushRules.IncomeFullDue(d, owners, Now + 300));
            Assert.Equal("1", Assert.Single(PushRules.IncomeFullDue(d, owners, Now + 200 + cap)).Args[0]);
        }

        [Fact(DisplayName = "R-119 글 · 설정: 테마 문자열 push.{종류}.title · body로, 종류마다 켬 · 끔 (처음엔 모두 켬)")]
        public void TextAndSettings()
        {
            foreach (var k in PushKinds.All)
            {
                var (title, body) = PushRules.Render(Kit.D, new PushMessage { Kind = k, Args = { "3" } });
                Assert.False(title.StartsWith("push."), k);
                Assert.False(body.StartsWith("push."), k);
            }
            Assert.Contains("3", PushRules.Render(Kit.D, new PushMessage { Kind = PushKinds.IncomeFull, Args = { "3" } }).body);
            var s = new PushSettings();
            Assert.True(s.Allows(PushKinds.Attacked) && s.Allows(PushKinds.AllyHelp) && s.Allows(PushKinds.IncomeFull));
            s.AllyHelp = false;
            Assert.False(s.Allows(PushKinds.AllyHelp));
            Assert.False(s.Allows("other"));
        }
    }
}
