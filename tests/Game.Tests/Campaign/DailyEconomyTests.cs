using System.Linq;
using Game.Campaign;
using Game.Economy;
using Game.Flow;
using Game.Foundation.Time;
using Game.Online;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>출석 · 교환 · 금괴 스킬 해금 (economy.md 1장 · 1.0장 · 6장, verification R-112). 수치는 economy.json 임시값.</summary>
    public class DailyEconomyTests
    {
        /// <summary>2026-11-04 00:00 UTC = 한국 09:00. 한국 자정(하루 바뀜) = UTC 15:00 = T0 + 15시간.</summary>
        private static readonly long T0 = Kit.Clock().UtcNow.ToUnixTimeSeconds();
        private const long Day = 86400;

        [Fact(DisplayName = "R-112 출석: 하루 한 번 (서버 시각, 하루는 한국 자정에 바뀜), 차례대로 우편, 빠져도 이어서, 끝나면 처음부터")]
        public void Attendance()
        {
            var eco = new DailyEconomy(Kit.D);
            Assert.True(eco.AttendanceOn);
            var rec = new AttendanceRecord();
            var sent = new System.Collections.Generic.List<Mail>();

            var r = eco.CheckIn(rec, T0, sent.Add);
            Assert.Equal((AttendanceResults.Ok, 0), (r.Result, r.Index));
            Assert.Equal((MailKinds.Attendance, eco.Cycle[0].Resource, eco.Cycle[0].Amount), (sent[0].Kind, sent[0].Resource, sent[0].Amount));
            Assert.Equal(T0 + 15 * 3600, r.NextDayAtUnixSeconds); // UTC 15:00 = 한국 자정 (임시값 dayStartUtcOffsetHours 9)

            Assert.Equal(AttendanceResults.Already, eco.CheckIn(rec, T0 + 15 * 3600 - 1, sent.Add).Result); // 같은 날
            Assert.Single(sent);
            Assert.Equal(1, eco.CheckIn(rec, T0 + 15 * 3600, sent.Add).Index);                            // 자정 지나면 다음 날
            Assert.Equal(2, eco.CheckIn(rec, T0 + 15 * 3600 + 3 * Day, sent.Add).Index);                  // 며칠 빠져도 이어서 (missedResets false)
            long t = T0 + 15 * 3600 + 3 * Day;
            for (int i = 3; i < eco.Cycle.Count; i++) { t += Day; Assert.Equal(i, eco.CheckIn(rec, t, sent.Add).Index); }
            Assert.Equal(0, eco.CheckIn(rec, t + Day, sent.Add).Index); // 한 바퀴 끝 → 처음
            Assert.Contains(sent, m => m.Resource == ResourceIds.Gem);
        }

        [Fact(DisplayName = "R-112 교환: 금괴 → 엽전 · 곡식은 한 방향, 엽전 ↔ 곡식은 손실 + 하루 한도 (행동 시각의 날)")]
        public void Exchange()
        {
            var eco = new DailyEconomy(Kit.D);
            var runner = new PlayerActionRunner(Kit.D);
            var s = StartingKitService.CreateNewGame(Kit.D);
            s.Wallet[ResourceIds.Gold] = 20000; s.Wallet[ResourceIds.Supply] = 0; s.Wallet[ResourceIds.Gem] = 10;

            // 엽전 10 → 곡식 10 × 500 × 0.9 = 4500
            Assert.True(runner.Apply(s, PlayerActionRunner.Exchange("gold", "supply", 10, T0)));
            Assert.Equal((19990L, 4500L), (s.Wallet["gold"], s.Wallet["supply"]));
            // 곡식 → 엽전: 5000 → 10 × 0.9 = 9
            Assert.True(runner.Apply(s, PlayerActionRunner.Exchange("supply", "gold", 4500, T0)));
            Assert.Equal((19990L + 8, 0L), (s.Wallet["gold"], s.Wallet["supply"])); // 4500 → 9 × 0.9 = 8 (내림)

            // 하루 한도 (엽전 5000): 4990 더 내면 한도, 1 더는 거절, 다음 날(한국 자정 뒤)은 다시
            Assert.True(runner.Apply(s, PlayerActionRunner.Exchange("gold", "supply", 4990, T0)));
            Assert.Equal(0, eco.LeftToday(s, eco.Rate("gold", "supply"), T0));
            Assert.False(runner.Apply(s, PlayerActionRunner.Exchange("gold", "supply", 1, T0 + 100)));
            Assert.True(runner.Apply(s, PlayerActionRunner.Exchange("gold", "supply", 1, T0 + 15 * 3600)));

            // 금괴 → 엽전 (한도 · 손실 없음), 되돌리는 길은 없다
            long gold = s.Wallet["gold"];
            Assert.True(runner.Apply(s, PlayerActionRunner.Exchange("gem", "gold", 2, T0 + 15 * 3600)));
            Assert.Equal((gold + 200, 8L), (s.Wallet["gold"], s.Wallet["gem"]));
            Assert.False(runner.Apply(s, PlayerActionRunner.Exchange("gold", "gem", 100, T0 + 15 * 3600)));
            Assert.Null(eco.Rate("supply", "gem"));

            // 모자람 · 받는 양 0 · 시각 없는 행동은 거절 (한도를 0과 지금을 섞어 비우지 못하게)
            Assert.False(runner.Apply(s, PlayerActionRunner.Exchange("gem", "gold", 99, T0 + 15 * 3600)));
            s.Wallet["supply"] = 100;
            Assert.False(runner.Apply(s, PlayerActionRunner.Exchange("supply", "gold", 100, T0 + 15 * 3600))); // 100 → 0.2 → 0
            Assert.False(runner.Apply(s, PlayerActionRunner.Exchange("gem", "gold", 1, 0)));
        }

        [Fact(DisplayName = "R-112 금괴 스킬 해금: 스킬 단계 금괴로 얻기 조건(기록 카운터)을 해금, 계정 전체 · 한 번, 스킬북 · 요구 능력치는 그대로")]
        public void SkillUnlock()
        {
            var eco = new DailyEconomy(Kit.D);
            var runner = new PlayerActionRunner(Kit.D);
            var books = new SkillBookService(Kit.D);
            var s = StartingKitService.CreateNewGame(Kit.D);
            var g = s.FindGeneral("general_c");
            s.Wallet[ResourceIds.Gold] = 100000; s.Wallet[ResourceIds.Gem] = 40;

            Assert.Equal(30, eco.UnlockPrice("ambush"));   // basic
            Assert.Equal(60, eco.UnlockPrice("fireAttack")); // intermediate
            Assert.Null(eco.UnlockPrice("arrowRain"));      // 조건 없는 스킬은 해금할 것이 없다

            Assert.True(books.Buy(s, "ambush"));
            Assert.Equal(LearnCheck.ConditionNotMet, books.Check(s, g.GeneralId, "ambush"));
            Assert.False(runner.Apply(s, PlayerActionRunner.UnlockSkillLearn("fireAttack"))); // 금괴 모자람
            Assert.True(runner.Apply(s, PlayerActionRunner.UnlockSkillLearn("ambush")));
            Assert.Equal(10, s.Wallet[ResourceIds.Gem]);
            Assert.False(runner.Apply(s, PlayerActionRunner.UnlockSkillLearn("ambush")));     // 한 번만
            Assert.Equal(LearnCheck.Ok, books.Learn(s, g.GeneralId, "ambush"));                // 기록 없이 배운다
            Assert.Equal(LearnCheck.NoBook, books.Check(s, "general_b", "ambush"));            // 책은 여전히 필요
        }

        [Fact(DisplayName = "R-112 데이터: 자원 · 비율 · 한 방향 · 단계 이름 검사, 출석 우편 문구")]
        public void Data()
        {
            var eco = new DailyEconomy(Kit.D);
            Assert.Empty(eco.Validate(Kit.D.Campaign.SkillBookPriceByTier.Keys.ToList()));
            Assert.All(eco.Rates, x => Assert.NotEqual(ResourceIds.Gem, x.To));
            Assert.True(Kit.D.Strings.ContainsKey("ui.mail.attendance"));
        }

        [Fact(DisplayName = "R-112 가짜 서버: 출석 우편 → mail.claim으로 지갑에 (금괴 우편도), 같은 날 두 번은 already")]
        public void FakeServer()
        {
            var time = new ManualTimeProvider(Kit.Clock().UtcNow);
            var fake = new LocalOnlineService(Kit.D, Kit.Source, time);
            CloudSave save = null;
            fake.SignIn(_ => { });
            fake.LoadSave(x => save = x);
            AttendanceResponse r = null;
            fake.CheckIn(x => r = x);
            Assert.Equal(AttendanceResults.Ok, r.Result);
            fake.CheckIn(x => r = x);
            Assert.Equal(AttendanceResults.Already, r.Result);
            MailListResponse box = null;
            fake.GetMail(x => box = x);
            var mail = box.Mail.Single(m => m.Kind == MailKinds.Attendance);
            long before = SaveJson.Read(save.Json).Wallet.TryGetValue(mail.Resource, out var n) ? n : 0;
            ActionVerdict v = null;
            fake.SubmitActions(new ActionSubmission { BaseRevision = save.Revision, Actions = { PlayerActionRunner.ClaimMail(mail) } }, x => v = x);
            Assert.True(v.Valid, v.Reason);
            Assert.Equal(before + mail.Amount, SaveJson.Read(v.Save.Json).Wallet[mail.Resource]);

            var gemMail = new Mail { Id = "x", Resource = ResourceIds.Gem, Amount = 30 };
            var st = SaveJson.Read(v.Save.Json);
            long gem = st.Wallet.TryGetValue(ResourceIds.Gem, out var gm) ? gm : 0;
            Assert.True(new PlayerActionRunner(Kit.D).Apply(st, PlayerActionRunner.ClaimMail(gemMail))); // 금괴 우편 받기 (출석 7일째)
            Assert.Equal(gem + 30, st.Wallet[ResourceIds.Gem]);
        }
    }
}
