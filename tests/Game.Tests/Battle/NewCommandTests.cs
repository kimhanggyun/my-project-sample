using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Control;
using Game.Battle.Model.Events;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Flow;
using Game.Foundation.Math;
using Game.Tests.Campaign;
using Xunit;

namespace Game.Tests.Battle
{
    /// <summary>R-103 새 명령 (19 시안 명령 줄): 돌격 · 사격만 · 유인 · 은밀 · 지키기 · 엄호, 여러 군단 일제 공격 · 집결, 경로.</summary>
    public class NewCommandTests
    {
        /// <summary>직접 지휘 step01, 안개 끔 (적이 늘 보이게).</summary>
        private static (BattleSimulation sim, BattleControl ctl) Make()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var plan = flow.Prepare(Kit.Veteran(), new SortieRequest { StepId = "step01", Seed = 1 });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            plan.Setup.Flags = Kit.D.ResolveFlags(Newtonsoft.Json.Linq.JObject.Parse("{\"fogOfWar\": false}"));
            var sim = new BattleSimulation(plan.Setup);
            return (sim, new BattleControl(sim, StoryFlow.PlayerTeam));
        }

        private static CorpsState Enemy(BattleSimulation sim) => sim.Context.Corps.First(c => c.Team == StoryFlow.EnemyTeam && c.InBattle);

        private static bool HasRanged(CorpsState c) => c.Units.Any(u => u.Alive && u.Troop.HasTag("ranged"));

        private static Fix Moved(CorpsState c, Vec from) => Vec.DistanceSquared(c.Anchor, from);

        [Fact(DisplayName = "R-103 명령 검사: 사격만은 원거리가 있어야, 유인 · 지키기는 전장 안 지점, 엄호는 다른 아군 군단만, 은밀 이동은 받는다")]
        public void Validation()
        {
            var (sim, ctl) = Make();
            var enemy = Enemy(sim);
            var own = ctl.OwnCorps().Where(ctl.CanCommand).ToList();
            var melee = own.FirstOrDefault(c => !HasRanged(c));
            var ranged = own.FirstOrDefault(HasRanged);
            if (melee != null) { ctl.Select(melee.Id); Assert.False(ctl.AttackWith(enemy.Id, AttackModes.Standoff)); }
            if (ranged != null) { ctl.Select(ranged.Id); Assert.True(ctl.AttackWith(enemy.Id, AttackModes.Standoff)); }
            ctl.Select(own[0].Id);
            Assert.True(ctl.AttackWith(enemy.Id, AttackModes.Charge));
            Assert.False(ctl.Lure(enemy.Id, new Vec(Fix.FromInt(-5), Fix.FromInt(-5))));
            Assert.True(ctl.Lure(enemy.Id, own[0].Anchor));
            Assert.False(ctl.Guard(new Vec(Fix.FromInt(9999), Fix.Zero)));
            Assert.True(ctl.Guard(own[0].Anchor));
            Assert.False(ctl.Escort(own[0].Id));   // 자기 자신
            Assert.False(ctl.Escort(enemy.Id));    // 적
            Assert.True(ctl.Escort(own[1].Id));
            Assert.True(ctl.MoveTo(own[0].Anchor + new Vec(Fix.FromInt(3), Fix.Zero), MoveModes.Stealth));
            Assert.False(sim.Issue(new BattleCommand { CorpsId = own[0].Id, Kind = CommandKinds.Attack, TargetCorpsId = enemy.Id, Mode = "nope" }));
        }

        [Fact(DisplayName = "R-103 돌격: 보통 공격보다 빨리 다가가고, 첫 접전에 상대 사기를 chargeShockMorale 깎은 뒤 보통 공격으로")]
        public void Charge()
        {
            Fix Run(string mode, out bool shocked, out string after)
            {
                var (sim, ctl) = Make();
                var me = ctl.OwnCorps().First(ctl.CanCommand);
                var enemy = Enemy(sim);
                ctl.Select(me.Id);
                Assert.True(ctl.AttackWith(enemy.Id, mode));
                var start = me.Anchor;
                for (int i = 0; i < 20; i++) sim.Step();
                var d = Moved(me, start);
                shocked = false;
                for (int i = 0; i < 3000 && !sim.IsOver && !shocked; i++) { sim.Step(); shocked = sim.Context.Log.Any(e => e.Kind == EventKinds.Charge); }
                after = me.Order.Mode;
                return d;
            }
            var normal = Run(AttackModes.Normal, out bool s0, out _);
            var charge = Run(AttackModes.Charge, out bool s1, out string after);
            Assert.True(charge > normal, "돌격이 더 빨리 다가가야 합니다.");
            Assert.False(s0);
            Assert.True(s1, "돌격이 접전에 들지 못했습니다.");
            Assert.Equal(MoveModes.Normal, after);
        }

        [Fact(DisplayName = "R-103 은밀: 보통 이동보다 느리다 (stealthMovePct)")]
        public void StealthSlower()
        {
            Fix Run(string mode)
            {
                var (sim, ctl) = Make();
                var me = ctl.OwnCorps().First(ctl.CanCommand);
                ctl.Select(me.Id);
                var to = sim.Context.ClampToField(me.Anchor + sim.Context.Forward(me) * Fix.FromInt(20));
                Assert.True(ctl.MoveTo(to, mode));
                var start = me.Anchor;
                for (int i = 0; i < 20; i++) sim.Step();
                return Moved(me, start);
            }
            Assert.True(Run(MoveModes.Stealth) < Run(MoveModes.Normal));
            Assert.Equal(Fix.FromInt(-30), Kit.D.Combat.StealthMovePct);
            Assert.Equal(Fix.FromInt(50), Kit.D.Combat.StealthSightPct);
        }

        [Fact(DisplayName = "R-103 지키기: 둘레에 적이 없으면 지점에 머문다 / 엄호: 아군이 가면 따라간다")]
        public void GuardAndEscort()
        {
            var (sim, ctl) = Make();
            var own = ctl.OwnCorps().Where(ctl.CanCommand).ToList();
            var guard = own[0];
            ctl.Select(guard.Id);
            Assert.True(ctl.Guard(guard.Anchor));
            var leader = own[1];
            var follower = own[2];
            ctl.Select(leader.Id);
            var far = sim.Context.ClampToField(leader.Anchor - sim.Context.Forward(leader) * Fix.FromInt(15));
            Assert.True(ctl.MoveTo(far, MoveModes.Forced));
            ctl.Select(follower.Id);
            Assert.True(ctl.Escort(leader.Id));
            var g0 = guard.Anchor;
            var f0 = Vec.DistanceSquared(follower.Anchor, far);
            for (int i = 0; i < 40; i++) sim.Step();
            Assert.True(Vec.DistanceSquared(guard.Anchor, g0) <= Fix.OneValue, "지키기는 적이 없으면 제자리");
            Assert.Equal(HoldModes.Guard, guard.Order.Mode);
            Assert.True(Vec.DistanceSquared(follower.Anchor, far) < f0, "엄호는 아군을 따라간다");
        }

        [Fact(DisplayName = "R-103 유인: 맞붙으면 정한 지점으로 강행해 물러난다 (사건 lure)")]
        public void Lure()
        {
            var (sim, ctl) = Make();
            var me = ctl.OwnCorps().First(ctl.CanCommand);
            var enemy = Enemy(sim);
            ctl.Select(me.Id);
            var home = me.Anchor;
            Assert.True(ctl.Lure(enemy.Id, home));
            bool lured = false;
            for (int i = 0; i < 3000 && !sim.IsOver && !lured; i++) { sim.Step(); lured = sim.Context.Log.Any(e => e.Kind == EventKinds.Lure && e.CorpsId == me.Id); }
            Assert.True(lured, "유인이 접전에 들지 못했습니다.");
            Assert.Equal(OrderKinds.MoveTo, me.Order.Kind);
            Assert.Equal(MoveModes.Forced, me.Order.Mode);
            Assert.Equal(home, me.Order.Point);
        }

        [Fact(DisplayName = "R-103 여러 군단: 일제 공격 = 같은 적 협공 대기, 집결 = 한 지점 둘레 이동, 경로 = 첫 점 + 예약 + 끝 적 공격")]
        public void MultiAndPath()
        {
            var (sim, ctl) = Make();
            var ids = ctl.OwnCorps().Where(ctl.CanCommand).Select(c => c.Id).ToList();
            var enemy = Enemy(sim);
            Assert.Equal(ids.Count, ctl.Volley(ids, enemy.Id));
            var volley = sim.CommandLog.TakeLast(ids.Count).ToList();
            Assert.Equal(ids, volley.Select(c => c.CorpsId));
            Assert.All(volley, c => { Assert.Equal(CommandKinds.Hold, c.Kind); Assert.Equal(HoldModes.Coordinate, c.Mode); Assert.Equal(enemy.Id, c.TargetCorpsId); });
            var center = sim.Context.FindCorps(ids[0]).Anchor;
            Assert.Equal(ids.Count, ctl.Rally(ids, center));
            var rallies = sim.CommandLog.Where(c => c.Kind == CommandKinds.MoveTo).TakeLast(ids.Count).ToList();
            Assert.Equal(ids.Count, rallies.Select(r => (r.X, r.Y)).Distinct().Count()); // 겹치지 않게 나란히
            Assert.All(rallies, r => Assert.True(Vec.DistanceSquared(new Vec(r.X, r.Y), center) <= Fix.FromInt(100)));

            ctl.Select(ids[0]);
            var me = sim.Context.FindCorps(ids[0]);
            var f = sim.Context.Forward(me);
            var pts = new List<Vec> { me.Anchor + f * Fix.FromInt(3), me.Anchor + f * Fix.FromInt(6) + f.RightOf() * Fix.FromInt(4), me.Anchor + f * Fix.FromInt(9) };
            int before = sim.CommandLog.Count;
            Assert.Equal(4, ctl.Path(pts, MoveModes.Stealth, enemy.Id));
            var issued = sim.CommandLog.Skip(before).ToList();
            Assert.Equal(new[] { false, true, true, true }, issued.Select(c => c.Queued));
            Assert.All(issued.Take(3), c => Assert.Equal(MoveModes.Stealth, c.Mode));
            Assert.Equal(CommandKinds.Attack, issued[3].Kind);
            sim.Step(); // 명령은 다음 틱에 들어간다
            Assert.Equal(3, me.Queue.Count);
        }

        [Fact(DisplayName = "R-103 재현: 새 명령을 섞은 전투도 명령 기록으로 다시 돌리면 같은 결과")]
        public void Replay()
        {
            var (sim, ctl) = Make();
            var own = ctl.OwnCorps().Where(ctl.CanCommand).ToList();
            var enemy = Enemy(sim);
            ctl.Select(own[0].Id); ctl.AttackWith(enemy.Id, AttackModes.Charge);
            ctl.Select(own[1].Id); ctl.Escort(own[0].Id);
            ctl.Select(own[2].Id); ctl.Guard(own[2].Anchor);
            while (!sim.IsOver) sim.Step();
            var again = new BattleSimulation(sim.Context.Setup);
            int next = 0;
            var log = sim.CommandLog.ToList();
            while (!again.IsOver)
            {
                while (next < log.Count && log[next].Tick <= again.Context.Tick) again.Issue(log[next++].Clone());
                again.Step();
            }
            Assert.Equal(sim.Result.StateHash, again.Result.StateHash);
        }
    }
}
