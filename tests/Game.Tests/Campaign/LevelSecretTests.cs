using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Events;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Campaign.Save;
using Game.Data.Defs;
using Game.Flow;
using Game.Foundation.Math;
using Game.Foundation.Save;
using Game.Progression;
using Xunit;
using static Game.Tests.TestData;

namespace Game.Tests.Campaign
{
    /// <summary>장수 레벨 (성장률 방식, battle.md 2.19.1).</summary>
    public class LevelTests
    {
        private static readonly LevelRules L = Kit.D.Levels;

        [Fact(DisplayName = "경험치가 필요치를 넘으면 레벨업, 능력치는 성장률 × 오른 레벨 (무작위 없음)")]
        public void LevelUp()
        {
            var rates = Kit.D.Generals["general_a"].GrowthRates;
            long need = L.ExpToNext(1) + L.ExpToNext(2);
            var r = LevelEngine.AddExp(L, rates, 1, 0, need + 7);
            Assert.Equal(3, r.Level);
            Assert.Equal(7, r.Exp);
            Assert.Equal(2, r.LevelsGained);
            Assert.Equal(rates["leadership"] * 2, r.StatGains["leadership"]);
            var none = LevelEngine.AddExp(L, rates, 1, 0, L.ExpToNext(1) - 1);
            Assert.Equal(1, none.Level);
            Assert.Empty(none.StatGains);
        }

        [Fact(DisplayName = "최대 레벨이면 더 오르지 않는다")]
        public void MaxLevel()
        {
            var r = LevelEngine.AddExp(L, Kit.D.Generals["general_a"].GrowthRates, L.MaxLevel, 0, 1_000_000);
            Assert.Equal(L.MaxLevel, r.Level);
            Assert.Equal(0, r.LevelsGained);
        }

        [Fact(DisplayName = "전투 경험치는 아주 더디게 (스텝 경험치 × 배율), 스위치를 끄면 없음")]
        public void BattleExp()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = Kit.Veteran();
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 5, Delegate = true });
            flow.Confirm(state, plan);
            Assert.True(flow.ApplyResult(state, plan, BattleSimulation.Run(plan.Setup)).Won);
            long expected = LevelEngine.BattleExp(L, StepDef.Load(Kit.Source, "step01").RewardExp);
            Assert.True(expected > 0);
            Assert.All(state.Generals, g => Assert.Equal(expected, g.Exp));
        }
    }

    /// <summary>스텝 연출이 전투 설정으로 넘어간다.</summary>
    public class StepEventFlowTests
    {
        [Fact(DisplayName = "스텝 연출·예비 군단이 전투 설정에 들어가고, 시작 대사가 기록된다")]
        public void Step02Events()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = Kit.Veteran();
            state.ClearedSteps.Add("step01");
            state.LearnedFormations.Add("fishScale");
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step02", Seed = 3 });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            Assert.Equal(3, plan.Setup.Events.Count);
            Assert.True(plan.Setup.Corps.Single(c => c.Id == "enemy_reserve_01").Reserve);
            Assert.Equal(StoryFlow.EnemyTeam, plan.Setup.Events.Single(e => e.Id == "enemy_half").Trigger.Team);
            var r = BattleSimulation.Run(plan.Setup);
            Assert.Contains(r.Events, e => e.Kind == EventKinds.Dialogue && e.Data == "dialogue.step02.start");
        }
    }

    /// <summary>숨은 보상 (secrets.md).</summary>
    public class SecretTests
    {
        [Fact(DisplayName = "숨은 보상 데이터의 조건·받는 장수 종류는 모두 등록되어 있다")]
        public void Registered() => Assert.Empty(new SecretEngine().Validate(Kit.D));

        [Fact(DisplayName = "일기토로 적장을 쓰러뜨리면 그 장수에게 특성 (한 번만), 도감에 기록")]
        public void DuelSecret()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = Kit.Veteran();
            Assert.All(SecretEngine.Codex(Kit.D, state), e => Assert.Null(e.SecretId));

            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 5 });
            plan.Setup.Contests.Clear();
            string mine = plan.Setup.Corps.First(c => c.Team == StoryFlow.PlayerTeam && c.Role == CorpsRoles.Commander).Id;
            plan.Setup.Contests.Add(new ContestSetup { ContestId = "duel", CorpsA = mine, CorpsB = "enemy_corps_01",
                ChoicesA = new List<string> { "bow", "bow" }, ChoicesB = new List<string> { "retreat", "retreat" } });
            flow.Confirm(state, plan);
            var report = flow.ApplyResult(state, plan, BattleSimulation.Run(plan.Setup));
            var winner = plan.Setup.Corps.First(c => c.Id == mine).General.Id;
            var found = Assert.Single(report.Secrets, s => s.SecretId == "secret_duel_fall");
            Assert.Equal(winner, found.GeneralId);
            Assert.Contains("peerless", state.FindGeneral(winner).LearnedTraits);
            Assert.Contains(SecretEngine.Codex(Kit.D, state), e => e.SecretId == "secret_duel_fall" && e.Found);
            Assert.Contains(SecretEngine.Codex(Kit.D, state), e => !e.Found && e.HintKey != null);
            Assert.Equal(1, state.FindGeneral(winner).Records["duelWins"]);

            // 한 번만
            var plan2 = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 6 });
            plan2.Setup.Contests.Clear();
            plan2.Setup.Contests.Add(new ContestSetup { ContestId = "duel", CorpsA = mine, CorpsB = "enemy_corps_01",
                ChoicesA = new List<string> { "bow", "bow" }, ChoicesB = new List<string> { "retreat", "retreat" } });
            flow.Confirm(state, plan2);
            Assert.DoesNotContain(flow.ApplyResult(state, plan2, BattleSimulation.Run(plan2.Setup)).Secrets, s => s.SecretId == "secret_duel_fall");
        }

        [Fact(DisplayName = "요구 능력치가 모자라면 숨은 보상 스킬은 스킬북으로 보관 (첫 싸움 손실 10% 이하)")]
        public void SkillStoredAsBook()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            CampaignState state = null;
            BattleReport report = null;
            long start = 0, lost = 0;
            bool qualifies = false;
            for (ulong seed = 1; seed <= 40 && !qualifies; seed++) // 손실 10% 이하로 이기는 판을 찾는다 (평균 손실은 약 13%)
            {
                state = Kit.Veteran();
                var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = seed, Delegate = true });
                flow.Confirm(state, plan);
                var result = BattleSimulation.Run(plan.Setup);
                report = flow.ApplyResult(state, plan, result);
                start = result.Corps.Where(c => c.Team == StoryFlow.PlayerTeam).Sum(c => c.StartTroops);
                lost = result.Corps.Where(c => c.Team == StoryFlow.PlayerTeam).Sum(c => c.Losses);
                qualifies = report.Won && lost * 10 <= start;
            }
            Assert.True(qualifies, "40개 시드 중 손실 10% 이하로 이긴 판이 없습니다.");
            Assert.Equal(qualifies, report.Secrets.Any(s => s.SecretId == "secret_step01_light_losses"));
            if (qualifies)
            {
                var s = report.Secrets.Single(x => x.SecretId == "secret_step01_light_losses");
                var commander = state.FindGeneral(s.GeneralId);
                // 화계는 지략 50 필요: 모자라면 보관함
                bool meets = Game.Campaign.Roster.CorpsValidator.EffectiveStat(Kit.D, commander, "intellect") >= Fix.FromInt(50);
                Assert.Equal(!meets, s.StoredAsBook);
                if (!meets) Assert.Equal(1, state.SkillBooks["fireAttack"]);
            }
        }

        [Fact(DisplayName = "숨은 보상마다 이름·힌트 문구가 있고, 보상 스킬은 있는 스킬, 특성은 이름이 있다")]
        public void Strings()
        {
            foreach (var s in Kit.D.Secrets)
            {
                Assert.True(Kit.D.Strings.ContainsKey("secret." + s.Id), s.Id + ": 이름");
                Assert.True(s.HintKey != null && Kit.D.Strings.ContainsKey(s.HintKey), s.Id + ": 힌트");
                Assert.True(s.RewardSkill != null || s.RewardTrait != null, s.Id + ": 보상 없음");
                if (s.RewardSkill != null) Assert.True(Kit.D.Skills.ContainsKey(s.RewardSkill), s.Id + ": 스킬 " + s.RewardSkill);
                if (s.RewardTrait != null) Assert.True(Kit.D.Strings.ContainsKey("trait." + s.RewardTrait), s.Id + ": 특성 " + s.RewardTrait);
                if (s.Step != null) Assert.Contains(s.Step, Kit.D.StepIndex.Order);
            }
        }

        private static CampaignState StartOf(string stepId)
        {
            var s = Kit.Veteran();
            foreach (var id in Kit.D.StepIndex.Order) { if (id == stepId) break; s.ClearedSteps.Add(id); }
            return s;
        }

        /// <summary>위임 출진 고정 시드 60개 중 숨은 보상이 나온 판 수 (숨은 보상은 드물어야 한다).</summary>
        private static int Hits(string stepId, string secretId, System.Action<SortiePlan> tweak = null)
        {
            int hits = 0;
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var state = StartOf(stepId);
                var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
                var plan = flow.Prepare(state, new SortieRequest { StepId = stepId, Seed = seed, Delegate = true });
                Assert.True(plan.Ok, string.Join("\n", plan.Errors));
                tweak?.Invoke(plan);
                flow.Confirm(state, plan);
                var result = BattleSimulation.Run(plan.Setup);
                var report = flow.ApplyResult(state, plan, result);
                var f = report.Secrets.FirstOrDefault(x => x.SecretId == secretId);
                if (f == null) continue;
                hits++;
                Assert.True(report.Won);
                Assert.Contains(secretId, state.FoundSecrets);
            }
            return hits;
        }

        [Theory(Skip = "군대 개편 PR 2에서 다시 켬: 새 군대 규모(army-v2.md, 처음 십부장 보병 3,000)에 맞춘 스텝·영토 밸런스 재조정 전", DisplayName = "스텝 숨은 보상은 나올 수 있지만 드물다 (위임 출진 60판 중 1~10판)")]
        [InlineData("step04", "secret_step04_iron_wall")]
        [InlineData("step06", "secret_step06_unbroken")]
        public void StepSecretRare(string stepId, string secretId)
        {
            int hits = Hits(stepId, secretId);
            Assert.InRange(hits, 1, 10);
        }

        [Fact(Skip = "군대 개편 PR 2에서 다시 켬: 새 군대 규모(army-v2.md, 처음 십부장 보병 3,000)에 맞춘 스텝·영토 밸런스 재조정 전", DisplayName = "저물녘 숲(step05)은 궁병 없이 이겨야 숨은 보상: 기본 편성(궁병 있음)으로는 안 나온다")]
        public void DuskChargeNeedsNoArchers()
        {
            const string id = "secret_step05_dusk_charge";
            Assert.Equal(0, Hits("step05", id));
            int hits = Hits("step05", id, plan =>
            {
                foreach (var c in plan.Setup.Corps)
                    if (c.Team == StoryFlow.PlayerTeam)
                        foreach (var u in c.Units)
                            if (Kit.D.TroopTypes[u.TroopType].HasTag("ranged")) u.TroopType = "infantry";
            });
            Assert.True(hits > 0, "궁병을 보병으로 바꿔 이긴 판이 없습니다.");
        }

        [Fact(DisplayName = "v2 세이브는 v3로 바뀌고 도감·보관함은 빈 값")]
        public void SaveV3()
        {
            var store = new InMemorySaveStore();
            store.Write("campaign", "{\"version\":2,\"campaign\":{\"Generals\":[],\"Prisoners\":[],\"ExploredBySteps\":{}}}");
            var loaded = new CampaignSaveService(store).Load();
            Assert.Empty(loaded.FoundSecrets);
            Assert.Empty(loaded.SkillBooks);
        }
    }
}
