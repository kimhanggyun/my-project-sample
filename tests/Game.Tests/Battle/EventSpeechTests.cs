using System.Linq;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Events;
using Game.Battle.Model.Simulation;
using Game.Foundation.Math;
using Xunit;
using static Game.Tests.Battle.BattleKit;

namespace Game.Tests.Battle
{
    /// <summary>결속의 전투 효과 (battle.md 2.19.1).</summary>
    public class CohesionBattleTests
    {
        [Fact(DisplayName = "결속이 높으면 혼란에 덜 걸리고, 걸려도 짧다")]
        public void CohesionResistsConfusion()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, null, Corps("A", "teamA", "square").Player(), Corps("B", "teamB", "square").Player()));
            var ctx = sim.Context;
            var unit = ctx.FindCorps("A").Units[0];
            int Hits(int cohesion)
            {
                typeof(SubUnitState).GetProperty(nameof(SubUnitState.Cohesion)).SetValue(unit, Fix.FromInt(cohesion));
                int n = 0;
                for (int i = 0; i < 400; i++)
                {
                    typeof(SubUnitState).GetProperty(nameof(SubUnitState.ConfusedTicks)).SetValue(unit, 0L);
                    if (Confusion.Apply(ctx, unit, 40)) n++;
                }
                return n;
            }
            Assert.Equal(400, Hits(0));
            int high = Hits(100);
            Assert.InRange(high, 200, 280); // 저항 40%
            typeof(SubUnitState).GetProperty(nameof(SubUnitState.ConfusedTicks)).SetValue(unit, 0L);
            while (!Confusion.Apply(ctx, unit, 40)) { }
            Assert.Equal(20, unit.ConfusedTicks); // 결속 100 → 시간 절반
        }
    }

    /// <summary>스텝 연출·전투 이벤트 기록 (ai.md 8장, battle.md 4.4).</summary>
    public class EventTests
    {
        private static BattleSetup WithReserve()
        {
            var a = Corps("A", "teamA", "square").Player();
            var b = Corps("B", "teamB", "square").Player();
            var r = Corps("R", "teamB", "square").At("12.5", "3.5", "S");
            r.Role = null;
            r.Reserve = true;
            var s = Setup("test_plain", 1, null, a, b, r);
            s.Events.Add(new EventSetup { Id = "hello", Trigger = new TriggerSetup { Type = "start" }, Actions = { new ActionSetup { Type = "dialogue", Key = "dialogue.step01.start" } } });
            s.Events.Add(new EventSetup { Id = "late", Trigger = new TriggerSetup { Type = "atTick", Tick = 50 }, Actions = { new ActionSetup { Type = "reinforce", CorpsId = "R" }, new ActionSetup { Type = "setAiStance", CorpsId = "R", Stance = "defensive" } } });
            return s;
        }

        [Fact(DisplayName = "연출: 시작 대사, 정해진 틱에 예비 군단 증원·AI 성향 바꾸기 (한 번만)")]
        public void ReserveAndDialogue()
        {
            var sim = new BattleSimulation(WithReserve());
            var r = sim.Context.FindCorps("R");
            Assert.Equal(CorpsStatus.Reserve, r.Status);
            Assert.False(r.InBattle);
            sim.Step();
            Assert.Contains(sim.Context.Log, e => e.Kind == EventKinds.Dialogue && e.Data == "dialogue.step01.start" && e.Tick == 0);
            for (int i = 0; i < 49; i++) sim.Step();
            Assert.Equal(CorpsStatus.Reserve, r.Status);
            sim.Step();
            Assert.Equal(CorpsStatus.Active, r.Status);
            Assert.Equal("defensive", r.AiStance);
            Assert.Single(sim.Context.Log, e => e.Kind == EventKinds.Reinforced);
            for (int i = 0; i < 20; i++) sim.Step();
            Assert.Single(sim.Context.Log, e => e.Kind == EventKinds.Reinforced);
        }

        [Fact(DisplayName = "연출: 군단 병력이 비율 이하가 되면 (예: 대장 위기 → 증원)")]
        public void TroopsBelowTrigger()
        {
            var s = WithReserve();
            s.Events.Clear();
            s.Corps[1].Controller = Controllers.Ai;
            s.Events.Add(new EventSetup { Id = "danger", Trigger = new TriggerSetup { Type = "corpsTroopsBelow", CorpsId = "B", Ratio = Fix.Parse("0.9") }, Actions = { new ActionSetup { Type = "reinforce", CorpsId = "R" } } });
            var sim = new BattleSimulation(s);
            var r = sim.Context.FindCorps("R");
            for (int i = 0; i < 3000 && !sim.IsOver && r.Status == CorpsStatus.Reserve; i++) sim.Step();
            Assert.Equal(CorpsStatus.Active, r.Status);
            Assert.True(Fix.FromInt(sim.Context.FindCorps("B").Troops) <= Fix.Parse("0.9") * sim.Context.FindCorps("B").StartTroops);
        }

        [Fact(DisplayName = "예비 대장은 아직 빠진 것이 아니다 (시작하자마자 지지 않음)")]
        public void ReserveCommanderNotDefeated()
        {
            var s = WithReserve();
            s.Corps[1].Role = null;
            s.Corps[2].Role = CorpsRoles.Commander;
            var sim = new BattleSimulation(s);
            for (int i = 0; i < 10; i++) sim.Step();
            Assert.False(sim.IsOver);
        }

        [Fact(DisplayName = "등록되지 않은 연출 조건·행동, 없는 AI 성향은 시작 전에 거절")]
        public void InvalidEvents()
        {
            var s = WithReserve();
            s.Events.Add(new EventSetup { Id = "bad", Trigger = new TriggerSetup { Type = "moonRise" } });
            Assert.Throws<System.ArgumentException>(() => new BattleSimulation(s));
            var s2 = WithReserve();
            s2.Events[1].Actions[1].Stance = "berserk";
            Assert.Throws<System.ArgumentException>(() => new BattleSimulation(s2));
        }

        [Fact(DisplayName = "전투 이벤트 기록: 스킬 사용, 퇴각 시작, 군량 부족·사기 붕괴 경고 (군단마다 한 번)")]
        public void EventLog()
        {
            var a = Corps("A", "teamA", "square", general: General("ga", might: 60), supply: 1500).Player();
            a.General.Skills.Add("warCry");
            var sim = new BattleSimulation(Setup("test_plain", 1, null, a, Corps("B", "teamB", "square")));
            sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.UseSkill, SkillId = "warCry" });
            var r = sim.RunToEnd();
            Assert.Contains(r.Events, e => e.Kind == EventKinds.SkillUsed && e.CorpsId == "A" && e.Data == "warCry");
            Assert.Single(r.Events, e => e.Kind == EventKinds.SupplyLow && e.CorpsId == "A");
            Assert.Contains(r.Events, e => e.Kind == EventKinds.RetreatStarted);
            Assert.True(r.Events.Count(e => e.Kind == EventKinds.MoraleCritical && e.CorpsId == "A") <= 1);
        }
    }

    /// <summary>장수 말풍선 공통 시스템 (battle.md 4.4).</summary>
    public class SpeechTests
    {
        private static BattleResult Run()
        {
            var a = Corps("A", "teamA", "square", general: General("ga", might: 60), supply: 1500).Player();
            a.General.Skills.Add("warCry");
            var sim = new BattleSimulation(Setup("test_plain", 1, null, a, Corps("B", "teamB", "square")));
            sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.UseSkill, SkillId = "warCry" });
            return sim.RunToEnd();
        }

        [Fact(DisplayName = "이벤트 → 말풍선: 스킬별 대사 키, 같은 틱은 경고가 외침보다 먼저, 같은 기록이면 같은 말풍선")]
        public void Plan()
        {
            var r = Run();
            var planner = new SpeechPlanner(Defs, TestData.Flags(Defs), Defs.Combat.TickSeconds, id => id == "B");
            var speeches = planner.Plan(r.Events);
            Assert.Contains(speeches, s => s.CorpsId == "A" && s.LineKey == "speech.skill.warCry" && s.Kind == "shout");
            Assert.Contains(speeches, s => s.LineKey == "speech.supplyLow" && s.Kind == "warning");
            for (int i = 1; i < speeches.Count; i++)
                Assert.True(speeches[i - 1].Tick < speeches[i].Tick || speeches[i - 1].Priority >= speeches[i].Priority);
            var again = new SpeechPlanner(Defs, TestData.Flags(Defs), Defs.Combat.TickSeconds, id => id == "B").Plan(Run().Events);
            Assert.Equal(speeches.Select(s => s.Tick + s.LineKey), again.Select(s => s.Tick + s.LineKey));
        }

        [Fact(DisplayName = "같은 군단·같은 상황은 최소 간격, 스위치(generalSpeech)를 끄면 없음")]
        public void IntervalAndFlag()
        {
            var planner = new SpeechPlanner(Defs, TestData.Flags(Defs), Defs.Combat.TickSeconds, null);
            BattleEvent E(long t) => (BattleEvent)typeof(BattleEvent).GetConstructor(System.Type.EmptyTypes).Invoke(null) is BattleEvent e
                ? Set(e, t) : null;
            Assert.NotNull(planner.Feed(E(0)));
            Assert.Null(planner.Feed(E(10)));   // 3초 = 30틱 안
            Assert.NotNull(planner.Feed(E(40)));
            var off = new SpeechPlanner(Defs, TestData.Flags(Defs, "{\"generalSpeech\": false}"), Defs.Combat.TickSeconds, null);
            Assert.Null(off.Feed(E(0)));
        }

        private static BattleEvent Set(BattleEvent e, long tick)
        {
            typeof(BattleEvent).GetProperty(nameof(BattleEvent.Tick)).SetValue(e, tick);
            typeof(BattleEvent).GetProperty(nameof(BattleEvent.Kind)).SetValue(e, EventKinds.RetreatStarted);
            typeof(BattleEvent).GetProperty(nameof(BattleEvent.CorpsId)).SetValue(e, "A");
            return e;
        }
    }
}
