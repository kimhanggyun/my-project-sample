using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Crowd;
using Game.Flow;
using Game.Foundation.Geo;
using Game.Foundation.Math;
using Game.Online;
using Game.Territory;
using Xunit;
using Xunit.Abstractions;

namespace Game.Tests.Campaign
{
    /// <summary>영지 깃발전 연결 (R-163 · R-166 · R-167 · R-168): 맵 고르기, 출진 준비 → 깃발전, 서버 다시 돌려 확인, 뺏기 · 없애기, 하루 잃는 상한 · 계급 차, 수비판.</summary>
    public class FlagBattleFlowTests
    {
        private readonly ITestOutputHelper _out;
        public FlagBattleFlowTests(ITestOutputHelper output) { _out = output; }

        private const string Here = "37.5665,126.978";
        private const long Now = 1_800_000_000;
        private static TerritoryDefs Defs => TerritoryDefs.Load(Kit.Source);
        private static FlagDefs Flags => Defs.Flags;

        private static TerritoryOwner Rival(string account = "rival", string at = "37.568,126.978", string general = "general_b", string map = null)
        {
            GeoPoint.TryParse(at, out var p);
            var o = new TerritoryOwner { RegionId = FlagDefs.KeyOf(Flags.CellOf(p)), OwnerAccountId = account, GeneralId = general, GeneralIds = new List<string> { general },
                RadiusM = 3000, SinceUnixSeconds = Now, CollectedUnixSeconds = Now, MapId = map, OwnerRank = Kit.Veteran().LordLevel };
            o.Defense = TerritoryDefenses.SnapshotGeneral(Kit.D, Kit.Veteran(), general, out _);
            o.Defense.Version = 1;
            o.Defense.OwnerAccountId = account;
            return o;
        }

        [Fact(DisplayName = "R-168 꽂을 때 맵: 고른 맵 (flags.json maps), 모르는 맵 · 없으면 첫째 (평원) · 주인 계급 기록")]
        public void PlantPicksMap()
        {
            var state = Kit.Veteran();
            var owners = new Dictionary<string, TerritoryOwner>();
            var r = TerritoryFlags.Plant(Defs, Kit.D, state, "me", null, owners, new PlantRequest { Location = Here, GeneralId = "general_a", MapId = "crowd_forest" }, Now);
            Assert.True(r.Ok, r.Reason);
            Assert.Equal("crowd_forest", r.Flag.MapId);
            Assert.Equal(state.LordLevel, r.Flag.OwnerRank);
            var r2 = TerritoryFlags.Plant(Defs, Kit.D, state, "me", null, owners, new PlantRequest { Location = Here, Target = "37.5705,126.978", GeneralId = "general_b", MapId = "nowhere" }, Now);
            Assert.Equal(Flags.DefaultMap, r2.Flag.MapId);
            Assert.Equal("crowd_plain", Flags.DefaultMap);
            Assert.Equal("crowd_forest", FlagBattles.MapOf(Flags, r.Flag));
            Assert.Equal("crowd_plain", FlagBattles.MapOf(Flags, new TerritoryOwner()));
            foreach (var m in Flags.Maps) Assert.True(Kit.Source.Exists("Maps/" + m + ".json"), m);
        }

        private static (StoryFlow flow, SortiePlan plan, TerritoryOwner rival) Plan(TerritoryOwner rival = null, Game.Campaign.CampaignState state = null)
        {
            rival = rival ?? Rival();
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var plan = flow.Prepare(state ?? Kit.Veteran(), new SortieRequest { StepId = TerritoryBattles.StepIdOf(rival.RegionId), Seed = 5, Location = Here, Defense = rival.Defense });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            return (flow, plan, rival);
        }

        [Fact(DisplayName = "R-163 출진 준비 → 깃발전: 군단마다 부대 하나 (병사 = 병력 ÷ troopsPerSoldier), 공격 = 아래 · 수비 = 깃발, 수비판 자리 · 자세 · 매복 · 함정")]
        public void SetupFromSortie()
        {
            var (_, plan, rival) = Plan();
            var rules = CrowdRules.Load(Kit.Source);
            var setup = FlagBattles.Setup(Kit.D, Kit.Source, plan, "crowd_plain");
            int allyCorps = plan.Setup.Corps.Count(c => c.Team == StoryFlow.PlayerTeam && !c.Reserve);
            Assert.Equal(allyCorps, setup.AllyUnits.Count);
            Assert.Single(setup.EnemyUnits);
            Assert.True(setup.FlagBattle);
            Assert.Equal(CrowdBattle.Enemy, setup.FlagDefender);
            var first = plan.Setup.Corps.First(c => c.Team == StoryFlow.PlayerTeam);
            long troops = first.Units.Sum(u => u.Troops);
            var spec = setup.AllyUnits.First(u => u.Key == first.Id);
            Assert.Equal(CrowdBridge.SoldiersFor(rules, troops), spec.Count);
            Assert.Equal((int)System.Math.Min(rules.GridMaxCount, (troops + rules.TroopsPerSoldier - 1) / rules.TroopsPerSoldier), spec.Count);
            Assert.Single(setup.AllyUnits, u => u.Commander);
            Assert.Equal(CrowdUnitSpec.Behaviors.Delegate, setup.EnemyUnits[0].Behavior); // 지킴 (AI)

            // 수비판: 뒷줄 왼쪽 · 제자리 · 매복, 함정 둘
            rival.Defense.Board = new DefenseBoard
            {
                Places = { new CrowdBoardPlace { GeneralId = "general_b", Row = 2, Col = 0, Stance = CrowdStances.Hold, Ambush = true } },
                Traps = { new BoardTrap { Kind = CrowdTrapKinds.Pit, X = Fix.FromInt(30), Y = Fix.FromInt(60) }, new BoardTrap { Kind = CrowdTrapKinds.Slow, X = Fix.FromInt(20), Y = Fix.FromInt(60) } },
            };
            var (_, plan2, _) = Plan(rival);
            var s2 = FlagBattles.Setup(Kit.D, Kit.Source, plan2, "crowd_forest");
            var e = Assert.Single(s2.EnemyUnits);
            Assert.Equal((2, 0), (e.Row, e.Col));
            Assert.Equal(CrowdUnitSpec.Behaviors.Hold, e.Behavior);
            Assert.True(e.Ambush);
            Assert.Equal(2, s2.Traps.Count);
            Assert.All(s2.Traps, t => Assert.Equal(CrowdBattle.Enemy, t.Team));
            var b = FlagBattles.New(Kit.D, Kit.Source, s2);
            Assert.Equal(b.Commander(CrowdBattle.Enemy).General.Pos, b.FlagPos.Value);
            Assert.Equal(2, b.Traps.Count);
        }

        /// <summary>기기에서 한 판: 우리 부대 모두 위임 (AI가 깃발로) + 처음에 명령 몇 개.</summary>
        private static CrowdBattle PlayOnDevice(SortiePlan plan, string map, int maxTicks = 3000)
        {
            var b = FlagBattles.New(Kit.D, Kit.Source, FlagBattles.Setup(Kit.D, Kit.Source, plan, map));
            var mine = b.Armies.Where(a => a.Team == CrowdBattle.Ally).ToList();
            for (int t = 0; t < maxTicks && !b.IsOver; t++)
            {
                if (t == 3) foreach (var a in mine) b.SetDelegated(a.Id, true);
                if (t == 30) b.UseSkill(mine[0].Id, CrowdBattle.Rally);
                b.Step();
            }
            return b;
        }

        [Fact(DisplayName = "R-166 서버 확인: 같은 출진 준비 + 명령 기록으로 다시 돌리면 같은 지문 · 틱 → 반영 (병력 손실), 지문이 다르면 mismatch")]
        public void ServerReplaysCrowdBattle()
        {
            var (_, plan, rival) = Plan();
            var device = PlayOnDevice(plan, "crowd_plain");
            _out.WriteLine($"device: tick {device.Tick} winner {device.Winner} {device.EndReason} flag {device.FlagProgress} commands {device.Commands.Count}");
            Assert.True(device.Commands.Count >= 2);
            var s = FlagBattles.SubmissionOf(plan, device, 1, FlagActions.Remove);
            Assert.Equal(FlagActions.Remove, s.FlagAction);

            // JSON으로 오가도 같게
            var wire = OnlineJson.Read<BattleSubmission>(OnlineJson.Write(s));
            Assert.Equal(s.CrowdCommands.Count, wire.CrowdCommands.Count);

            var server = Kit.Veteran();
            long before = server.Units.Sum(u => u.Troops);
            var verifier = new BattleVerifier(Kit.D, Kit.Source, Kit.Clock());
            var v = verifier.VerifyAndApply(server, wire, null, rival.Defense, "crowd_plain", out var report);
            Assert.True(v.Valid, v.Reason);
            Assert.Equal(s.ClaimedHash, v.Hash);
            Assert.Equal(device.Winner == CrowdBattle.Ally ? StoryFlow.PlayerTeam : device.Winner == CrowdBattle.Enemy ? StoryFlow.EnemyTeam : null, v.Winner);
            long after = server.Units.Sum(u => u.Troops);
            _out.WriteLine($"troops {before} -> {after}");
            Assert.True(after < before, "losses are applied");

            // 다른 맵(서버 장부의 맵이 다름) → 다른 전투
            var other = Kit.Veteran();
            var bad = new BattleVerifier(Kit.D, Kit.Source, Kit.Clock()).VerifyAndApply(other, OnlineJson.Read<BattleSubmission>(OnlineJson.Write(s)), null, rival.Defense, "crowd_forest", out _);
            Assert.False(bad.Valid);
            Assert.Equal(VerdictReasons.Mismatch, bad.Reason);
            // 지문을 바꾸면
            var forged = OnlineJson.Read<BattleSubmission>(OnlineJson.Write(s));
            forged.ClaimedHash = "0000000000000000";
            Assert.Equal(VerdictReasons.Mismatch, new BattleVerifier(Kit.D, Kit.Source, Kit.Clock()).VerifyAndApply(Kit.Veteran(), forged, null, rival.Defense, "crowd_plain", out _).Reason);
            // 영지 깃발이 아닌 스텝에 깃발전 기록 → 거절
            var story = OnlineJson.Read<BattleSubmission>(OnlineJson.Write(s));
            story.StepId = "step01";
            Assert.Equal(VerdictReasons.BadSortie, new BattleVerifier(Kit.D, Kit.Source, Kit.Clock()).VerifyAndApply(Kit.Veteran(), story, null, rival.Defense, "crowd_plain", out _).Reason);
        }

        [Fact(DisplayName = "R-163 이긴 뒤: 뺏기 = 내 깃발 (맵 그대로), 없애기 = 깃발이 사라지고 쌓인 수입은 이전 주인에게")]
        public void TakeOrRemove()
        {
            var me = Kit.Veteran();
            var rival = Rival(map: "crowd_pass");
            rival.CollectedUnixSeconds = Now - 3600;
            var owners = new Dictionary<string, TerritoryOwner> { [rival.RegionId] = rival };
            TerritoryFlags.AfterSiege(Defs, Kit.D, owners, rival.RegionId, true, "me", me, "general_a", Now, (w, m) => { });
            Assert.Equal("me", owners[rival.RegionId].OwnerAccountId);
            Assert.Equal("crowd_pass", owners[rival.RegionId].MapId);

            var rival2 = Rival(at: "37.569,126.979");
            rival2.CollectedUnixSeconds = Now - 3600;
            owners[rival2.RegionId] = rival2;
            var mails = new List<(string who, Mail m)>();
            TerritoryFlags.AfterSiege(Defs, Kit.D, owners, rival2.RegionId, true, "me", me, "general_a", Now, (w, m) => mails.Add((w, m)), null, remove: true);
            Assert.False(owners.ContainsKey(rival2.RegionId));
            Assert.Contains(mails, x => x.who == "rival" && x.m.Resource == "supply");
        }

        [Fact(DisplayName = "R-168 하루 잃는 상한: 주인이 그날 dailyLossCap개를 잃으면 남은 깃발은 다음 날(UTC)까지 못 침 · 새로 꽂은 깃발도 같음")]
        public void DailyLossCap()
        {
            var me = Kit.Veteran();
            int cap = Flags.DailyLossCap;
            Assert.True(cap >= 1);
            var owners = new Dictionary<string, TerritoryOwner>();
            var lats = new[] { "37.568", "37.570", "37.572", "37.574", "37.576", "37.578" };
            var flags = new List<TerritoryOwner>();
            for (int i = 0; i <= cap; i++) { var f = Rival(at: lats[i] + ",126.978"); owners[f.RegionId] = f; flags.Add(f); }
            for (int i = 0; i < cap; i++)
            {
                Assert.Null(TerritoryFlags.CheckSiege(Defs, Kit.D, me, "me", owners, flags[i].RegionId, Here, Now));
                TerritoryFlags.AfterSiege(Defs, Kit.D, owners, flags[i].RegionId, true, "me", me, "general_a", Now + i, (w, m) => { }, null, remove: true);
            }
            var last = flags[cap];
            Assert.Equal(cap, last.LossCount);
            Assert.NotNull(TerritoryFlags.CheckSiege(Defs, Kit.D, me, "me", owners, last.RegionId, Here, Now + 100));
            // 다음 날은 다시
            long tomorrow = (TerritoryFlags.DayOf(Now) + 1) * 86400 + 10;
            Assert.Null(TerritoryFlags.CheckSiege(Defs, Kit.D, me, "me", owners, last.RegionId, Here, tomorrow));
        }

        [Fact(DisplayName = "R-168 계급 차: 공격자 계급 − 주인 계급 > rankGapMax면 못 침 (예전 깃발 = 계급 0은 봄 안 함)")]
        public void RankGap()
        {
            var me = Kit.Veteran();
            var rival = Rival();
            var owners = new Dictionary<string, TerritoryOwner> { [rival.RegionId] = rival };
            rival.OwnerRank = me.LordLevel - Flags.RankGapMax;
            Assert.Null(TerritoryFlags.CheckSiege(Defs, Kit.D, me, "me", owners, rival.RegionId, Here, Now));
            rival.OwnerRank = me.LordLevel - Flags.RankGapMax - 1;
            if (rival.OwnerRank > 0) Assert.NotNull(TerritoryFlags.CheckSiege(Defs, Kit.D, me, "me", owners, rival.RegionId, Here, Now));
            me.LordLevel = rival.OwnerRank + Flags.RankGapMax + 5;
            rival.OwnerRank = 1;
            Assert.NotNull(TerritoryFlags.CheckSiege(Defs, Kit.D, me, "me", owners, rival.RegionId, Here, Now));
            rival.OwnerRank = 0;
            Assert.Null(TerritoryFlags.CheckSiege(Defs, Kit.D, me, "me", owners, rival.RegionId, Here, Now));
        }

        [Fact(DisplayName = "R-167 수비판 저장: 내 깃발 장수만 · 칸 겹침 없음 · 아는 자세 · 함정 수 상한, 되면 판 + 1, 장수를 더하거나 회수해도 남음")]
        public void SaveBoard()
        {
            var state = Kit.Veteran();
            var owners = new Dictionary<string, TerritoryOwner>();
            var flag = TerritoryFlags.Plant(Defs, Kit.D, state, "me", null, owners, new PlantRequest { Location = Here, GeneralIds = new List<string> { "general_a", "general_b" } }, Now).Flag;
            DefenseBoard Board(params CrowdBoardPlace[] p) => new DefenseBoard { Places = p.ToList() };
            FlagBoardRequest R(DefenseBoard b) => new FlagBoardRequest { FlagKey = flag.RegionId, Board = b };
            var ok = Board(new CrowdBoardPlace { GeneralId = "general_a", Row = 0, Col = 1, Stance = CrowdStances.Guard }, new CrowdBoardPlace { GeneralId = "general_b", Row = 2, Col = 2, Stance = CrowdStances.Advance, Ambush = true });
            Assert.Equal(PlantReasons.NotMine, TerritoryFlags.SetBoard(Defs, "other", owners, R(ok), Now).Reason);
            Assert.Equal(PlantReasons.BadBoard, TerritoryFlags.SetBoard(Defs, "me", owners, R(Board(new CrowdBoardPlace { GeneralId = "general_c", Row = 0, Col = 0 })), Now).Reason);
            Assert.Equal(PlantReasons.BadBoard, TerritoryFlags.SetBoard(Defs, "me", owners, R(Board(new CrowdBoardPlace { GeneralId = "general_a", Row = 0, Col = 0 }, new CrowdBoardPlace { GeneralId = "general_b", Row = 0, Col = 0 })), Now).Reason);
            Assert.Equal(PlantReasons.BadBoard, TerritoryFlags.SetBoard(Defs, "me", owners, R(Board(new CrowdBoardPlace { GeneralId = "general_a", Row = 3, Col = 0 })), Now).Reason);
            Assert.Equal(PlantReasons.BadBoard, TerritoryFlags.SetBoard(Defs, "me", owners, R(Board(new CrowdBoardPlace { GeneralId = "general_a", Row = 0, Col = 0, Stance = "dance" })), Now).Reason);
            var many = Board();
            for (int i = 0; i <= Flags.BoardTrapsMax; i++) many.Traps.Add(new BoardTrap { Kind = CrowdTrapKinds.Pit, X = Fix.FromInt(10 + i), Y = Fix.FromInt(50) });
            Assert.Equal(PlantReasons.BadBoard, TerritoryFlags.SetBoard(Defs, "me", owners, R(many), Now).Reason);
            ok.Traps.Add(new BoardTrap { Kind = CrowdTrapKinds.Slow, X = Fix.FromInt(30), Y = Fix.FromInt(70) });
            var r = TerritoryFlags.SetBoard(Defs, "me", owners, R(ok), Now);
            Assert.True(r.Ok, r.Reason);
            Assert.Equal(2, flag.Defense.Version);
            Assert.Equal(2, flag.Defense.Board.Places.Count);
            // 장수 회수 → 남은 장수 자리만, 함정은 그대로
            var rec = TerritoryFlags.Recall(Defs, Kit.D, state, "me", owners, new RecallRequest { FlagKey = flag.RegionId, GeneralIds = new List<string> { "general_b" } }, Now + 10, (w, m) => { });
            Assert.True(rec.Ok);
            Assert.Single(flag.Defense.Board.Places);
            Assert.Single(flag.Defense.Board.Traps);
            // 공격받는 중엔 못 바꿈
            flag.SiegeAttackerId = "x";
            Assert.Equal(PlantReasons.UnderSiege, TerritoryFlags.SetBoard(Defs, "me", owners, R(Board()), Now).Reason);
        }
    }
}
