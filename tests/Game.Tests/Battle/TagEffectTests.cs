using Game.Battle.Model.Simulation;
using Game.Foundation.Math;
using Newtonsoft.Json.Linq;
using Xunit;
using static Game.Tests.Battle.BattleKit;

namespace Game.Tests.Battle
{
    /// <summary>병종 태그 효과 (troops.md 2장, R-58): 정예(elite) 사기 안정, 근위(bodyguard) 장수 포로 압력 감소.</summary>
    public class TagEffectTests
    {
        [Fact(DisplayName = "R-58 데이터: 정예 태그 손실 사기 × 0.8, 근위 태그 포로 압력 × 0.5. 3단계 정예는 모두 elite, 근위병은 bodyguard")]
        public void Data()
        {
            var c = Defs.Combat;
            Assert.Equal(Fix.FromRatio(8, 10), c.TagMoraleLossMultiplier["elite"]);
            Assert.Equal(Fix.FromRatio(5, 10), c.TagCapturePressureMultiplier["bodyguard"]);
            foreach (var t in Defs.TroopTypes.Values)
                if (t.Tier == 3) Assert.True(t.HasTag("elite"), t.Id);
            Assert.True(Defs.TroopTypes["guard"].HasTag("bodyguard"));
        }

        private static BattleSetup WithTagEffects(BattleSetup s, string json)
        {
            if (json != null) s.Combat = Defs.ResolveCombat(JObject.Parse("{\"tagEffects\": " + json + "}"));
            return s;
        }

        [Fact(DisplayName = "R-58 태그 사기 배율: 그 태그 병력만큼 손실 사기 하락이 줄어든다 (전원이면 배율 그대로)")]
        public void MoraleLoss()
        {
            Fix MoraleAt(string effects, long tick)
            {
                var sim = new BattleSimulation(WithTagEffects(Setup("test_plain", 7, null, Corps("A", "teamA", "square", "infantry"), Corps("B", "teamB", "square", "spearman")), effects));
                while (!sim.IsOver && sim.Tick < tick) sim.Step();
                return sim.Context.FindCorps("A").Morale;
            }
            // 처음 사기가 떨어지는 틱을 찾는다
            var probe = new BattleSimulation(Setup("test_plain", 7, null, Corps("A", "teamA", "square", "infantry"), Corps("B", "teamB", "square", "spearman")));
            var a = probe.Context.FindCorps("A");
            Fix start = a.Morale;
            while (!probe.IsOver && a.Morale >= start) probe.Step();
            long t = probe.Tick;
            Fix baseDrop = start - MoraleAt(null, t);
            Fix halfDrop = start - MoraleAt("{\"infantry\": {\"moraleLossMultiplier\": 0.5}}", t);
            Assert.True(baseDrop > Fix.Zero);
            Assert.Equal(baseDrop / 2, halfDrop);
        }

        [Fact(DisplayName = "R-58 태그 포로 압력 배율: 포위된 군단에 그 태그 부대가 살아 있으면 포로 압력이 배율만큼 더디게 쌓인다")]
        public void CapturePressure()
        {
            BattleSetup Surrounded(string effects)
            {
                var a = Corps("A", "teamA", "square", troopsPerUnit: 100, general: General("ga", might: 1)).Player().At("12.5", "20.5", "N");
                var n = Corps("N", "teamB", "square", troopsPerUnit: 10).Player().At("12.5", "18.5", "S");
                var s = Corps("S", "teamB", "square", troopsPerUnit: 10).Player().At("12.5", "22.5", "N");
                var w = Corps("W", "teamB", "square", troopsPerUnit: 10).Player().At("10.5", "20.5", "E");
                var e = Corps("E", "teamB", "square", troopsPerUnit: 10).Player().At("14.5", "20.5", "W");
                foreach (var c in new[] { n, s, w, e }) c.Role = null;
                return WithTagEffects(Setup("test_plain", 3, "{\"fogOfWar\": false}", a, n, s, w, e), effects);
            }
            var baseSim = new BattleSimulation(Surrounded(null));
            var half = new BattleSimulation(Surrounded("{\"infantry\": {\"capturePressureMultiplier\": 0.5}}"));
            var ba = baseSim.Context.FindCorps("A");
            var ha = half.Context.FindCorps("A");
            while (!baseSim.IsOver && ba.CapturePressure == Fix.Zero) { baseSim.Step(); half.Step(); }
            Assert.True(ba.CapturePressure > Fix.Zero, "포위가 되지 않았습니다.");
            Assert.Equal(ba.CapturePressure / 2, ha.CapturePressure);
        }
    
        /// <summary>창벽병 군단 A(진형 formation)가 보병 B와 맞붙어 첫 피해 뒤 20틱까지 잃은 병력.</summary>
        private static long WallLosses(string formation, string wallJson)
        {
            var setup = WithTagEffects(Setup("test_plain", 11, null, Corps("A", "teamA", formation, "pikeWall"), Corps("B", "teamB", "square", "infantry")), wallJson);
            var sim = new BattleSimulation(setup);
            var a = sim.Context.FindCorps("A");
            long start = a.Troops;
            while (!sim.IsOver && a.Troops == start) sim.Step();
            for (int i = 0; i < 20 && !sim.IsOver; i++) sim.Step();
            return start - a.Troops;
        }

        [Fact(DisplayName = "R-58 창벽병: 방원·횡진에서는 정면 피해가 줄고 (−25%p), 다른 진형이면 정면 보정이 없다")]
        public void PikeWallFront()
        {
            Assert.True(Defs.TroopTypes["pikeWall"].HasTag("wall"));
            var w = Defs.Combat.TagDamageTaken["wall"];
            Assert.Equal(Fix.FromInt(-25), w.FrontPp);
            Assert.Equal(new[] { "circle", "line" }, w.Formations);
            const string noFront = "{\"wall\": {\"frontDamageTakenPp\": 0}}";
            Assert.True(WallLosses("circle", null) < WallLosses("circle", noFront));
            Assert.True(WallLosses("line", null) < WallLosses("line", noFront));
            Assert.Equal(WallLosses("column", null), WallLosses("column", noFront)); // 진형 조건 밖
        }
}
}
