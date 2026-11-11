using System.Linq;
using System.Text.RegularExpressions;
using Game.Battle.Model.Combat;
using Game.Battle.Model.Contests;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Foundation.Math;
using Game.Foundation.Random;
using Game.Tests.Campaign;
using Xunit;

namespace Game.Tests.Battle
{
    /// <summary>R-67 장수 특성: 정의(traits.json)의 효과가 조건에 맞을 때 전투에 들어간다.</summary>
    public class TraitTests
    {
        private static BattleSetup Step01(string trait)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var plan = flow.Prepare(StartingKitService.CreateNewGame(Kit.D), new SortieRequest { StepId = "step01", Seed = 3, Delegate = true });
            Assert.True(plan.Ok);
            foreach (var c in plan.Setup.Corps.Where(c => c.Team == StoryFlow.PlayerTeam))
            {
                c.General.Traits.Clear();
                if (trait != null) c.General.Traits.Add(trait);
            }
            foreach (var c in plan.Setup.Corps.Where(c => c.Team != StoryFlow.PlayerTeam)) c.General.Traits.Clear();
            return plan.Setup;
        }

        [Fact(DisplayName = "데이터에서 장수에게 주는 특성(장수 기본·성장 보상·숨은 보상)은 모두 traits.json에 정의")]
        public void AllGrantedTraitsDefined()
        {
            var ids = Kit.D.Generals.Values.SelectMany(g => g.Traits).ToList();
            foreach (var f in new[] { "Rules/growthTracks.json", "Rules/secrets.json" })
                foreach (Match m in Regex.Matches(Kit.Source.ReadText(f), "\"trait[A-Za-z]*\"\\s*:\\s*\"([^\"]+)\""))
                    ids.Add(m.Groups[1].Value);
            Assert.NotEmpty(ids);
            foreach (var id in ids.Distinct()) Assert.True(Kit.D.Traits.Traits.ContainsKey(id), "정의 없음: " + id);
        }

        [Fact(DisplayName = "조건이 맞을 때만 효과: 낮·맑음에서 야습·빗속 행군은 결과가 같고, 맹장은 결과가 바뀐다")]
        public void ConditionalDamage()
        {
            string Hash(string trait) => BattleSimulation.Run(Step01(trait)).StateHash.ToString("x16");
            string none = Hash(null);
            Assert.Equal(none, Hash("nightRaid")); // step01은 낮
            Assert.Equal(none, Hash("rainMarch")); // step01은 맑음
            Assert.NotEqual(none, Hash("fierce"));
        }

        [Fact(DisplayName = "사기 하락 배율·스킬 비용·스위치 끄기")]
        public void MoraleAndSkillCost()
        {
            var sim = new BattleSimulation(Step01("discipline"));
            var corps = sim.Context.Corps.First(c => c.Team == StoryFlow.PlayerTeam);
            Assert.Equal(Fix.FromRatio(90, 100), TraitEffects.MoraleLossMultiplier(sim.Context, corps));
            Assert.Equal(Fix.FromInt(15), TraitEffects.SkillCost(sim.Context, corps, "reveal", Fix.FromInt(15))); // 군기는 비용과 무관

            var sim2 = new BattleSimulation(Step01("keenEye"));
            var c2 = sim2.Context.Corps.First(c => c.Team == StoryFlow.PlayerTeam);
            Assert.Equal(Fix.FromInt(11), TraitEffects.SkillCost(sim2.Context, c2, "reveal", Fix.FromInt(15))); // 15 × 0.7 = 10.5 → 올림 11
            Assert.Equal(Fix.FromInt(40), TraitEffects.SkillCost(sim2.Context, c2, "fireAttack", Fix.FromInt(40)));

            var off = Step01("discipline");
            off.Flags = Kit.D.ResolveFlags(Newtonsoft.Json.Linq.JObject.Parse("{\"traits\": false}"));
            var sim3 = new BattleSimulation(off);
            Assert.Equal(Fix.OneValue, TraitEffects.MoraleLossMultiplier(sim3.Context, sim3.Context.Corps.First(c => c.Team == StoryFlow.PlayerTeam)));
        }

        [Fact(DisplayName = "대결 피해 % (일기당천): 같은 시드에서 주는 피해가 커진다")]
        public void ContestDamage()
        {
            var duel = Kit.D.Contests["duel"];
            Fix Hit(Fix pct)
            {
                var s = new ContestSession(duel, new ContestFighter("a", Fix.FromInt(60), "calm") { DamagePct = pct }, new ContestFighter("b", Fix.FromInt(60), "calm"), new SeededRandom(7));
                s.Play("attack", "charge"); // B만 맞는다 (×1.5)
                return duel.Start - s.B.Resource;
            }
            Assert.Equal(Fix.FromInt(20), TraitEffects.ContestDamagePct(Kit.D, new[] { "peerless" }));
            Assert.True(Hit(Fix.FromInt(20)) > Hit(Fix.Zero));
        }
    }
}
