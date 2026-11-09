using Game.Flow;
using Game.Online;
using Xunit;

namespace Game.Tests
{
    /// <summary>닉네임 (공통 계정): 규칙과 가짜 서버.</summary>
    public class NicknameTests
    {
        [Fact(DisplayName = "닉네임 규칙: 앞뒤 공백 빼고 2~10자, 글자·숫자·밑줄만")]
        public void Rules()
        {
            Assert.Equal("관우", NicknameRules.Normalize("  관우 "));
            Assert.Equal("Guan_Yu99", NicknameRules.Normalize("Guan_Yu99"));
            Assert.Null(NicknameRules.Normalize("가"));
            Assert.Null(NicknameRules.Normalize("가나다라마바사아자차카"));
            Assert.Null(NicknameRules.Normalize("관 우"));
            Assert.Null(NicknameRules.Normalize("관우!"));
            Assert.Null(NicknameRules.Normalize(null));
        }

        [Fact(DisplayName = "가짜 서버: 내 닉네임을 정하면 프로필·이름 찾기에 나온다, 시장 등록자는 NPC")]
        public void Fake()
        {
            var fake = new LocalOnlineService(Campaign.Kit.D, Campaign.Kit.Source, Campaign.Kit.Clock());
            ProfileResponse p = null;
            fake.GetProfile(x => p = x);
            Assert.Null(p.Nickname);
            NicknameResponse r = null;
            fake.SetNickname("!", x => r = x);
            Assert.Equal(NicknameRules.BadName, r.Reason);
            fake.SetNickname("조운", x => r = x);
            Assert.True(r.Ok);
            NamesResponse names = null;
            fake.GetNames(new[] { fake.AccountId, LocalOnlineService.NpcAccount, "nobody" }, x => names = x);
            Assert.Equal("조운", names.Names[fake.AccountId]);
            Assert.Equal("NPC", names.Names[LocalOnlineService.NpcAccount]);
            Assert.False(names.Names.ContainsKey("nobody"));
        }
    }
}
