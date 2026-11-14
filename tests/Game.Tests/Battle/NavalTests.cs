using System.Linq;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Control;
using Game.Battle.Model.Events;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Foundation.Math;
using Game.Tests.Campaign;
using Xunit;
using Xunit.Abstractions;

namespace Game.Tests.Battle
{
    /// <summary>R-85 수상전: 물 지형·수군, 난전(진형 효과 절반), 수군만 출진, 훈련 전투, 화선·충각.</summary>
    public class NavalTests
    {
        private readonly ITestOutputHelper _out;
        public NavalTests(ITestOutputHelper o) { _out = o; }

        /// <summary>새 게임의 부대를 모두 전투선으로 바꾼 상태 (수군 훈련이 열린다).</summary>
        public static CampaignState NavalState()
        {
            var s = Kit.Veteran();
            foreach (var u in s.Units) u.TroopType = "warship";
            return s;
        }

        [Fact(DisplayName = "R-85 물: 배만 다니고 배는 뭍에 못 오른다 (강도 물)")]
        public void WaterRules()
        {
            var water = Kit.D.TerrainRules["water"];
            var plain = Kit.D.TerrainRules["plain"];
            var ship = Kit.D.TroopTypes["warship"];
            var foot = Kit.D.TroopTypes["infantry"];
            Assert.False(water.Blocks(ship));
            Assert.True(water.Blocks(foot));
            Assert.True(plain.Blocks(ship));
            Assert.False(plain.Blocks(foot));
            Assert.False(Kit.D.TerrainRules["river"].Blocks(ship));
            Assert.Equal(Fix.FromRatio(1, 2), Kit.D.Combat.FormationScale("naval"));
            Assert.Equal(Fix.OneValue, Kit.D.Combat.FormationScale("field"));
        }

        [Fact(DisplayName = "R-85 수군 훈련: 수군 부대가 있어야 열리고, 수군이 아닌 부대로는 출진할 수 없다")]
        public void DrillUnlockAndNavalOnly()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var land = Kit.Veteran();
            Assert.False(flow.IsUnlocked(land, "naval_drill"));
            var mixed = Kit.Veteran();
            mixed.Units[0].TroopType = "warship";
            Assert.True(flow.IsUnlocked(mixed, "naval_drill"));
            var bad = flow.Prepare(mixed, new SortieRequest { StepId = "naval_drill", Seed = 1 });
            Assert.Contains(bad.Errors, e => e.Contains("수군"));
            var ok = flow.Prepare(NavalState(), new SortieRequest { StepId = "naval_drill", Seed = 1 });
            Assert.True(ok.Ok, string.Join("\n", ok.Errors));
            Assert.Equal("naval", ok.Setup.BattleType);
        }

        [Fact(Skip = "군대 개편 PR 2에서 다시 켬: 새 군대 규모(army-v2.md, 처음 십부장 보병 3,000)에 맞춘 스텝·영토 밸런스 재조정 전", DisplayName = "R-85 수군 훈련: 위임 출진으로 배들이 물 위로 나아가 싸우고 판이 끝난다 (승률 기록)")]
        public void DrillPlays()
        {
            var stats = new StepSimulator(Kit.D, Kit.Source).Measure("naval_drill", NavalState, Enumerable.Range(1, 12).Select(i => (ulong)i));
            _out.WriteLine($"naval_drill 승률 {stats.Wins}/{stats.Runs}, 시간 끝 {stats.TimeLimitEnds}");
            Assert.True(stats.TimeLimitEnds < stats.Runs / 2, "배가 움직이지 못해 시간만 끝나면 안 된다");
            Assert.True(stats.Wins >= stats.Runs * 3 / 10 && stats.Wins <= stats.Runs * 9 / 10, $"승률 {stats.Wins}/{stats.Runs}");
        }

        private static (BattleSimulation sim, CorpsState me) Start(string troop, params string[] skills)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = NavalState();
            foreach (var u in state.Units) u.TroopType = troop;
            var plan = flow.Prepare(state, new SortieRequest { StepId = "naval_drill", Seed = 1 });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            var c = plan.Setup.Corps.First(x => x.Controller == Controllers.Player);
            c.General.Intellect = Fix.Max(c.General.Intellect, Fix.FromInt(50));
            c.General.Might = Fix.Max(c.General.Might, Fix.FromInt(50));
            c.General.Skills.AddRange(skills);
            var sim = new BattleSimulation(plan.Setup);
            return (sim, sim.Context.FindCorps(c.Id));
        }

        private static CorpsState Approach(BattleSimulation sim, CorpsState me, int cells)
        {
            sim.Issue(new BattleCommand { CorpsId = me.Id, Kind = CommandKinds.Advance });
            for (int i = 0; i < 4000 && !sim.IsOver; i++)
            {
                var e = sim.Context.Corps.FirstOrDefault(x => x.Team != me.Team && x.InBattle && sim.Context.IsVisibleTo(me.Team, x) && (x.Anchor - me.Anchor).LengthSquared <= Fix.FromInt(cells * cells));
                if (e != null) return e;
                sim.Step();
            }
            return null;
        }

        [Fact(DisplayName = "R-85 화선: 수군만, 내 병력 15%를 태워 보내고 전조 뒤 반경 안 적이 (보낸 병력 × 2)를 나눠 받고 혼란·사기 −8")]
        public void FireShip()
        {
            var (sim, me) = Start("warship", "fireShip");
            var e = Approach(sim, me, 10);
            Assert.NotNull(e);
            long mine = me.Troops, theirs = e.Troops;
            Assert.True(sim.Issue(new BattleCommand { CorpsId = me.Id, Kind = CommandKinds.UseSkill, SkillId = "fireShip", X = e.Anchor.X, Y = e.Anchor.Y }));
            for (int i = 0; i < 25; i++) sim.Step();
            Assert.Contains(sim.Context.Log, x => x.Kind == EventKinds.FireShipHit && x.Data == me.Id);
            Assert.True(me.Troops <= mine - mine * 15 / 100 + 50);
            Assert.True(e.Troops < theirs);
        }

        [Fact(DisplayName = "R-85 충각 돌격: 충각선만, 대상에게 돌진해 6초 공격 +40%p, 대상 소부대 30% 혼란")]
        public void Ramming()
        {
            var (sim0, m0) = Start("warship", "ramming");
            var e0 = Approach(sim0, m0, 10);
            Assert.NotNull(e0);
            Assert.False(sim0.Issue(new BattleCommand { CorpsId = m0.Id, Kind = CommandKinds.UseSkill, SkillId = "ramming", TargetCorpsId = e0.Id }));

            var (sim, me) = Start("ramShip", "ramming");
            var e = Approach(sim, me, 10);
            Assert.NotNull(e);
            Assert.True(sim.Issue(new BattleCommand { CorpsId = me.Id, Kind = CommandKinds.UseSkill, SkillId = "ramming", TargetCorpsId = e.Id })); // 사거리(8) 밖이면 다가가서
            for (int i = 0; i < 1000 && !sim.IsOver && !sim.Context.Log.Any(x => x.Kind == EventKinds.Rammed); i++) sim.Step();
            Assert.True(sim.Context.Log.Any(x => x.Kind == EventKinds.Rammed && x.CorpsId == e.Id),
                $"over={sim.IsOver} st={me.Status} pending={me.PendingSkill != null} d={(e.Anchor - me.Anchor).Length} eSt={e.Status}");
            Assert.Equal(Fix.FromInt(40), me.FindStatus("buff.ramming").AttackPp);
            Assert.Contains(e.Units, u => u.Confused);
        }
    }
}
