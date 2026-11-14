using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Data.Defs;
using Game.Flow;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>A3: 뽑기에서만 나오는 장수 없음 (generals.md 1장) — 스텝 첫 클리어 합류, 이미 있으면 조각. 새 장수는 기본 스킬·특성과 함께.</summary>
    public class GeneralJoinTests
    {
        [Fact(DisplayName = "모집 풀의 모든 장수는 플레이로도 얻는다 (초기 지급·스텝 합류·적으로 나와 등용)")]
        public void EveryGachaGeneralHasPlayPath()
        {
            var kit = Kit.Veteran().Generals.Select(g => g.GeneralId).ToHashSet();
            var join = new System.Collections.Generic.HashSet<string>();
            var enemies = new System.Collections.Generic.HashSet<string>();
            foreach (var id in Kit.D.StepIndex.Order)
            {
                var s = StepDef.Load(Kit.Source, id);
                foreach (var g in s.RewardGenerals) { Assert.True(Kit.D.Generals.ContainsKey(g), id + ": 없는 장수 " + g); join.Add(g); }
                foreach (var e in s.Enemy) enemies.Add(e.General);
            }
            foreach (var b in Kit.D.Gacha.Banners.Values)
                foreach (var g in b.Pool)
                    Assert.True(kit.Contains(g) || join.Contains(g) || (enemies.Contains(g) && Kit.D.Generals[g].Recruitable), "뽑기 전용 장수: " + g);
        }

        [Fact(Skip = "군대 개편 PR 2에서 다시 켬: 새 군대 규모(army-v2.md)에 맞춘 스텝 밸런스 재조정 전 (step03을 깨야 함)", DisplayName = "첫 클리어에 합류(기본 스킬·특성), 이미 있으면 조각 1개, 두 번째 클리어는 없음")]
        public void JoinOnFirstClear()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            CampaignState s = null;
            BattleReport report = null;
            for (ulong seed = 1; seed < 30 && (s == null || !s.ClearedSteps.Contains("step03")); seed++)
            {
                // 판마다 새 상태 (진 판의 손실이 다음 판에 남지 않게)
                s = Kit.Veteran();
                s.ClearedSteps.Add("step01");
                s.ClearedSteps.Add("step02");
                var plan = flow.Prepare(s, new SortieRequest { StepId = "step03", Seed = seed, Delegate = true });
                Assert.True(plan.Ok, string.Join(";", plan.Errors));
                flow.Confirm(s, plan);
                report = flow.ApplyResult(s, plan, BattleSimulation.Run(plan.Setup));
            }
            Assert.Contains("step03", s.ClearedSteps);
            Assert.Contains("general_d", report.JoinedGenerals);
            var d = s.FindGeneral("general_d");
            Assert.NotNull(d);
            Assert.Equal(Kit.D.Generals["general_d"].InnateSkills.ToList(), d.LearnedSkills);
        }

        [Fact(DisplayName = "이미 있는 장수가 합류 보상이면 조각, 새 장수는 모집·등용과 같은 모습 (GeneralState.Join)")]
        public void JoinShapes()
        {
            var def = Kit.D.Generals["general_a"];
            var g = Game.Campaign.Roster.GeneralState.Join(def);
            Assert.Equal(1, g.Level);
            Assert.Equal(def.InnateSkills.ToList(), g.LearnedSkills);
            Assert.Equal(def.Traits.ToList(), g.LearnedTraits);
        }
    }
}
