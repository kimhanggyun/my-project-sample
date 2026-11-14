using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Flow;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>군단 편성 (07 시안, verification R-95): 3×3 포진판 자리 → 역할·시작 위치, 총대장, 자동 편성, 편성 복사.</summary>
    public class CorpsBoardTests
    {
        private static StoryFlow Flow() => new StoryFlow(Kit.D, Kit.Source, Kit.Clock());

        [Fact(DisplayName = "R-95 포진판 줄 → 역할 (앞 선봉 · 가운데 측면 · 뒤 후위), 자리를 바꾸면 맞바꾼다, 총대장은 하나")]
        public void BoardRoles()
        {
            var s = Kit.Veteran();
            var ed = new FormationEditor(Kit.D, s);
            ed.SetBoard(0, 0, 1);
            ed.SetBoard(1, 1, 0);
            ed.SetBoard(2, 2, 1);
            Assert.Equal(new[] { "vanguard", "flank", "rear" }, ed.Draft.Select(c => c.Role));
            ed.SetBoard(2, 0, 1); // 제1군 자리로 → 제1군은 제3군의 옛 자리 (뒤 가운데)
            Assert.Equal((2, 1, "rear"), (ed.Draft[0].BoardRow.Value, ed.Draft[0].BoardCol.Value, ed.Draft[0].Role));
            Assert.Equal("vanguard", ed.Draft[2].Role);
            ed.SetCommander(1);
            Assert.Equal(new[] { false, true, false }, ed.Draft.Select(c => c.Commander));
            Assert.Empty(ed.Errors());
            ed.Draft[0].BoardRow = 0; ed.Draft[0].BoardCol = 1; // 겹침
            Assert.Contains(ed.Errors(), e => e.Contains("다른 군단"));
            ed.Draft[0].BoardRow = 3;
            Assert.Contains(ed.Errors(), e => e.Contains("3×3 밖"));
        }

        [Fact(DisplayName = "R-95 출진: 총대장 ★ 군단이 대장, 앞 줄 군단은 진영 앞쪽·뒤 줄은 뒤쪽에서 시작한다")]
        public void SortiePositions()
        {
            var s = Kit.Veteran();
            var ed = new FormationEditor(Kit.D, s);
            ed.SetBoard(0, 2, 1); // 제1군 뒤
            ed.SetBoard(1, 0, 1); // 제2군 앞
            ed.SetBoard(2, 1, 0); // 제3군 가운데 왼
            ed.SetCommander(2);
            Assert.True(ed.Apply());
            var plan = Flow().Prepare(s, new SortieRequest { StepId = "step01", Seed = 1 });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            var mine = plan.Setup.Corps.Where(c => c.Team == StoryFlow.PlayerTeam).ToList();
            Assert.Equal(Game.Battle.Model.Simulation.CorpsRoles.Commander, mine[2].Role);
            Assert.Null(mine[0].Role);
            Assert.Equal(("rear", "vanguard", "flank"), (mine[0].AdvanceRole, mine[1].AdvanceRole, mine[2].AdvanceRole));
            var sim = new BattleSimulation(plan.Setup);
            var back = sim.Context.FindCorps(mine[0].Id).Anchor;
            var front = sim.Context.FindCorps(mine[1].Id).Anchor;
            var facing = sim.Context.FindCorps(mine[1].Id).Facing;
            // 앞 줄이 진영 앞 방향으로 더 나가 있다 (두 줄 = spawnSpacingCells × 2)
            var d = front - back;
            Assert.True((d.X * facing.X + d.Y * facing.Y) > Game.Foundation.Math.Fix.Zero, "앞 줄 군단이 뒤 줄보다 앞에 있어야 한다");
        }

        [Fact(DisplayName = "R-95 자동 편성: 계급 군단 수까지 통솔 큰 장수, 남은 병력이 많은 병종부터 칸 수·통솔 한도까지, 자리·총대장도 채운다")]
        public void AutoFill()
        {
            var s = StartingKitService.CreateNewGame(Kit.D);
            s.LordLevel = 6; // 천부장: 군단 2 · 칸 2
            var ed = new FormationEditor(Kit.D, s);
            ed.AutoFill();
            Assert.Equal(2, ed.Draft.Count);
            Assert.All(ed.Draft, c => Assert.Equal(2, c.Picks.Count));
            Assert.All(ed.Draft, c => Assert.True(c.BoardRow.HasValue && c.BoardCol.HasValue));
            Assert.Single(ed.Draft, c => c.Commander);
            Assert.Empty(ed.Errors());
            // 병종 합은 보유를 넘지 않는다
            foreach (var u in s.Units) Assert.True(ed.Draft.SelectMany(c => c.Picks).Where(p => p.UnitId == u.UnitId).Sum(p => p.Troops) <= u.Troops);
        }

        [Fact(DisplayName = "R-95 편성 복사: 지금 편성(검사 통과)을 다른 상황의 편성으로")]
        public void CopyTo()
        {
            var s = Kit.Veteran();
            var ed = new FormationEditor(Kit.D, s);
            Assert.True(ed.CopyTo("siege"));
            Assert.Equal(s.Compositions.Count, s.CompositionPresets["siege"].Count);
            Assert.False(ed.CopyTo(null)); // 같은 상황(기본)
            ed.Draft[0].Picks.Clear(); // 검사 실패
            Assert.False(ed.CopyTo("naval"));
        }
    }
}
