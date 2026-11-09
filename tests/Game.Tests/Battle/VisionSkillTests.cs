using System.Linq;
using Game.Battle.Model.Ai;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Events;
using Game.Battle.Model.Simulation;
using Game.Battle.Model.Skills;
using Game.Foundation.Math;
using Xunit;
using static Game.Tests.Battle.BattleKit;

namespace Game.Tests.Battle
{
    /// <summary>시야·안개·은신 (battle.md 2.10, 2.18).</summary>
    public class VisionTests
    {
        private static CorpsSetup At(CorpsSetup c, int x, int y, string facing)
        {
            c.X = Fix.FromInt(x) + Fix.Parse("0.5");
            c.Y = Fix.FromInt(y) + Fix.Parse("0.5");
            c.Facing = facing;
            return c;
        }

        private static CorpsSetup Holder(string id, string team, string troop = "infantry") =>
            Corps(id, team, "square", troop, controller: Controllers.Player);

        [Fact(DisplayName = "안개: 멀리 있는 적은 보이지 않고, 스위치를 끄면 모두 보인다")]
        public void FogHidesDistantEnemies()
        {
            var fog = new BattleSimulation(Setup("test_plain", 1, null, Holder("A", "teamA"), Holder("B", "teamB")));
            Assert.False(fog.Context.IsVisibleTo("teamA", fog.Context.FindCorps("B")));
            var open = new BattleSimulation(Setup("test_plain", 1, "{\"fogOfWar\": false}", Holder("A", "teamA"), Holder("B", "teamB")));
            Assert.True(open.Context.IsVisibleTo("teamA", open.Context.FindCorps("B")));
        }

        [Fact(DisplayName = "보이지 않는 적은 공격 대상으로 지정할 수 없다")]
        public void CannotTargetUnseen()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, null, Holder("A", "teamA"), Holder("B", "teamB")));
            Assert.False(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.Attack, TargetCorpsId = "B" }));
        }

        [Fact(DisplayName = "척후병은 시야가 넓다: 같은 거리의 적이 척후병이 있을 때만 보인다")]
        public void ScoutsSeeFarther()
        {
            // A의 소부대와 B 사이 약 10칸: 보병 시야(7)로는 안 보이고 척후병 시야(10.5)로는 보인다
            var plain = new BattleSimulation(Setup("test_plain", 1, null, At(Holder("A", "teamA"), 12, 30, "N"), At(Holder("B", "teamB"), 12, 16, "S")));
            Assert.False(plain.Context.IsVisibleTo("teamA", plain.Context.FindCorps("B")));
            var withScout = At(Mixed("A", "teamA", "square", ("vanguard", "scout", 500), ("center", "infantry", 1000)), 12, 30, "N");
            withScout.Controller = Controllers.Player;
            var sim = new BattleSimulation(Setup("test_plain", 1, null, withScout, At(Holder("B", "teamB"), 12, 16, "S")));
            Assert.True(sim.Context.IsVisibleTo("teamA", sim.Context.FindCorps("B")));
        }

        [Fact(DisplayName = "숲 안의 적은 가까이 가야 보인다")]
        public void ForestConceals()
        {
            var far = new BattleSimulation(Setup("test_forest", 1, null, At(Holder("A", "teamA"), 12, 26, "N"), At(Holder("B", "teamB"), 12, 18, "S")));
            Assert.False(far.Context.IsVisibleTo("teamA", far.Context.FindCorps("B")));
            var plain = new BattleSimulation(Setup("test_plain", 1, null, At(Holder("A", "teamA"), 12, 26, "N"), At(Holder("B", "teamB"), 12, 18, "S")));
            Assert.True(plain.Context.IsVisibleTo("teamA", plain.Context.FindCorps("B")));
        }

        [Fact(DisplayName = "탐색 기억: 결과의 탐색 칸을 다음 전투 시작에 넣으면 처음부터 탐색됨 (fogMemory)")]
        public void ExploredMemory()
        {
            var first = BattleSimulation.Run(Setup("test_plain", 1, null, Corps("A", "teamA", "square"), Corps("B", "teamB", "square")));
            var explored = first.Explored["teamA"];
            int count = explored.Count(x => x);
            Assert.True(count > 0);

            var setup = Setup("test_plain", 1, null, Holder("A", "teamA"), Holder("B", "teamB"));
            setup.InitialExplored["teamA"] = explored;
            var sim = new BattleSimulation(setup);
            Assert.True(sim.Context.Vision["teamA"].ExploredCells.Count(x => x) >= count);

            var off = Setup("test_plain", 1, "{\"fogMemory\": false}", Holder("A", "teamA"), Holder("B", "teamB"));
            off.InitialExplored["teamA"] = explored;
            Assert.True(new BattleSimulation(off).Context.Vision["teamA"].ExploredCells.Count(x => x) < count);
        }

        [Fact(DisplayName = "보이는 적이 없으면 AI는 적 시작 위치로 진군해 결국 싸운다")]
        public void AiAdvancesUnderFog()
        {
            var r = BattleSimulation.Run(Setup("test_plain", 1, null, Corps("A", "teamA", "square"), Corps("B", "teamB", "square")));
            Assert.Equal(BattleEndReasons.Defeat, r.EndReason);
            Assert.All(r.Corps, c => Assert.True(c.Losses > 0));
        }
    }

    /// <summary>스킬 (verification R-11~R-13, skills.md 1차).</summary>
    public class SkillTests
    {
        private static CorpsSetup With(CorpsSetup c, int intellect = 50, int might = 50, int leadership = 50, params string[] skills)
        {
            c.General = General("g_" + c.Id, leadership, might, intellect);
            c.General.Skills.AddRange(skills);
            c.Controller = Controllers.Player;
            return c;
        }

        private static CorpsSetup At(CorpsSetup c, int x, int y, string facing)
        {
            c.X = Fix.FromInt(x) + Fix.Parse("0.5");
            c.Y = Fix.FromInt(y) + Fix.Parse("0.5");
            c.Facing = facing;
            return c;
        }

        private static BattleCommand Use(string corps, string skill, int x = 0, int y = 0, string target = null) =>
            new BattleCommand { CorpsId = corps, Kind = CommandKinds.UseSkill, SkillId = skill, X = Fix.FromInt(x), Y = Fix.FromInt(y), TargetCorpsId = target };

        private static void Steps(BattleSimulation sim, int n) { for (int i = 0; i < n && !sim.IsOver; i++) sim.Step(); }

        [Fact(DisplayName = "N-10 지략 80: 간파(15, 2회) 두 번 → 점수 50, 세 번째는 못 씀 (R-11, R-12)")]
        public void N10RevealTwice()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, null, With(Corps("A", "teamA", "square"), intellect: 80, skills: "reveal"), With(Corps("B", "teamB", "square"))));
            var a = sim.Context.FindCorps("A");
            Assert.Equal(Fix.FromInt(80), a.SkillPoints.Points);
            Assert.True(sim.Issue(Use("A", "reveal", 12, 6)));
            sim.Step();
            Assert.True(sim.Issue(Use("A", "reveal", 12, 6)));
            sim.Step();
            Assert.Equal(0, a.SkillPoints.UsesLeft("reveal"));
            Assert.False(sim.Issue(Use("A", "reveal", 12, 6)));
            Assert.True(a.SkillPoints.Points <= Fix.FromInt(51)); // 80 − 30 (+ 두 틱 회복분)
        }

        [Fact(DisplayName = "요구 능력치: 지략이 모자라면 못 쓰고, '또는' 요구는 하나만 맞으면 된다")]
        public void Requirements()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, "{\"fogOfWar\": false}",
                With(Corps("A", "teamA", "square"), intellect: 10, might: 50, skills: new[] { "reveal", "taunt" }), With(Corps("B", "teamB", "square"))));
            Assert.False(sim.Issue(Use("A", "reveal", 12, 6)));              // 지략 20 필요
            Assert.False(sim.Issue(Use("A", "rally")));                       // 배우지 않은 스킬
            Assert.False(sim.Issue(Use("A", "taunt", target: "B")));          // 무력 50으로 요구는 맞지만 사거리 10 밖 → 다가가기
            var a = sim.Context.FindCorps("A");
            Assert.True(SkillRules.RequirementsMet(a, sim.Context.Defs.Skills["taunt"]));
        }

        [Fact(DisplayName = "간파: 지점을 밝혀 멀리 있는 적도 보인다")]
        public void RevealShowsArea()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, null, With(Corps("A", "teamA", "square"), intellect: 60, skills: "reveal"), With(Corps("B", "teamB", "square"))));
            Assert.False(sim.Context.IsVisibleTo("teamA", sim.Context.FindCorps("B")));
            Assert.True(sim.Issue(Use("A", "reveal", 12, 6)));
            sim.Step();
            Assert.True(sim.Context.IsVisibleTo("teamA", sim.Context.FindCorps("B")));
            Steps(sim, 60); // 5초 지나면 꺼진다
            Assert.False(sim.Context.IsVisibleTo("teamA", sim.Context.FindCorps("B")));
        }

        private static BattleSimulation AmbushScene(string flags = null, bool bHasReveal = false)
        {
            var a = At(With(Corps("A", "teamA", "square"), intellect: 60, skills: "ambush"), 8, 18, "E"); // 숲 동쪽 끝
            var b = With(Corps("B", "teamB", "square"), intellect: 60, skills: bHasReveal ? new[] { "reveal" } : new string[0]);
            var sim = new BattleSimulation(Setup("test_ambush", 3, flags, a, b));
            Assert.True(sim.Issue(Use("A", "ambush")));
            return sim;
        }

        [Fact(DisplayName = "복병: 준비가 끝나면 은신, 가까이 온 적에게 기습 (혼란·사기↓·기습 보정)")]
        public void AmbushSprings()
        {
            var sim = AmbushScene();
            var a = sim.Context.FindCorps("A");
            var b = sim.Context.FindCorps("B");
            Steps(sim, 205);
            Assert.True(a.Hidden);
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "B", Kind = CommandKinds.MoveTo, X = Fix.FromInt(12), Y = Fix.FromInt(36) }));
            Fix moraleBefore = b.Morale;
            bool sprang = false;
            for (int i = 0; i < 600 && !sim.IsOver && !sprang; i++)
            {
                sim.Step();
                Assert.True(a.Hidden || !sprang);
                if (!a.Hidden) sprang = true;
                else Assert.False(sim.Context.IsVisibleTo("teamB", a));
            }
            Assert.True(sprang, "복병이 튀어나오지 않았습니다.");
            Assert.True(a.HasStatus(StatusIds.Surprise));
            Assert.Contains(b.Units, u => u.Confused);
            Assert.True(b.Morale < moraleBefore);
            // 기록 (R-32): 기습당한 군단에 ambushed 이벤트 (값 = 복병 군단)
            Assert.Contains(sim.Context.Log, e => e.Kind == EventKinds.Ambushed && e.CorpsId == "B" && e.Data == "A");
        }

        [Fact(DisplayName = "복병 혼란 스위치를 끄면 혼란은 없다")]
        public void AmbushConfusionFlagOff()
        {
            var sim = AmbushScene("{\"ambushConfusion\": false}");
            var a = sim.Context.FindCorps("A");
            Steps(sim, 205);
            sim.Issue(new BattleCommand { CorpsId = "B", Kind = CommandKinds.MoveTo, X = Fix.FromInt(12), Y = Fix.FromInt(36) });
            for (int i = 0; i < 600 && a.Hidden; i++) sim.Step();
            Assert.False(a.Hidden);
            Assert.DoesNotContain(sim.Context.FindCorps("B").Units, u => u.Confused);
        }

        [Fact(DisplayName = "간파가 복병을 드러낸다")]
        public void RevealExposesAmbush()
        {
            var sim = AmbushScene(bHasReveal: true);
            var a = sim.Context.FindCorps("A");
            Steps(sim, 205);
            Assert.True(a.Hidden);
            Assert.True(sim.Issue(Use("B", "reveal", 8, 18)));
            sim.Step();
            Assert.False(a.Hidden);
            Assert.True(sim.Context.IsVisibleTo("teamB", a));
            // 기록 (R-32): 간파를 쓴 군단이 숨은 적을 찾아냄
            Assert.Single(sim.Context.Log, e => e.Kind == EventKinds.HiddenFound && e.CorpsId == "B" && e.Data == "A");
        }

        [Fact(DisplayName = "복병 준비 중 움직이면 실패하고, 점수·횟수는 돌려주지 않는다 (R-12)")]
        public void AmbushFailsWhenMoving()
        {
            var sim = AmbushScene();
            var a = sim.Context.FindCorps("A");
            Steps(sim, 10);
            Assert.True(a.HasStatus(StatusIds.AmbushSetup));
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.MoveTo, X = Fix.FromInt(14), Y = Fix.FromInt(18) }));
            Steps(sim, 5);
            Assert.False(a.HasStatus(StatusIds.AmbushSetup));
            Steps(sim, 250);
            Assert.False(a.Hidden);
            Assert.Equal(0, a.SkillPoints.UsesLeft("ambush"));
        }

        [Fact(DisplayName = "복병은 숨는 지형(숲·수풀)에서만 쓸 수 있다")]
        public void AmbushNeedsCover()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, null, With(Corps("A", "teamA", "square"), intellect: 60, skills: "ambush"), With(Corps("B", "teamB", "square"))));
            Assert.False(sim.Issue(Use("A", "ambush")));
        }

        [Fact(DisplayName = "화살비: 궁병이 있어야 하고, 전조 뒤 범위 안 적이 피해를 입는다")]
        public void ArrowRain()
        {
            var archers = At(With(Mixed("A", "teamA", "square", ("center", "archer", 2000), ("vanguard", "infantry", 1000)), leadership: 50, skills: "arrowRain"), 12, 24, "N");
            var b = At(With(Corps("B", "teamB", "square")), 12, 10, "S"); // 보통 사거리(10+1) 밖
            var sim = new BattleSimulation(Setup("test_plain", 1, "{\"fogOfWar\": false}", archers, b));
            var bc = sim.Context.FindCorps("B");
            long before = bc.Troops;
            Assert.True(sim.Issue(Use("A", "arrowRain", 12, 11)));
            Steps(sim, 10); // 전조 1.5초 = 15틱
            Assert.Equal(before, bc.Troops);
            Steps(sim, 10);
            Assert.True(bc.Troops < before);

            var noArchers = new BattleSimulation(Setup("test_plain", 1, "{\"fogOfWar\": false}",
                At(With(Corps("A", "teamA", "square"), skills: "arrowRain"), 12, 26, "N"), At(With(Corps("B", "teamB", "square")), 12, 16, "S")));
            Assert.False(noArchers.Issue(Use("A", "arrowRain", 12, 16)));
        }

        [Fact(DisplayName = "사거리 밖 스킬은 다가간 뒤 쓴다")]
        public void ApproachThenCast()
        {
            var archers = With(Mixed("A", "teamA", "square", ("center", "archer", 2000), ("vanguard", "infantry", 1000)), skills: "arrowRain");
            var sim = new BattleSimulation(Setup("test_plain", 1, null, archers, With(Corps("B", "teamB", "square"))));
            var a = sim.Context.FindCorps("A");
            Assert.True(sim.Issue(Use("A", "arrowRain", 12, 6)));
            sim.Step();
            Assert.NotNull(a.PendingSkill);
            Assert.Equal(1, a.SkillPoints.UsesLeft("arrowRain")); // 받는 순간 냈다
            for (int i = 0; i < 400 && a.PendingSkill != null; i++) sim.Step();
            Assert.Null(a.PendingSkill);
            Assert.True(Game.Battle.Model.Geometry.Vec.DistanceSquared(a.Anchor, new Game.Battle.Model.Geometry.Vec(Fix.FromInt(12), Fix.FromInt(6))) <= Fix.FromInt(14 * 14));
        }

        [Fact(DisplayName = "고무: 자기와 가까운 아군 사기 회복, 먼 아군은 그대로")]
        public void Rally()
        {
            var a1 = At(With(Corps("A1", "teamA", "square"), leadership: 60, skills: "rally"), 6, 34, "N");
            var a2 = At(With(Corps("A2", "teamA", "square")), 10, 34, "N");
            var a3 = At(With(Corps("A3", "teamA", "square")), 22, 34, "N");
            a1.Role = null; a2.Role = null; a3.Role = null;
            var sim = new BattleSimulation(Setup("test_plain", 1, null, a1, a2, a3, With(Corps("B", "teamB", "square"))));
            Fix s1 = sim.Context.FindCorps("A1").Morale, s2 = sim.Context.FindCorps("A2").Morale, s3 = sim.Context.FindCorps("A3").Morale;
            Assert.True(sim.Issue(Use("A1", "rally")));
            sim.Step();
            Assert.Equal(s1 + Fix.FromInt(15), sim.Context.FindCorps("A1").Morale);
            Assert.Equal(s2 + Fix.FromInt(15), sim.Context.FindCorps("A2").Morale);
            Assert.Equal(s3, sim.Context.FindCorps("A3").Morale);
        }

        [Fact(DisplayName = "재정비: 혼란을 푼다")]
        public void Regroup()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, null, With(Corps("A", "teamA", "square"), leadership: 60, skills: "regroup"), With(Corps("B", "teamB", "square"))));
            var a = sim.Context.FindCorps("A");
            typeof(SubUnitState).GetProperty(nameof(SubUnitState.ConfusedTicks)).SetValue(a.Units[0], 40L);
            Assert.True(a.Units[0].Confused);
            Assert.True(sim.Issue(Use("A", "regroup")));
            sim.Step();
            Assert.False(a.Units[0].Confused);
        }

        [Fact(DisplayName = "돌격 함성: 일정 시간 공격 보정, 끝나면 사라진다")]
        public void WarCry()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, null, With(Corps("A", "teamA", "square"), might: 60, skills: "warCry"), With(Corps("B", "teamB", "square"))));
            var a = sim.Context.FindCorps("A");
            Assert.True(sim.Issue(Use("A", "warCry")));
            sim.Step();
            var buff = a.FindStatus("buff.warCry");
            Assert.NotNull(buff);
            Assert.Equal(Fix.FromInt(20), buff.AttackPp);
            Steps(sim, 80);
            Assert.False(a.HasStatus("buff.warCry"));
        }

        [Fact(DisplayName = "도발: 대상은 시전 군단을 공격하러 오고, 그동안 명령을 받지 않는다")]
        public void Taunt()
        {
            var a = At(With(Corps("A", "teamA", "square"), might: 60, skills: "taunt"), 12, 24, "N");
            var b = At(With(Corps("B", "teamB", "square")), 12, 16, "S");
            var sim = new BattleSimulation(Setup("test_plain", 1, "{\"fogOfWar\": false}", a, b));
            Assert.True(sim.Issue(Use("A", "taunt", target: "B")));
            sim.Step();
            var bc = sim.Context.FindCorps("B");
            Assert.True(bc.HasStatus(StatusIds.Taunted));
            Assert.Equal(OrderKinds.Attack, bc.Order.Kind);
            Assert.Equal("A", bc.Order.TargetCorpsId);
            Assert.False(sim.Issue(new BattleCommand { CorpsId = "B", Kind = CommandKinds.Hold }));
            Fix y0 = bc.Anchor.Y;
            Steps(sim, 20);
            Assert.True(bc.Anchor.Y > y0); // A 쪽(남쪽)으로 다가감
        }

        [Fact(DisplayName = "R-13 책략 점수는 사기 ≥ 기준일 때 회복하고 지략을 넘지 않는다. 스위치를 끄면 회복 없음")]
        public void R13Regen()
        {
            BattleSimulation Run(string flags)
            {
                var sim = new BattleSimulation(Setup("test_plain", 1, flags, With(Corps("A", "teamA", "square"), intellect: 60, skills: "reveal"), With(Corps("B", "teamB", "square"))));
                sim.Issue(Use("A", "reveal", 12, 6));
                Steps(sim, 1200);
                return sim;
            }
            var on = Run(null).Context.FindCorps("A");
            Assert.Equal(Fix.FromInt(60), on.SkillPoints.Points);
            var off = Run("{\"skillPointRegen\": false}").Context.FindCorps("A");
            Assert.Equal(Fix.FromInt(45), off.SkillPoints.Points);
        }

        [Fact(DisplayName = "R-29 스킬을 쓴 전투도 명령 기록으로 같은 결과가 나온다")]
        public void SkillReplay()
        {
            BattleSetup Make() => Setup("test_ambush", 9, null,
                At(With(Mixed("A", "teamA", "square", ("center", "archer", 1500), ("vanguard", "infantry", 1500), ("leftWing", "infantry", 1000)), intellect: 60, might: 60, skills: new[] { "ambush", "arrowRain", "warCry", "rally" }), 4, 18, "E"),
                Corps("B", "teamB", "square", "infantry", 1000, General("gb", intellect: 60)));
            var cmds = new System.Collections.Generic.List<BattleCommand>
            {
                Use("A", "ambush"),
                new BattleCommand { Tick = 300, CorpsId = "A", Kind = CommandKinds.UseSkill, SkillId = "warCry" },
                new BattleCommand { Tick = 320, CorpsId = "A", Kind = CommandKinds.UseSkill, SkillId = "rally" },
            };
            var r1 = BattleSimulation.Run(Make(), cmds);
            var r2 = BattleSimulation.Run(Make(), r1.Commands);
            Assert.Equal(r1.StateHash, r2.StateHash);
            Assert.True(r1.Commands.Count >= 1);
        }
    }
}
