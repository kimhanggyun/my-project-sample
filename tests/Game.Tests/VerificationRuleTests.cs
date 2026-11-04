using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Damage;
using Game.Battle.Model.Display;
using Game.Battle.Model.Skills;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Campaign.Troops;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Data.Rules;
using Game.Data.Validation;
using Game.Economy;
using Game.Foundation.Math;
using Game.Foundation.Random;
using Game.Online;
using Newtonsoft.Json.Linq;
using Xunit;
using static Game.Tests.TestData;

namespace Game.Tests
{
    /// <summary>
    /// docs/design/verification.md 의 규칙 예시(R). 현재 데이터 값으로 계산하므로 밸런스를 바꿔도 깨지지 않고,
    /// 계산 방식이 틀렸을 때만 실패한다.
    /// </summary>
    public class VerificationRuleTests
    {
        private static readonly DefinitionSet D = Real();

        [Fact(DisplayName = "R-1 보정은 더한 뒤 상·하한으로 자르고 한 번만 곱한다")]
        public void R1()
        {
            var b = D.Balance;
            var calc = new DamageCalculator(b, Flags(D));
            var mods = new[] { new DamageModifier("x", b.DamageModifierMaxPp), new DamageModifier("y", F(30)) };
            var r = calc.Calculate(F(1000), mods);
            Assert.Equal(b.DamageModifierMaxPp + F(30), r.TotalPp);
            Assert.Equal(b.DamageModifierMaxPp, r.ClampedPp);
            Assert.Equal(F(1000) * (Fix.Hundred + b.DamageModifierMaxPp) / Fix.Hundred, r.Final);
        }

        [Fact(DisplayName = "R-1 꺼진 스위치의 보정은 0으로 본다")]
        public void R1_FlagOff()
        {
            var calc = new DamageCalculator(D.Balance, Flags(D, "{\"flankBonus\": false}"));
            var r = calc.Calculate(F(100), new[] { new DamageModifier("flank", F(15), "flankBonus"), new DamageModifier("terrain", F(5), "terrainCombat") });
            Assert.Equal(F(5), r.TotalPp);
            Assert.Single(r.Applied);
        }

        [Fact(DisplayName = "R-2 보정 내역의 합 = 잘리기 전 합계")]
        public void R2()
        {
            var r = new DamageCalculator(D.Balance, Flags(D)).Calculate(F(100),
                new[] { new DamageModifier("a", F(70)), new DamageModifier("b", F(60)), new DamageModifier("c", F(-5)) });
            Fix sum = Fix.Zero;
            foreach (var m in r.Applied) sum += m.Pp;
            Assert.Equal(r.TotalPp, sum);
        }

        [Fact(DisplayName = "R-4 병력 한도는 통솔 범위 밖이면 양 끝으로 고정된다")]
        public void R4()
        {
            var b = D.Balance;
            Assert.Equal(b.TroopCapMin, TroopCapCalculator.Cap(b, b.TroopCapMinLeadership));
            Assert.Equal(b.TroopCapMax, TroopCapCalculator.Cap(b, b.TroopCapMaxLeadership));
            Assert.Equal(b.TroopCapMin, TroopCapCalculator.Cap(b, F(-10)));
            Assert.Equal(b.TroopCapMax, TroopCapCalculator.Cap(b, F(500)));
        }

        [Fact(DisplayName = "R-5/N-6 군단 병력 합이 장수 한도를 넘으면 편성 불가 (12,500 > 12,424 → 76 초과)")]
        public void R5()
        {
            var state = new CampaignState();
            state.Generals.Add(new GeneralState { GeneralId = "general_c" }); // 통솔 50 (테스트 데이터)
            long[] troops = { 3000, 3000, 3000, 2000, 1500 };
            var comp = new CorpsComposition { GeneralId = "general_c", Formation = "square", Role = "rear" };
            for (int i = 0; i < troops.Length; i++)
            {
                state.Units.Add(new UnitState { UnitId = "t" + i, TroopType = "infantry", Troops = troops[i] });
                comp.Slots[SlotIds.All[i]] = "t" + i;
            }
            long cap = TroopCapCalculator.Cap(D.Balance, D.Generals["general_c"].Stats.Get("leadership"));
            var errors = CorpsValidator.Validate(D, state, new[] { comp });
            if (cap < 12500) Assert.Contains(errors, e => e.Contains((12500 - cap) + " 초과"));
            else Assert.Empty(errors);
        }

        [Fact(DisplayName = "R-5 한 부대는 한 군단에만")]
        public void R5_UnitOnce()
        {
            var state = new CampaignState();
            state.Generals.Add(new GeneralState { GeneralId = "general_a" });
            state.Generals.Add(new GeneralState { GeneralId = "general_b" });
            state.Units.Add(new UnitState { UnitId = "x", TroopType = "infantry", Troops = 100 });
            var c1 = new CorpsComposition { GeneralId = "general_a", Slots = { ["vanguard"] = "x" } };
            var c2 = new CorpsComposition { GeneralId = "general_b", Slots = { ["vanguard"] = "x" } };
            Assert.Contains(CorpsValidator.Validate(D, state, new[] { c1, c2 }), e => e.Contains("두 군단"));
        }

        [Fact(DisplayName = "R-6/R-7 군량: 출진 자동 계산 = 출진 분량만큼의 전투 소비")]
        public void R6_R7()
        {
            var b = D.Balance;
            long daily = SupplyCalculator.DailyConsumption(new[] { (1234L, 1L), (777L, 2L) });
            Assert.Equal((1234 + 777 * 2) * b.SupplyDaysPerMinute, SupplyCalculator.BattleConsumption(b, daily, F(1)));
            Assert.Equal(SupplyCalculator.BattleConsumption(b, daily, b.SortieSupplyMinutes), SupplyCalculator.AutoSortieSupply(b, daily));
        }

        [Fact(DisplayName = "R-8 궤멸·포로 군단은 군량 전량 몰수, 가져감 + 몰수 = 남은 군량")]
        public void R8()
        {
            Assert.Equal((0L, 90000L), SupplyCalculator.RetreatSplit(90000, 3000, 9000, true));
            var (k, c) = SupplyCalculator.RetreatSplit(100001, 3333, 9999, false);
            Assert.Equal(100001, k + c);
        }

        [Fact(DisplayName = "R-9 부상 + 사망 = 손실, 스위치 끄면 전부 사망")]
        public void R9()
        {
            var (w, d) = CasualtySplitter.Split(D.Balance, 777, true);
            Assert.Equal(777, w + d);
            Assert.Equal((0L, 777L), CasualtySplitter.Split(D.Balance, 777, false));
        }

        [Fact(DisplayName = "R-10 치료 비용 = 징병(군량)·모병(돈) 비용 × 치료 비율")]
        public void R10()
        {
            var b = D.Balance;
            Assert.Equal((b.HealCostRatio * HealCostCalculator.ConscriptCost(b, 400)).CeilToLong(), HealCostCalculator.HealCostInSupply(b, 400));
            Assert.Equal((b.HealCostRatio * HealCostCalculator.RecruitCost(b, 400)).CeilToLong(), HealCostCalculator.HealCostInGold(b, 400));
        }

        [Fact(DisplayName = "R-11 점수·횟수·조건이 모두 맞아야 사용 가능")]
        public void R11()
        {
            var pool = new SkillPointPool(F(30), new Dictionary<string, int> { ["s"] = 1 });
            Assert.False(pool.CanUse("s", F(31), true));
            Assert.False(pool.CanUse("s", F(10), false));
            Assert.False(pool.CanUse("unknown", F(1), true));
            Assert.True(pool.CanUse("s", F(30), true));
        }

        [Fact(DisplayName = "R-12 사용하면 소모되고, 못 쓰면 아무것도 바뀌지 않는다")]
        public void R12()
        {
            var pool = new SkillPointPool(F(30), new Dictionary<string, int> { ["s"] = 1 });
            Assert.False(pool.TryConsume("s", F(40), true));
            Assert.Equal(F(30), pool.Points);
            Assert.Equal(1, pool.UsesLeft("s"));
        }

        [Fact(DisplayName = "R-13 점수 회복은 스위치·사기 기준 충족 시에만, 시작 점수를 넘지 않음")]
        public void R13()
        {
            var b = D.Balance;
            var pool = new SkillPointPool(F(50), new Dictionary<string, int> { ["s"] = 1 });
            pool.TryConsume("s", F(20), true);
            pool.Regenerate(F(5), b.MoraleBase - F(1), b.MoraleBase, true);
            Assert.Equal(F(30), pool.Points);
            pool.Regenerate(F(5), b.MoraleBase, b.MoraleBase, false);
            Assert.Equal(F(30), pool.Points);
            pool.Regenerate(F(100), b.MoraleBase, b.MoraleBase, true);
            Assert.Equal(F(50), pool.Points);
        }

        [Fact(DisplayName = "R-14/R-15 실제 진형 상성표는 대칭이고 핵심 6종은 2/2")]
        public void R14_R15() => Assert.DoesNotContain(DataValidator.Validate(D), e => e.Contains("진형"));

        [Fact(DisplayName = "R-14 대칭 위반을 데이터 검사가 잡는다")]
        public void R14_Detects()
        {
            var doc = JsonUtil.ParseObject(System.IO.File.ReadAllText(System.IO.Path.Combine(DataRoot, "Rules", "formations.json")));
            ((JArray)doc["wins"]["craneWing"]).Add("fishScale"); // 어린 > 학익인데 학익 > 어린도 추가
            var bad = WithReplacedFile("Rules/formations.json", doc.ToString());
            Assert.Contains(DataValidator.Validate(bad), e => e.Contains("대칭 위반"));
        }

        [Fact(DisplayName = "R-17 숲: 보병 > 창병 > 궁병 > 기병")]
        public void R17()
        {
            Fix Pp(string troop) => TerrainCombat.ModifierPp(D, "forest", D.TroopTypes[troop]);
            Assert.True(Pp("infantry") > Pp("spearman"));
            Assert.True(Pp("spearman") > Pp("archer"));
            Assert.True(Pp("archer") > Pp("cavalry"));
        }

        [Fact(DisplayName = "R-18 덮어쓰기: 공통 → 스텝 → 규칙 세트, 나중 것이 이긴다")]
        public void R18()
        {
            var step = JObject.Parse("{\"casualty\":{\"woundedRatio\":0.3}}");
            var ruleSet = JObject.Parse("{\"casualty\":{\"woundedRatio\":0.2}}");
            Assert.Equal(F("0.3"), D.ResolveBalance(step).WoundedRatio);
            Assert.Equal(F("0.2"), D.ResolveBalance(step, ruleSet).WoundedRatio);
            Assert.Equal(D.Balance.HealCostRatio, D.ResolveBalance(step, ruleSet).HealCostRatio); // 안 건드린 값은 유지
        }

        [Fact(DisplayName = "R-19 상위 스위치가 꺼지면 하위는 꺼진 것으로 본다")]
        public void R19()
        {
            var flags = Flags(D, "{\"capture\": false, \"duelCapture\": true}");
            Assert.False(flags.IsEnabled("duelCapture"));
            Assert.False(flags.IsEnabled("recruitRetry")); // capture → recruitment → recruitRetry
            Assert.True(Flags(D).IsEnabled("recruitRetry"));
            Assert.Throws<DataException>(() => Flags(D).IsEnabled("noSuchFlag"));
            Assert.Throws<DataException>(() => Flags(D, "{\"typoFlag\": true}"));
        }

        [Fact(DisplayName = "R-24 고용 인원은 세 한도 중 가장 작은 값")]
        public void R24()
        {
            var b = D.Balance;
            Assert.Equal(100, HireCalculator.MaxHireable(b, 100, long.MaxValue / 1000, 1, 5000));
            Assert.Equal(50, HireCalculator.MaxHireable(b, 1000, long.MaxValue / 1000, 1, 50));
            Assert.Equal(0, HireCalculator.MaxHireable(b, 1000, 0, 1, 5000));
        }

        [Fact(DisplayName = "R-27 고용 가격 ≤ 추천 가격 × 상한 배수")]
        public void R27()
        {
            var b = D.Balance;
            long limit = (b.HirePriceCapMultiplier * 100).FloorToLong();
            Assert.True(HireCalculator.IsPriceAllowed(b, limit, 100));
            Assert.False(HireCalculator.IsPriceAllowed(b, limit + 1, 100));
        }

        [Fact(DisplayName = "R-28 화면 병사 비율은 예산을 넘지 않게 올림")]
        public void R28()
        {
            Assert.Equal(200, SoldierRatio.TroopsPerSoldier(60000, 300));
            Assert.Equal(201, SoldierRatio.TroopsPerSoldier(60001, 300));
            Assert.True(SoldierRatio.VisibleSoldiers(60001, SoldierRatio.TroopsPerSoldier(60001, 300)) <= 300);
        }

        [Fact(DisplayName = "R-29 같은 시드면 같은 난수열 (SplitMix64 기준값)")]
        public void R29()
        {
            Assert.Equal(0xE220A8397B1DCDAFUL, new SeededRandom(0).NextUInt64());
            var a = new SeededRandom(12345); var b = new SeededRandom(12345);
            for (int i = 0; i < 100; i++) Assert.Equal(a.NextInt(1000), b.NextInt(1000));
            var restored = SeededRandom.FromState(a.State);
            Assert.Equal(a.NextUInt64(), restored.NextUInt64());
        }

        [Fact(DisplayName = "R-30 편의 기능이 아닌 상품은 플레이로 얻는 경로가 있어야 한다")]
        public void R30()
        {
            Assert.DoesNotContain(DataValidator.Validate(D), e => e.Contains("R-30"));
            var bad = WithReplacedFile("Rules/products.json", "{\"products\":[{\"id\":\"exclusive\",\"kind\":\"general\"}]}");
            Assert.Contains(DataValidator.Validate(bad), e => e.Contains("R-30"));
        }
    }
}
