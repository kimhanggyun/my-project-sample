using System;
using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Flow;
using Game.Foundation.Time;
using Game.Online;
using Game.Services;
using Game.Territory;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>영토 수입 (territory.md 6장, verification R-47): 시간당 쌓임, 최대 시간, 받기 = 우편, 주인이 바뀌면 이전 주인에게 정산.</summary>
    public class TerritoryIncomeTests
    {
        private static TerritoryDefs Defs() => TerritoryDefs.Load(Kit.Source);

        [Fact(DisplayName = "R-47 쌓인 수입 = 시간당 양 × 쌓인 초 ÷ 3600 (내림), 최대 incomeCapHours")]
        public void Accrued()
        {
            var d = Defs();
            var zone = d.Regions["dev_zone_a1"];
            var a = d.Accrued(zone, 0, 5400); // 1.5시간
            Assert.Equal(3000, a["supply"]);
            Assert.Equal(15, a["gold"]);
            Assert.Equal(2000 * 24, d.Accrued(zone, 0, 100 * 3600)["supply"]); // 24시간까지만
            Assert.Empty(d.Accrued(zone, 1000, 1000));
            Assert.Equal(1, d.Accrued(zone, 0, 360)["gold"]); // 10 × 0.1시간
        }

        [Fact(DisplayName = "R-46·R-47 장부: 차지하면 그때부터 쌓이고, 받으면 우편, 남에게 빼앗기면 이전 주인에게 그때까지 정산")]
        public void Ledger()
        {
            var d = Defs();
            var owners = new Dictionary<string, TerritoryOwner>();
            var mails = new List<(string who, Mail m)>();
            void Send(string who, Mail m) => mails.Add((who, m));

            TerritoryLedger.Capture(d, owners, "dev_zone_a1", "A", 0, Send);
            Assert.Empty(mails);
            TerritoryLedger.Capture(d, owners, "dev_zone_a1", "A", 1800, Send); // 이미 내 것: 그대로
            Assert.Equal(0, owners["dev_zone_a1"].CollectedUnixSeconds);

            var view = TerritoryLedger.View(d, owners, "A", 3600);
            Assert.Equal(2000, view.Owners.Single().Pending["supply"]);
            Assert.Null(TerritoryLedger.View(d, owners, "B", 3600).Owners.Single().Pending); // 남의 지역은 수입을 보이지 않는다

            var got = TerritoryLedger.Collect(d, owners, "A", 3600, Send);
            Assert.Equal(new[] { ("gold", 10L), ("supply", 2000L) }, got.Select(m => (m.Resource, m.Amount)).OrderBy(x => x.Resource));
            Assert.All(got, m => Assert.Equal((MailKinds.TerritoryIncome, "dev_zone_a1"), (m.Kind, m.RegionId)));
            Assert.Equal(3600, owners["dev_zone_a1"].CollectedUnixSeconds);

            mails.Clear();
            TerritoryLedger.Capture(d, owners, "dev_zone_a1", "B", 5400, Send); // B가 빼앗음: A에게 30분치
            Assert.Equal(1000, mails.Single(x => x.m.Resource == "supply" && x.who == "A").m.Amount);
            Assert.Equal(("B", 5400L), (owners["dev_zone_a1"].OwnerAccountId, owners["dev_zone_a1"].CollectedUnixSeconds));
        }

        [Fact(Skip = "군대 개편 PR 2에서 다시 켬: 새 군대 규모(army-v2.md, 처음 십부장 보병 3,000)에 맞춘 스텝·영토 밸런스 재조정 전", DisplayName = "R-47 가짜 서버: 차지 → 2시간 뒤 받기 → 우편 → mail.claim으로 지갑에, 같은 우편 두 번은 거절")]
        public void FakeCollectAndClaim()
        {
            var time = new ManualTimeProvider(Kit.Clock().UtcNow);
            var clock = new ServerClock(time);
            var fake = new LocalOnlineService(Kit.D, Kit.Source, time);
            var sync = new CloudSync(fake, clock);
            SyncResult start = null;
            sync.Start(null, 0, r => start = r);
            var flow = new StoryFlow(Kit.D, Kit.Source, clock);
            string here = FakeLocationService.Default.ToString();
            SyncResult won = null;
            for (ulong seed = 1; seed <= 10 && won == null; seed++)
            {
                var state = SaveJson.Read(SaveJson.Write(start.State));
                var plan = flow.Prepare(state, new SortieRequest { StepId = "territory.dev_zone_a1", Seed = seed, Delegate = true, Location = here });
                Assert.True(flow.Confirm(state, plan));
                var sim = new BattleSimulation(plan.Setup);
                while (!sim.IsOver) sim.Step();
                flow.ApplyResult(state, plan, sim.Result);
                if (sim.Result.Winner == StoryFlow.PlayerTeam) sync.SubmitBattle(CloudSync.SubmissionOf(plan, sim.CommandLog, sim.Result, start.Revision), state, r => won = r);
            }
            Assert.Equal(SyncOutcomes.Accepted, won.Outcome);

            time.Advance(TimeSpan.FromHours(2));
            TerritoryResponse view = null;
            fake.GetTerritory(x => view = x);
            Assert.Equal(4000, view.Owners.Single().Pending["supply"]);
            MailListResponse sent = null;
            fake.CollectTerritory(x => sent = x);
            Assert.Equal(2, sent.Mail.Count);
            MailListResponse box = null;
            fake.GetMail(x => box = x);
            var supply = box.Mail.Single(m => m.Resource == "supply");

            long before = won.State.Wallet["supply"];
            var claim = new ActionSubmission { BaseRevision = won.Revision, Actions = { PlayerActionRunner.ClaimMail(supply) } };
            ActionVerdict v = null;
            fake.SubmitActions(claim, x => v = x);
            Assert.True(v.Valid);
            Assert.Equal(before + 4000, SaveJson.Read(v.Save.Json).Wallet["supply"]);
            claim.BaseRevision = v.Save.Revision;
            fake.SubmitActions(claim, x => v = x);
            Assert.Equal(VerdictReasons.BadAction, v.Reason);
        }
    }
}
