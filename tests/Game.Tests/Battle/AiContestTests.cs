using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Ai;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Contests;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;
using Game.Foundation.Math;
using Game.Foundation.Random;
using Xunit;
using static Game.Tests.Battle.BattleKit;

namespace Game.Tests.Battle
{
    /// <summary>대결 엔진: 일기토·논쟁 (battle.md 2.7, verification R-21~R-23).</summary>
    public class ContestTests
    {
        private static ContestDef Duel => Defs.Contests["duel"];
        private static ContestDef Debate => Defs.Contests["debate"];

        private static ContestSession Pure(ContestDef def, int statA = 50, int statB = 50) =>
            new ContestSession(def, new ContestFighter("a", Fix.FromInt(statA), "calm"), new ContestFighter("b", Fix.FromInt(statB), "calm"), null);

        [Fact(DisplayName = "R-21 퇴각 vs 활쏘기: 체력 > 50이면 −50, 50 이하면 0 (쓰러짐)")]
        public void R21()
        {
            var s = Pure(Duel);
            Assert.True(s.Play("bow", "retreat"));
            Assert.Equal(Fix.FromInt(50), s.B.Resource);
            Assert.False(s.IsOver); // 퇴각 실패
            Assert.True(s.Play("bow", "retreat"));
            Assert.Equal(Fix.Zero, s.B.Resource);
            Assert.Equal(ContestEnds.Fall, s.Result.End);
            Assert.Equal("A", s.Result.Winner);
        }

        [Fact(DisplayName = "R-22 공격 vs 공격: 무력이 높은 쪽이 차이만큼 상대 체력을 깎음 (무작위 폭 적용 전)")]
        public void R22()
        {
            var s = Pure(Duel, 70, 60);
            s.Play("attack", "attack");
            Assert.Equal(Fix.FromInt(100), s.A.Resource);
            Assert.Equal(Fix.FromInt(90), s.B.Resource);
        }

        [Fact(DisplayName = "결과표: 회피는 공격을 피하고 기운 +20, 기운 모으기끼리 +30, 공격에 기운 모으기는 큰 피해")]
        public void Table()
        {
            var s = Pure(Duel);
            s.Play("attack", "evade");
            Assert.Equal(Fix.FromInt(20), s.B.Charge);
            Assert.Equal(Fix.FromInt(100), s.B.Resource);
            s.Play("charge", "charge");
            Assert.Equal(Fix.FromInt(30), s.A.Charge);
            Assert.Equal(Fix.FromInt(50), s.B.Charge);
            s.Play("attack", "charge");
            Assert.Equal(Fix.FromInt(100) - Duel.BaseDamage * Fix.Parse("1.5"), s.B.Resource);
        }

        [Fact(DisplayName = "필살은 기운이 가득 찼을 때만, 회피 불가, 쓰면 기운이 0. 활쏘기에 끊긴다")]
        public void Special()
        {
            var s = Pure(Duel);
            Assert.DoesNotContain("special", s.Available(s.A));
            Assert.False(s.Play("special", "attack"));
            s.Play("charge", "evade"); // A +50 (상대 회피), B 0
            s.Play("charge", "evade"); // A 100
            Assert.Contains("special", s.Available(s.A));
            s.Play("special", "evade");
            Assert.Equal(Fix.Zero, s.A.Charge);
            Assert.Equal(Fix.FromInt(100) - Duel.SpecialDamage, s.B.Resource);

            var t = Pure(Duel);
            t.Play("charge", "evade"); t.Play("charge", "evade");
            t.Play("special", "bow"); // 필살이 끊기고 A가 피해
            Assert.Equal(Fix.FromInt(100), t.B.Resource);
            Assert.Equal(Fix.FromInt(100) - Duel.BaseDamage, t.A.Resource);
        }

        [Fact(DisplayName = "퇴각 성공, 둘 다 물러나면 무승부, 최대 라운드면 체력 많은 쪽 승")]
        public void Ends()
        {
            var r = Pure(Duel);
            r.Play("retreat", "attack");
            Assert.Equal(ContestEnds.Retreat, r.Result.End);
            Assert.Equal("A", r.Result.Retreater);

            var w = Pure(Duel);
            w.Play("retreat", "retreat");
            Assert.Equal(ContestEnds.Draw, w.Result.End);

            var m = Pure(Duel, 55, 50);
            for (int i = 0; i < Duel.MaxRounds; i++) m.Play(i == 0 ? "attack" : "evade", i == 0 ? "attack" : "evade");
            Assert.Equal(ContestEnds.Win, m.Result.End);
            Assert.Equal("A", m.Result.Winner);
        }

        [Fact(DisplayName = "논쟁은 일기토와 같은 결과표, 이름과 능력치(지략)만 다르다")]
        public void DebateSharesTable()
        {
            Assert.Equal("intellect", Debate.Stat);
            var s = Pure(Debate, 80, 70);
            s.Play("strongPersuasion", "strongPersuasion");
            Assert.Equal(Fix.FromInt(90), s.B.Resource);
            s.Play("mockery", "withdraw");
            Assert.Equal(Fix.FromInt(40), s.B.Resource);
        }

        [Fact(DisplayName = "대결 AI: 고를 수 있는 행동만, 같은 시드면 같은 선택")]
        public void AiChooser()
        {
            List<string> Run(ulong seed)
            {
                var rng = new SeededRandom(seed);
                var s = new ContestSession(Duel, new ContestFighter("a", Fix.FromInt(60), "brave"), new ContestFighter("b", Fix.FromInt(58), "timid"), rng);
                var ai = new AiContestChooser(Defs.Ai, rng);
                var r = ContestRunner.Run(s, ai, ai);
                Assert.True(r.Rounds.Count >= 1 && r.Rounds.Count <= Duel.MaxRounds);
                return r.Rounds.Select(x => x.ActionA + "/" + x.ActionB).ToList();
            }
            Assert.Equal(Run(5), Run(5));
            for (ulong seed = 1; seed < 30; seed++) Run(seed); // 고를 수 없는 행동이면 Play가 false → 무한 반복이 된다
        }

        [Fact(DisplayName = "라운드 기록에 회심 표시 (화면 연출용): 맞은 쪽에만, 피해가 없으면 없다")]
        public void CritFlags()
        {
            int crits = 0;
            for (ulong seed = 1; seed < 200; seed++)
            {
                var s = new ContestSession(Duel, new ContestFighter("a", Fix.FromInt(60), "calm"), new ContestFighter("b", Fix.FromInt(60), "calm"), new SeededRandom(seed));
                s.Play("charge", "attack"); // A만 피해 (×1.5), B는 아무 일 없음
                var r = s.Rounds[0];
                Assert.False(r.CritOnB);
                if (r.CritOnA) crits++;
            }
            Assert.InRange(crits, 1, 199); // 회심 확률대로 가끔
        }

        [Fact(DisplayName = "대결 발생: 능력치 차이가 기준 밖이면 없다")]
        public void Trigger()
        {
            var rng = new SeededRandom(1);
            Assert.False(ContestTrigger.Occurs(Duel, Fix.FromInt(70), Fix.FromInt(50), "calm", "calm", rng));
            int hits = 0;
            for (int i = 0; i < 100; i++) if (ContestTrigger.Occurs(Duel, Fix.FromInt(52), Fix.FromInt(50), "calm", "calm", rng)) hits++;
            Assert.InRange(hits, 60, 95); // 확률 0.8
        }
    }

    /// <summary>대결 결과가 전투에 주는 영향 (battle.md 2.7.5, 2.7.6, R-23).</summary>
    public class ContestBattleTests
    {
        private static BattleSetup WithDuel(string flags, string contest, List<string> a, List<string> b)
        {
            var setup = Setup("test_plain", 1, flags, Corps("A", "teamA", "square", controller: Controllers.Player), Corps("B", "teamB", "square", controller: Controllers.Player));
            setup.Contests.Add(new ContestSetup { ContestId = contest, CorpsA = "A", CorpsB = "B", ChoicesA = a, ChoicesB = b });
            return setup;
        }

        private static readonly List<string> Bows = new List<string> { "bow", "bow" };
        private static readonly List<string> Retreats = new List<string> { "retreat", "retreat" };

        [Fact(DisplayName = "R-23 일기토 패배(체력 0) → 중상, 군단은 혼란 뒤 퇴각, 사기 하락")]
        public void DuelFall()
        {
            var sim = new BattleSimulation(WithDuel("{\"duelCapture\": false}", "duel", Bows, Retreats));
            var b = sim.Context.FindCorps("B");
            var a = sim.Context.FindCorps("A");
            Assert.All(b.Units, u => Assert.True(u.Confused));
            Assert.True(b.Morale < a.Morale);
            for (int i = 0; i < 40; i++) sim.Step();
            Assert.Equal(CorpsStatus.Retreating, b.Status);
            var r = sim.RunToEnd();
            Assert.Equal("heavy", r.GeneralInjuries["g_B"]);
            Assert.Equal(ContestEnds.Fall, r.Contests[0].End);
            Assert.Equal("teamA", r.Winner);
        }

        [Fact(DisplayName = "부상 스위치를 끄면 부상 없음, 일기토 스위치를 끄면 대결 없음")]
        public void Flags()
        {
            var r = BattleSimulation.Run(WithDuel("{\"generalInjury\": false}", "duel", Bows, Retreats));
            Assert.Empty(r.GeneralInjuries);
            var off = new BattleSimulation(WithDuel("{\"duel\": false}", "duel", Bows, Retreats));
            Assert.Empty(BattleSimulation.Run(WithDuel("{\"duel\": false}", "duel", Bows, Retreats)).Captures);
            Assert.DoesNotContain(off.Context.FindCorps("B").Units, u => u.Confused);
            Assert.Empty(off.RunToEnd().Contests);
        }

        [Fact(DisplayName = "일기토 퇴각 → 퇴각한 장수 군단 사기 저하")]
        public void DuelRetreatMorale()
        {
            var sim = new BattleSimulation(WithDuel(null, "duel", new List<string> { "attack" }, new List<string> { "retreat" }));
            Assert.True(sim.Context.FindCorps("B").Morale < sim.Context.FindCorps("A").Morale);
            Assert.Equal(CorpsStatus.Active, sim.Context.FindCorps("B").Status);
        }

        [Fact(DisplayName = "논쟁 패배 → 책략 점수 크게 감소 (혼란·도망 없음)")]
        public void DebateFall()
        {
            var sim = new BattleSimulation(WithDuel(null, "debate", new List<string> { "mockery", "mockery" }, new List<string> { "withdraw", "withdraw" }));
            var b = sim.Context.FindCorps("B");
            Assert.Equal(Fix.FromInt(50 - 40), b.SkillPoints.Points);
            Assert.Equal(Fix.FromInt(50 + 10), sim.Context.FindCorps("A").SkillPoints.Points);
            Assert.DoesNotContain(b.Units, u => u.Confused);
            Assert.False(b.HasStatus(StatusIds.Routing));
        }

        [Fact(DisplayName = "대결이 들어간 전투도 같은 입력이면 같은 결과 (AI 선택 포함)")]
        public void Deterministic()
        {
            BattleSetup Make()
            {
                var s = Setup("test_plain", 77, null, Corps("A", "teamA", "square"), Corps("B", "teamB", "square"));
                s.Contests.Add(new ContestSetup { ContestId = "duel", CorpsA = "A", CorpsB = "B" });
                return s;
            }
            var r1 = BattleSimulation.Run(Make());
            var r2 = BattleSimulation.Run(Make());
            Assert.Equal(r1.StateHash, r2.StateHash);
            Assert.Equal(r1.Contests[0].Rounds.Count, r2.Contests[0].Rounds.Count);
        }
    }

    /// <summary>군단 AI: 스킬 판단, 상성 대응, 매복형, 난이도 (ai.md 1차).</summary>
    public class CorpsAiTests
    {
        private static CorpsSetup AiCorps(string id, string team, string formation = "square", string troop = "infantry", string stance = AiStanceIds.Aggressive,
            string difficulty = "normal", int intellect = 60, int leadership = 60, int might = 60, params string[] skills)
        {
            var c = Corps(id, team, formation, troop, stance: stance, general: General("g_" + id, leadership, might, intellect));
            c.AiDifficulty = difficulty;
            c.General.Skills.AddRange(skills);
            return c;
        }

        private static void SetMorale(CorpsState c, Fix m) => typeof(CorpsState).GetProperty(nameof(CorpsState.Morale)).SetValue(c, m);

        [Fact(DisplayName = "AI 고무: 사기가 많이 떨어지면 쓴다")]
        public void Rally()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, null, AiCorps("A", "teamA", stance: AiStanceIds.Defensive, skills: "rally"), Corps("B", "teamB", "square", controller: Controllers.Player)));
            var a = sim.Context.FindCorps("A");
            SetMorale(a, a.StartMorale - Fix.FromInt(20));
            for (int i = 0; i < 100 && a.SkillPoints.UsesLeft("rally") == 2; i++) sim.Step();
            Assert.True(a.SkillPoints.UsesLeft("rally") < 2);
        }

        [Fact(DisplayName = "AI 재정비: 혼란에 빠지면 쓴다")]
        public void Regroup()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, null, AiCorps("A", "teamA", stance: AiStanceIds.Defensive, skills: "regroup"), Corps("B", "teamB", "square", controller: Controllers.Player)));
            var a = sim.Context.FindCorps("A");
            foreach (var u in a.Units) typeof(SubUnitState).GetProperty(nameof(SubUnitState.ConfusedTicks)).SetValue(u, 500L);
            for (int i = 0; i < 100 && a.Units[0].Confused && a.SkillPoints.UsesLeft("regroup") == 2; i++) sim.Step();
            Assert.True(a.SkillPoints.UsesLeft("regroup") < 2);
            Assert.DoesNotContain(a.Units, u => u.Confused);
        }

        [Fact(DisplayName = "AI 화살비: 사거리 안에 보이는 적이 오면 쓴다")]
        public void ArrowRain()
        {
            var a = AiCorps("A", "teamA", stance: AiStanceIds.Defensive, skills: "arrowRain");
            a.Units[1].TroopType = "archer";
            var sim = new BattleSimulation(Setup("test_plain", 1, null, a, Corps("B", "teamB", "square")));
            var ac = sim.Context.FindCorps("A");
            for (int i = 0; i < 1500 && !sim.IsOver && ac.SkillPoints.UsesLeft("arrowRain") == 2; i++) sim.Step();
            Assert.True(ac.SkillPoints.UsesLeft("arrowRain") < 2);
        }

        [Fact(DisplayName = "AI 상성 대응: 보통은 불리한 진형을 유리한 진형으로 바꾸고, 쉬움은 안 바꾼다")]
        public void CounterFormation()
        {
            string Final(string difficulty)
            {
                var a = AiCorps("A", "teamA", "square", difficulty: difficulty);
                a.KnownFormations.Add("fishScale");
                a.KnownFormations.Add("circle"); // 어린·방원 > 학익
                var b = Corps("B", "teamB", "craneWing", controller: Controllers.Player); // 학익 > 방진
                var sim = new BattleSimulation(Setup("test_plain", 1, "{\"fogOfWar\": false}", a, b));
                for (int i = 0; i < 300; i++) sim.Step();
                return sim.Context.FindCorps("A").Formation.Id;
            }
            Assert.True(Game.Data.Validation.DataValidator.Wins(Defs, Final("normal"), "craneWing"));
            Assert.Equal("square", Final("easy"));
        }

        [Fact(DisplayName = "AI 매복형: 숲에서 복병을 치고 숨어 있다가 기습한다")]
        public void Ambusher()
        {
            var a = AiCorps("A", "teamA", stance: AiStanceIds.Ambusher, skills: "ambush");
            a.X = Fix.Parse("8.5"); a.Y = Fix.Parse("18.5"); a.Facing = "E";
            var b = Corps("B", "teamB", "square", controller: Controllers.Player);
            var sim = new BattleSimulation(Setup("test_ambush", 2, null, a, b));
            var ac = sim.Context.FindCorps("A");
            for (int i = 0; i < 220; i++) sim.Step();
            Assert.True(ac.Hidden);
            sim.Issue(new BattleCommand { CorpsId = "B", Kind = CommandKinds.MoveTo, X = Fix.FromInt(12), Y = Fix.FromInt(36) });
            for (int i = 0; i < 600 && ac.Hidden; i++) sim.Step();
            Assert.False(ac.Hidden);
            Assert.Contains(sim.Context.FindCorps("B").Units, u => u.Confused);
        }

        [Fact(DisplayName = "AI도 같은 제한: 책략 점수가 모자라면 스킬을 못 쓴다")]
        public void SameLimits()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, null, AiCorps("A", "teamA", stance: AiStanceIds.Defensive, intellect: 5, skills: "rally"), Corps("B", "teamB", "square", controller: Controllers.Player)));
            var a = sim.Context.FindCorps("A");
            SetMorale(a, Fix.FromInt(10));
            for (int i = 0; i < 100; i++) sim.Step();
            Assert.Equal(2, a.SkillPoints.UsesLeft("rally")); // 비용 10 > 점수 5
        }

        [Fact(DisplayName = "없는 AI 난이도는 시작 전에 거절")]
        public void UnknownDifficulty()
        {
            var a = AiCorps("A", "teamA", difficulty: "nightmare");
            Assert.Throws<System.ArgumentException>(() => new BattleSimulation(Setup("test_plain", 1, null, a, Corps("B", "teamB", "square"))));
        }

        [Fact(DisplayName = "스킬을 가진 AI끼리의 전투도 끝까지 돌고 결정적이다")]
        public void AiVsAiWithSkills()
        {
            BattleSetup Make()
            {
                var a = AiCorps("A", "teamA", "fishScale", skills: new[] { "rally", "regroup", "warCry", "arrowRain", "reveal" });
                a.Units[4].TroopType = "archer"; a.Units[5].TroopType = "archer";
                var b = AiCorps("B", "teamB", "circle", stance: AiStanceIds.Defensive, skills: new[] { "rally", "taunt", "warCry" });
                b.KnownFormations.Add("craneWing");
                return Setup("test_ambush", 11, null, a, b);
            }
            var r1 = BattleSimulation.Run(Make());
            var r2 = BattleSimulation.Run(Make());
            Assert.Equal(r1.StateHash, r2.StateHash);
            Assert.Contains(r1.Corps, c => c.SkillPointsLeft < Fix.FromInt(60));
        }
    }
}
