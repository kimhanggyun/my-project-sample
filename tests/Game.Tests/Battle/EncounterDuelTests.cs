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
    /// <summary>R-89·R-91 마주침 일기토: 대장끼리 마주치면 '기회', 거는 쪽이 신청하고 받는 쪽이 응함/거절.</summary>
    public class EncounterDuelTests
    {
        /// <param name="playerMight">플레이어 장수 무력 (적은 70). 높으면 내가 거는 쪽.</param>
        private static BattleSimulation Start(ulong seed, bool delegateAll, int playerMight, string enemyTemperament = null, string playerTemperament = null)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = StartingKitService.CreateNewGame(Kit.D);
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = seed, Delegate = delegateAll });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            Assert.DoesNotContain(plan.Setup.Contests, c => c.ContestId == "duel"); // 출진 때 일기토 없음
            Assert.Contains("duel", plan.Setup.EncounterContests);
            foreach (var c in plan.Setup.Corps)
            {
                bool mine = c.Team == StoryFlow.PlayerTeam;
                c.General.Might = Fix.FromInt(mine ? playerMight : 70); // 능력치 차이 ≤ 3 → 판정 확률 0.8
                if (!mine && enemyTemperament != null) c.General.Temperament = enemyTemperament;
                if (mine && playerTemperament != null) c.General.Temperament = playerTemperament;
            }
            return new BattleSimulation(plan.Setup);
        }

        /// <summary>아군이 진군하며 마주침 이벤트가 날 때까지.</summary>
        private static bool RunUntil(BattleSimulation sim, System.Func<bool> done)
        {
            for (int i = 0; i < 6000 && !sim.IsOver; i++)
            {
                if (sim.Context.Tick % 30 == 0)
                    foreach (var c in sim.Context.Corps.Where(c => c.Team == StoryFlow.PlayerTeam && c.InBattle && c.Order.Kind == OrderKinds.Hold))
                        sim.Issue(new BattleCommand { CorpsId = c.Id, Kind = CommandKinds.Advance });
                sim.Step();
                if (done()) return true;
            }
            return false;
        }

        [Fact(DisplayName = "R-91 내가 무력이 높으면 기회(duelChance)를 묻는다: 넘어가면 일기토 없음, 기한이 지나도 넘어감")]
        public void ChancePass()
        {
            for (ulong seed = 1; seed <= 8; seed++)
            {
                var sim = Start(seed, false, 72);
                if (!RunUntil(sim, () => sim.Context.DuelChances.Count > 0)) continue;
                var ch = sim.Context.DuelChances[0];
                Assert.Equal(StoryFlow.PlayerTeam, sim.Context.FindCorps(ch.CorpsA).Team);
                Assert.Contains(sim.Context.Log, e => e.Kind == EventKinds.DuelEncounter && e.CorpsId == ch.CorpsA && e.Data == ch.CorpsB);
                Assert.True(sim.Issue(new BattleCommand { CorpsId = ch.CorpsA, Kind = CommandKinds.DuelChance, Mode = "pass" }));
                sim.Step();
                Assert.Contains(sim.Context.Log, e => e.Kind == EventKinds.DuelChancePassed && e.CorpsId == ch.CorpsA);
                Assert.Empty(sim.Context.DuelOffers);
                Assert.DoesNotContain(sim.Context.Log, e => e.Kind == EventKinds.DuelStarted);

                var sim2 = Start(seed, false, 72);
                Assert.True(RunUntil(sim2, () => sim2.Context.DuelChances.Count > 0));
                for (int i = 0; i < 400 && !sim2.IsOver && sim2.Context.DuelChances.Count > 0; i++) sim2.Step(); // 답 없음
                Assert.Contains(sim2.Context.Log, e => e.Kind == EventKinds.DuelChancePassed);
                Assert.DoesNotContain(sim2.Context.Log, e => e.Kind == EventKinds.DuelStarted);
                return;
            }
            Assert.Fail("마주침 기회가 생긴 시드가 없음");
        }

        [Fact(DisplayName = "R-91 신청하면 상대 AI가 성향 확률로 응함/거절: 오만은 늘 응하고, 거부 성향 표 밖 소심(0.1)은 대부분 거절")]
        public void ChallengeAiAnswers()
        {
            int started = 0, refused = 0;
            foreach (var temper in new[] { "arrogant", "timid" })
                for (ulong seed = 1; seed <= 8; seed++)
                {
                    var sim = Start(seed, false, 72, temper);
                    if (!RunUntil(sim, () => sim.Context.DuelChances.Count > 0)) continue;
                    var ch = sim.Context.DuelChances[0];
                    Assert.True(sim.Issue(new BattleCommand { CorpsId = ch.CorpsA, Kind = CommandKinds.DuelChance, Mode = "challenge" }));
                    sim.Step();
                    bool s = sim.Context.Log.Any(e => e.Kind == EventKinds.DuelStarted && e.CorpsId == ch.CorpsA);
                    bool r = sim.Context.Log.Any(e => e.Kind == EventKinds.DuelRefused && e.CorpsId == ch.CorpsB);
                    Assert.True(s ^ r);
                    if (temper == "arrogant") Assert.True(s);
                    if (s) started++; else refused++;
                }
            Assert.True(started > 0 && refused > 0);
        }

        [Fact(DisplayName = "R-91 거절 벌칙: 거절한 군단 사기 −10·10초 공격 −10%p (duelRefused), 같은 편 다른 군단 사기 −3")]
        public void RefusePenalty()
        {
            for (ulong seed = 1; seed <= 8; seed++)
            {
                var sim = Start(seed, false, 68); // 적이 거는 쪽 → 나에게 신청
                if (!RunUntil(sim, () => sim.Context.DuelOffers.Count > 0)) continue;
                var offer = sim.Context.DuelOffers[0];
                var b = sim.Context.FindCorps(offer.CorpsB);
                Assert.Equal(StoryFlow.PlayerTeam, b.Team);
                var allies = sim.Context.Corps.Where(c => c.Team == b.Team && c != b && c.InBattle).ToList();
                var before = allies.ToDictionary(c => c.Id, c => c.Morale);
                Fix mine = b.Morale;
                Assert.True(sim.Issue(new BattleCommand { CorpsId = b.Id, Kind = CommandKinds.DuelAnswer, Mode = "refuse" }));
                // 명령은 다음 틱에 반영: 사기 변화만 보려고 그 틱의 결과를 본다
                sim.Step();
                Assert.Contains(sim.Context.Log, e => e.Kind == EventKinds.DuelRefused && e.CorpsId == b.Id);
                var st = b.Statuses.Single(x => x.Id == StatusIds.DuelRefused);
                Assert.Equal(Fix.FromInt(-10), st.AttackPp);
                Assert.True(b.Morale <= mine - Fix.FromInt(9));
                foreach (var a in allies) Assert.True(a.Morale <= before[a.Id] - Fix.FromInt(2));
                return;
            }
            Assert.Fail("적이 신청한 시드가 없음");
        }

        [Fact(DisplayName = "R-91 받은 신청에 답이 없으면 무조건 응하지 않고 내 장수 성향 확률로 (시드마다 응함·거절이 갈린다)")]
        public void NoAnswerUsesTemperament()
        {
            int started = 0, refused = 0;
            for (ulong seed = 1; seed <= 16; seed++)
            {
                var sim = Start(seed, false, 68, null, "calm"); // 침착 0.5
                if (!RunUntil(sim, () => sim.Context.DuelOffers.Count > 0)) continue;
                for (int i = 0; i < 400 && !sim.IsOver && sim.Context.DuelOffers.Count > 0; i++) sim.Step();
                if (sim.Context.Log.Any(e => e.Kind == EventKinds.DuelStarted)) started++;
                if (sim.Context.Log.Any(e => e.Kind == EventKinds.DuelRefused)) refused++;
            }
            Assert.True(started > 0, "응한 판이 없음");
            Assert.True(refused > 0, "거절한 판이 없음");
        }

        [Fact(DisplayName = "R-91 위임 군단은 묻지 않는다 (기회·신청 창 없음), 마주침은 한 전투에 한 번")]
        public void DelegatedNoPrompts()
        {
            bool any = false;
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var sim = Start(seed, true, 70);
                sim.RunToEnd();
                Assert.DoesNotContain(sim.Context.Log, e => e.Kind == EventKinds.DuelOffered || e.Kind == EventKinds.DuelChance);
                int n = sim.Context.Log.Count(e => e.Kind == EventKinds.DuelEncounter);
                Assert.True(n <= 1);
                any |= n == 1;
            }
            Assert.True(any);
        }

        [Fact(DisplayName = "R-91 일기토 신청 스킬은 모든 장수가 배우지 않아도 가진다 (점수 0), 쓰면 60초 쿨타임")]
        public void ChallengeForAllWithCooldown()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = StartingKitService.CreateNewGame(Kit.D);
            Assert.True(new SkillBookService(Kit.D).Knows(state.Generals[0], "challenge"));
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 1 });
            plan.Setup.EncounterContests.Clear();
            var sim = new BattleSimulation(plan.Setup);
            var me = sim.Context.Corps.First(c => c.Team == StoryFlow.PlayerTeam);
            Assert.True(me.KnowsSkill("challenge"));
            var enemy = sim.Context.Corps.First(c => c.Team != StoryFlow.PlayerTeam);
            BattleCommand Cmd() => new BattleCommand { CorpsId = me.Id, Kind = CommandKinds.UseSkill, SkillId = "challenge", TargetCorpsId = enemy.Id };
            sim.Issue(new BattleCommand { CorpsId = me.Id, Kind = CommandKinds.Attack, TargetCorpsId = enemy.Id });
            for (int i = 0; i < 4000 && !sim.IsOver && (enemy.Anchor - me.Anchor).LengthSquared > Fix.FromInt(81); i++) sim.Step();
            Assert.True(sim.Issue(Cmd()));
            sim.Step();
            Assert.False(me.SkillReady("challenge", sim.Context.Tick));
            for (int i = 0; i < 300 && !sim.IsOver; i++) sim.Step(); // 30초
            Assert.False(me.SkillReady("challenge", sim.Context.Tick));
            for (int i = 0; i < 310 && !sim.IsOver; i++) sim.Step();
            Assert.True(sim.IsOver || me.SkillReady("challenge", sim.Context.Tick));
        }
    }
}
