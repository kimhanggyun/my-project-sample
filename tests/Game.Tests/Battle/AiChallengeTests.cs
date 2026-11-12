using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Contests;
using Game.Battle.Model.Control;
using Game.Battle.Model.Events;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Tests.Campaign;
using Xunit;

namespace Game.Tests.Battle
{
    /// <summary>R-79 적이 거는 일기토 (플레이어가 응함/거절) + AI 2차 스킬 판단.</summary>
    public class AiChallengeTests
    {
        private static BattleSimulation Start(string stepId, ulong seed, string difficulty = "normal")
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = StartingKitService.CreateNewGame(Kit.D);
            state.Difficulty = difficulty;
            foreach (var id in Kit.D.StepIndex.Order) { if (id == stepId) break; state.ClearedSteps.Add(id); }
            var plan = flow.Prepare(state, new SortieRequest { StepId = stepId, Seed = seed });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            return new BattleSimulation(plan.Setup);
        }

        /// <summary>전군 진군(위임 아님)으로 돌려 적이 신청할 때까지. 신청을 받은 시뮬레이션을 돌려준다.</summary>
        private static (BattleSimulation sim, DuelOffer offer) UntilOffer()
        {
            for (ulong seed = 1; seed < 40; seed++)
            {
                var sim = Start("step05", seed, "hard");
                var ctl = new BattleControl(sim, StoryFlow.PlayerTeam);
                ctl.AdvanceAll();
                for (int i = 0; i < 4000 && !sim.IsOver; i++)
                {
                    sim.Step();
                    if (sim.Context.DuelOffers.Count > 0) return (sim, sim.Context.DuelOffers[0]);
                }
            }
            Assert.Fail("적이 일기토를 건 시드가 없음");
            return (null, null);
        }

        [Fact(DisplayName = "R-79 어려움에서 AI 장수(무력 55)는 접전 중 무력이 10 이상 낮은 플레이어 장수에게만 일기토를 걸고, 직접 지휘하는 군단에는 신청이 남는다 (이벤트 duelOffered)")]
        public void AiChallengesWeakerPlayerGeneral()
        {
            var (sim, offer) = UntilOffer();
            var a = sim.Context.FindCorps(offer.CorpsA);
            var b = sim.Context.FindCorps(offer.CorpsB);
            Assert.Equal("enemy_general_01", a.Setup.General.Id);
            Assert.True(a.Setup.General.Stat("might") >= b.Setup.General.Stat("might") + Kit.D.Ai.ChallengeEdge);
            Assert.Equal(Controllers.Player, b.Setup.Controller);
            Assert.Contains(sim.Context.Log, e => e.Kind == EventKinds.DuelOffered && e.CorpsId == b.Id && e.Data == a.Id);
            Assert.Null(b.DuelWith); // 답하기 전에는 계속 싸운다
            Assert.True(sim.Issue(new BattleCommand { CorpsId = b.Id, Kind = CommandKinds.Advance }));
        }

        [Fact(DisplayName = "R-79 응함: 받은 쪽(B)이 대결 기록을 넣고, 결과는 화면 계산(B 쪽 Simulate)과 같다")]
        public void AcceptThenPlayAsB()
        {
            var (sim, offer) = UntilOffer();
            var b = sim.Context.FindCorps(offer.CorpsB);
            Assert.True(sim.Issue(new BattleCommand { CorpsId = b.Id, Kind = CommandKinds.DuelAnswer, Mode = "accept" }));
            sim.Step();
            Assert.Empty(sim.Context.DuelOffers);
            var d = InBattleDuels.PendingFor(sim.Context, b.Id);
            Assert.NotNull(d);
            Assert.Equal(b.Id, d.CorpsB);
            // 화면 (BattleDuelPlay): 플레이어(B)를 왼쪽(A)으로 보여 주고, 명령은 B 군단으로
            var play = new BattleDuelPlay(sim, d);
            Assert.False(play.PlayerIsA);
            Assert.Equal(b.Setup.General.Id, play.GeneralA);
            while (!play.IsOver) Assert.True(play.Choose(play.Available()[0]));
            var shown = play.Result;
            var cmd = play.Command();
            Assert.Equal(b.Id, cmd.CorpsId);
            Assert.True(sim.Issue(cmd));
            sim.Step();
            Assert.NotNull(d.Result);
            // 전투 결과(A = 건 적)를 뒤집으면 화면이 보여 준 것과 같다
            var mirrored = d.Result.Mirror();
            Assert.Equal((shown.End, shown.Winner, shown.Rounds.Count), (mirrored.End, mirrored.Winner, mirrored.Rounds.Count));
            Assert.Equal(shown.Rounds.Last().ResourceA, mirrored.Rounds.Last().ResourceA);
        }

        [Fact(DisplayName = "R-79 거절: 받은 군단 사기 + refuseMorale(−10), 이벤트 duelRefused, 대결 없음")]
        public void Refuse()
        {
            var (sim, offer) = UntilOffer();
            var b = sim.Context.FindCorps(offer.CorpsB);
            Assert.True(sim.Issue(new BattleCommand { CorpsId = b.Id, Kind = CommandKinds.DuelAnswer, Mode = "refuse" }));
            var before = b.Morale;
            int logBefore = sim.Context.Log.Count;
            sim.Step();
            Assert.Contains(sim.Context.Log.Skip(logBefore), e => e.Kind == EventKinds.DuelRefused && e.CorpsId == b.Id);
            Assert.Null(b.DuelWith);
            Assert.Empty(sim.Context.DuelOffers);
            Assert.True(b.Morale < before);
        }

        [Fact(DisplayName = "R-79 답이 없으면 offerSec(10초) 뒤 성향 확률로 정해지고 신청은 사라진다")]
        public void NoAnswerDecidesAfterDeadline()
        {
            var (sim, offer) = UntilOffer();
            for (int i = 0; i < 2000 && sim.Context.DuelOffers.Count > 0 && !sim.IsOver; i++) sim.Step();
            Assert.Empty(sim.Context.DuelOffers);
            Assert.True(sim.Context.Tick >= offer.DeadlineTick || sim.IsOver);
            Assert.Contains(sim.Context.Log, e => (e.Kind == EventKinds.DuelStarted && e.CorpsId == offer.CorpsA) || (e.Kind == EventKinds.DuelRefused && e.CorpsId == offer.CorpsB) || sim.IsOver);
            // 답할 신청이 없으면 duelAnswer는 받지 않는다
            Assert.False(sim.Issue(new BattleCommand { CorpsId = offer.CorpsB, Kind = CommandKinds.DuelAnswer, Mode = "accept" }));
        }

        [Fact(DisplayName = "R-79 AI 2차 스킬: 적 장수들이 혼란·집중 사격·철벽을 실제로 쓰고, 일기토는 어려움에서만 건다 (위임 출진 여러 스텝)")]
        public void EnemyUsesTier2Skills()
        {
            var used = Used("normal");
            Assert.Contains("confusion", used);
            Assert.DoesNotContain("challenge", used);
            Assert.True(used.Contains("focusFire") || used.Contains("holdTheLine"), string.Join(",", used));
            Assert.Contains("challenge", Used("hard"));
        }

        private static HashSet<string> Used(string difficulty)
        {
            var used = new HashSet<string>();
            foreach (var step in new[] { "step03", "step05", "step06", "step07", "step09" })
                for (ulong seed = 1; seed <= 6; seed++)
                {
                    var sim = Start(step, seed, difficulty);
                    var ctl = new BattleControl(sim, StoryFlow.PlayerTeam);
                    foreach (var c in ctl.OwnCorps().ToList()) sim.Issue(new BattleCommand { CorpsId = c.Id, Kind = CommandKinds.Delegate, Mode = "on" });
                    for (int i = 0; i < 20000 && !sim.IsOver; i++) sim.Step();
                    foreach (var e in sim.Context.Log)
                        if (e.Kind == EventKinds.SkillUsed && sim.Context.FindCorps(e.CorpsId)?.Team != StoryFlow.PlayerTeam) used.Add(e.Data);
                }
            return used;
        }
    }
}
