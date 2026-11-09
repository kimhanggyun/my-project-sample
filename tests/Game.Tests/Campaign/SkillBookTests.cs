using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Events;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Economy;
using Game.Flow;
using Game.Foundation.Math;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>스킬북 습득 (generals.md 4장, verification R-31·R-32, N-13).</summary>
    public class SkillBookTests
    {
        private static (CampaignState state, GeneralState g, SkillBookService books) Fresh(string generalId = "general_c")
        {
            var state = StartingKitService.CreateNewGame(Kit.D);
            return (state, state.FindGeneral(generalId), new SkillBookService(Kit.D));
        }

        [Fact(DisplayName = "R-31 스킬북: 돈으로 사고, 책·요구 능력치·습득 최소 조건을 모두 채워야 배우며, 배우면 책이 사라진다")]
        public void R31()
        {
            var (state, g, books) = Fresh();
            long gold = new Wallet(state.Wallet).Get(ResourceIds.Gold);
            Assert.Equal(LearnCheck.NoBook, books.Check(state, g.GeneralId, "ambush"));

            Assert.True(books.Buy(state, "ambush"));
            Assert.Equal(gold - books.Price("ambush").Value, new Wallet(state.Wallet).Get(ResourceIds.Gold));
            Assert.Equal(1, books.Owned(state, "ambush"));

            // 조건(복병을 당했거나 숨은 적을 찾아냄, 하나 이상)을 못 채움 → 못 배우고 책은 그대로
            Assert.Equal(LearnCheck.ConditionNotMet, books.Learn(state, g.GeneralId, "ambush"));
            Assert.Equal(1, books.Owned(state, "ambush"));
            Assert.DoesNotContain("ambush", g.LearnedSkills);

            g.Records[BattleRecords.Counter(EventKinds.HiddenFound)] = 1; // 둘 중 하나만 채워도 된다 (any)
            Assert.Equal(LearnCheck.Ok, books.Learn(state, g.GeneralId, "ambush"));
            Assert.Contains("ambush", g.LearnedSkills);
            Assert.Equal(0, books.Owned(state, "ambush"));
            Assert.Equal(LearnCheck.AlreadyKnown, books.Check(state, g.GeneralId, "ambush"));
        }

        [Fact(DisplayName = "R-31 조건이 없는 스킬은 책과 요구 능력치만 보고, 능력치가 모자라면 책을 보관한다")]
        public void R31NoConditionAndStats()
        {
            var (state, g, books) = Fresh("general_b"); // 지략 40
            state.Wallet[ResourceIds.Gold] = 5000;
            Assert.Empty(Kit.D.Skills["arrowRain"].LearnConditions);
            Assert.True(books.Buy(state, "arrowRain"));
            Assert.Equal(LearnCheck.Ok, books.Learn(state, g.GeneralId, "arrowRain"));

            Assert.True(books.Buy(state, "fireAttack")); // 지략 50 필요
            Assert.Equal(LearnCheck.StatTooLow, books.Learn(state, g.GeneralId, "fireAttack"));
            Assert.Equal(1, books.Owned(state, "fireAttack"));
        }

        [Fact(DisplayName = "R-31 돈이 모자라면 살 수 없고, 상점 가격은 스킬 단계별")]
        public void Prices()
        {
            var (state, _, books) = Fresh();
            Assert.Equal(Kit.D.Campaign.SkillBookPriceByTier["basic"], books.Price("ambush"));
            Assert.Equal(Kit.D.Campaign.SkillBookPriceByTier["intermediate"], books.Price("fireAttack"));
            state.Wallet[ResourceIds.Gold] = books.Price("ambush").Value - 1;
            Assert.False(books.Buy(state, "ambush"));
            Assert.Equal(0, books.Owned(state, "ambush"));
        }

        [Fact(DisplayName = "N-13 복병 스킬북 300, 지략 62 장수: 복병을 한 번도 안 당하고 숨은 적도 못 찾았으면 못 배움 → 한 번 당한 뒤 배움")]
        public void N13()
        {
            var (state, g, books) = Fresh();
            Assert.Equal(300, books.Price("ambush"));
            state.Wallet[ResourceIds.Gold] = 1000;
            books.Buy(state, "ambush");
            Assert.Equal(700, state.Wallet[ResourceIds.Gold]);
            Assert.Equal(LearnCheck.ConditionNotMet, books.Learn(state, g.GeneralId, "ambush"));
            g.Records[BattleRecords.Counter(EventKinds.Ambushed)] = 1;
            Assert.Equal(LearnCheck.Ok, books.Learn(state, g.GeneralId, "ambush"));
        }

        [Fact(DisplayName = "R-32 전투 이벤트는 그 아군 군단 장수의 기록 카운터 battle.<종류>에 쌓인다 (적 군단 이벤트는 무시)")]
        public void R32()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = StartingKitService.CreateNewGame(Kit.D);
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 1, Delegate = true });
            var result = BattleSimulation.Run(plan.Setup);
            var expected = new Dictionary<(string general, string kind), long>();
            foreach (var e in result.Events)
                if (e.CorpsId != null && plan.PlayerCorps.TryGetValue(e.CorpsId, out var comp))
                {
                    expected.TryGetValue((comp.GeneralId, e.Kind), out var n);
                    expected[(comp.GeneralId, e.Kind)] = n + 1;
                }
            Assert.NotEmpty(expected);
            BattleRecords.Record(state, plan.PlayerCorps, result);
            foreach (var kv in expected)
                Assert.Equal(kv.Value, state.FindGeneral(kv.Key.general).Records[BattleRecords.Counter(kv.Key.kind)]);
            Assert.DoesNotContain(state.Generals.SelectMany(g => g.Records.Keys), k => k.StartsWith(BattleRecords.Prefix) && !expected.Keys.Any(x => BattleRecords.Counter(x.kind) == k));
        }
    }
}
