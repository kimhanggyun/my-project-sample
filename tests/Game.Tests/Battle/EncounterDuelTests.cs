using System.Linq;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Contests;
using Game.Battle.Model.Events;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Foundation.Math;
using Game.Tests.Campaign;
using Xunit;

namespace Game.Tests.Battle
{
    /// <summary>R-89 마주침 일기토: 출진 때가 아니라 장수 군단이 전투 중 처음 마주칠 때.</summary>
    public class EncounterDuelTests
    {
        private static BattleSimulation Start(ulong seed, bool delegateAll)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = StartingKitService.CreateNewGame(Kit.D);
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = seed, Delegate = delegateAll });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            Assert.DoesNotContain(plan.Setup.Contests, c => c.ContestId == "duel"); // 출진 때 일기토 없음
            Assert.Contains("duel", plan.Setup.EncounterContests);
            foreach (var c in plan.Setup.Corps) c.General.Might = Fix.FromInt(70); // 능력치 차이 0 → 판정 확률 0.8
            return new BattleSimulation(plan.Setup);
        }

        private static DuelOffer RunUntilOffer(BattleSimulation sim)
        {
            for (int i = 0; i < 6000 && !sim.IsOver; i++)
            {
                if (sim.Context.Tick % 30 == 0)
                    foreach (var c in sim.Context.Corps.Where(c => c.Team == StoryFlow.PlayerTeam && c.InBattle && c.Order.Kind == OrderKinds.Hold))
                        sim.Issue(new BattleCommand { CorpsId = c.Id, Kind = CommandKinds.Advance });
                sim.Step();
                if (sim.Context.DuelOffers.Count > 0) return sim.Context.DuelOffers[0];
            }
            return null;
        }

        [Fact(DisplayName = "R-89 마주치면 플레이어 군단에 일기토 신청 (duelEncounter·duelOffered), 마주친 거리 안이고, 답이 없으면 기한 뒤 응함")]
        public void OfferThenAutoAccept()
        {
            for (ulong seed = 1; seed <= 8; seed++)
            {
                var sim = Start(seed, false);
                var offer = RunUntilOffer(sim);
                if (offer == null) continue;
                var a = sim.Context.FindCorps(offer.CorpsA);
                var b = sim.Context.FindCorps(offer.CorpsB);
                Assert.Equal(StoryFlow.PlayerTeam, b.Team);
                Assert.Contains(sim.Context.Log, e => e.Kind == EventKinds.DuelEncounter && e.CorpsId == b.Id && e.Data == a.Id);
                Assert.True((a.Anchor - b.Anchor).LengthSquared <= Fix.FromInt(64));
                for (int i = 0; i < 400 && !sim.IsOver && sim.Context.DuelOffers.Count > 0; i++) sim.Step();
                Assert.Contains(sim.Context.Log, e => e.Kind == EventKinds.DuelStarted && e.CorpsId == a.Id && e.Data == b.Id);
                return;
            }
            Assert.Fail("마주침 일기토가 일어난 시드가 없음");
        }

        [Fact(DisplayName = "R-89 거절하면 내 군단 사기 −5, 같은 군단에는 다시 신청이 오지 않는다")]
        public void Refuse()
        {
            for (ulong seed = 1; seed <= 8; seed++)
            {
                var sim = Start(seed, false);
                var offer = RunUntilOffer(sim);
                if (offer == null) continue;
                var b = sim.Context.FindCorps(offer.CorpsB);
                Fix before = b.Morale;
                Assert.True(sim.Issue(new BattleCommand { CorpsId = b.Id, Kind = CommandKinds.DuelAnswer, Mode = "refuse" }));
                sim.Step();
                Assert.Contains(sim.Context.Log, e => e.Kind == EventKinds.DuelRefused && e.CorpsId == b.Id);
                Assert.True(b.Morale <= before - Fix.FromInt(5) + Fix.FromInt(1)); // 같은 틱 사기 변화 여유
                for (int i = 0; i < 3000 && !sim.IsOver; i++) { sim.Step(); Assert.DoesNotContain(sim.Context.DuelOffers, o => o.CorpsB == b.Id || o.CorpsA == b.Id); }
                return;
            }
            Assert.Fail("마주침 일기토가 일어난 시드가 없음");
        }

        [Fact(DisplayName = "R-89 위임 군단은 묻지 않고 바로 일기토, 끝까지 돌려도 군단마다 마주침 일기토는 한 번")]
        public void DelegatedStartsAtOnce()
        {
            bool any = false;
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var sim = Start(seed, true);
                sim.RunToEnd();
                Assert.DoesNotContain(sim.Context.Log, e => e.Kind == EventKinds.DuelOffered);
                var encounters = sim.Context.Log.Where(e => e.Kind == EventKinds.DuelEncounter).ToList();
                var ids = encounters.SelectMany(e => new[] { e.CorpsId, e.Data }).ToList();
                Assert.Equal(ids.Count, ids.Distinct().Count());
                foreach (var e in encounters) Assert.Contains(sim.Context.Log, s => s.Kind == EventKinds.DuelStarted && s.CorpsId == e.Data && s.Data == e.CorpsId);
                any |= encounters.Count > 0;
            }
            Assert.True(any);
        }
    }
}
