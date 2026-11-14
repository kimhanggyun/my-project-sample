using System.Collections.Generic;
using System.Linq;
using Game.Campaign.Roster;
using Game.Flow;
using Game.Foundation.Geo;
using Game.Online;
using Game.Territory;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>영토 깃발 (territory-v2.md 2장, verification R-96 깃발 꽂기 · R-97 공성전 · 깃발 수입).</summary>
    public class TerritoryFlagTests
    {
        private const string Here = "37.5665,126.978";
        private const long Now = 1_800_000_000;
        private static TerritoryDefs Defs => TerritoryDefs.Load(Kit.Source);
        private static FlagDefs Flags => Defs.Flags;

        private static PlantRequest Req(string general, string target = null) => new PlantRequest { Location = Here, Target = target, GeneralId = general };

        private static TerritoryOwner Other(string account, string at, long radius, long protectedUntil = 0)
        {
            GeoPoint.TryParse(at, out var p);
            return new TerritoryOwner { RegionId = FlagDefs.KeyOf(Flags.CellOf(p)), OwnerAccountId = account, GeneralId = "x", RadiusM = radius, ProtectedUntilUnixSeconds = protectedUntil, SinceUnixSeconds = Now, CollectedUnixSeconds = Now };
        }

        [Fact(DisplayName = "R-96 칸 맞춤: 같은 칸의 점은 같은 가운데, 키로 오가며, 음수 좌표도 아래로 내림")]
        public void CellSnapping()
        {
            var f = Flags;
            Assert.Equal(f.CellOf(new GeoPoint(37_566_500, 126_978_000)), f.CellOf(new GeoPoint(37_566_100, 126_977_500)));
            var c = f.CellOf(new GeoPoint(37_566_500, 126_978_000));
            Assert.Equal(0, (c.LatE6 - f.CellLatE6 / 2) % f.CellLatE6);
            Assert.True(FlagDefs.TryParseKey(FlagDefs.KeyOf(c), out var back));
            Assert.Equal(c, back);
            var neg = f.CellOf(new GeoPoint(-100, -100));
            Assert.Equal(-f.CellLatE6 / 2, neg.LatE6);
            Assert.False(FlagDefs.TryParseKey("flag:abc", out _));
        }

        [Fact(DisplayName = "R-96 거리: 위도 0.0089도(약 991m)는 1km 안, 0.0091도(약 1,013m)는 밖")]
        public void Distance()
        {
            var f = Flags;
            var a = new GeoPoint(37_566_500, 126_978_000);
            Assert.True(f.Within(a, new GeoPoint(37_575_400, 126_978_000), 1000));
            Assert.False(f.Within(a, new GeoPoint(37_575_600, 126_978_000), 1000));
        }

        [Fact(DisplayName = "R-96 꽂기: 빈 땅이면 그 칸에 깃발 (반지름 = 계급, 수비 = 그 장수 군단 하나)")]
        public void PlantEmpty()
        {
            var state = Kit.Veteran();
            var owners = new Dictionary<string, TerritoryOwner>();
            var r = TerritoryFlags.Plant(Defs, Kit.D, state, "me", null, owners, Req("general_a"), Now);
            Assert.True(r.Ok, r.Reason);
            Assert.Equal(10000, r.Flag.RadiusM); // 대장군
            Assert.Equal("general_a", r.Flag.GeneralId);
            Assert.Single(r.Flag.Defense.Corps);
            Assert.Equal(1, r.Flag.Defense.Version);
            Assert.True(owners.ContainsKey(r.Flag.RegionId));
        }

        [Fact(DisplayName = "R-96 장수: 없으면 noGeneral, 다른 깃발에 있거나 출전 못 하면(중상 · 포로) generalBusy, 기본 편성에 군단이 없으면 notInFormation")]
        public void GeneralChecks()
        {
            var state = Kit.Veteran();
            var owners = new Dictionary<string, TerritoryOwner>();
            Assert.Equal(PlantReasons.NoGeneral, TerritoryFlags.Plant(Defs, Kit.D, state, "me", null, owners, Req("nobody"), Now).Reason);
            Assert.True(TerritoryFlags.Plant(Defs, Kit.D, state, "me", null, owners, Req("general_a"), Now).Ok);
            Assert.Equal(PlantReasons.GeneralBusy, TerritoryFlags.Plant(Defs, Kit.D, state, "me", null, owners, Req("general_a", "37.57,126.98"), Now).Reason);
            state.FindGeneral("general_b").Status = GeneralStatus.SeriousInjury;
            Assert.Equal(PlantReasons.GeneralBusy, TerritoryFlags.Plant(Defs, Kit.D, state, "me", null, owners, Req("general_b", "37.57,126.98"), Now).Reason);
            state.Compositions.RemoveAll(c => c.GeneralId == "general_c");
            Assert.Equal(PlantReasons.NotInFormation, TerritoryFlags.Plant(Defs, Kit.D, state, "me", null, owners, Req("general_c", "37.57,126.98"), Now).Reason);
        }

        [Fact(DisplayName = "R-96 범위: 꽂는 곳은 내 위치에서 계급 반지름 안 (십부장 1km)")]
        public void Range()
        {
            var state = Kit.Veteran();
            state.LordLevel = 1;
            var owners = new Dictionary<string, TerritoryOwner>();
            Assert.Equal(PlantReasons.OutOfRange, TerritoryFlags.Plant(Defs, Kit.D, state, "me", null, owners, Req("general_a", "37.5865,126.978"), Now).Reason);
            Assert.True(TerritoryFlags.Plant(Defs, Kit.D, state, "me", null, owners, Req("general_a", "37.5705,126.978"), Now).Ok);
            Assert.Equal(PlantReasons.BadLocation, TerritoryFlags.Plant(Defs, Kit.D, state, "me", null, owners, new PlantRequest { GeneralId = "general_b" }, Now).Reason);
        }

        [Fact(DisplayName = "R-96 겹침: 내 · 우리 연합 원과는 겹쳐도 되고, 같은 칸이면 occupied")]
        public void FriendlyOverlap()
        {
            var state = Kit.Veteran();
            var owners = new Dictionary<string, TerritoryOwner>();
            Assert.True(TerritoryFlags.Plant(Defs, Kit.D, state, "me", null, owners, Req("general_a"), Now).Ok);
            Assert.True(TerritoryFlags.Plant(Defs, Kit.D, state, "me", null, owners, Req("general_b", "37.5705,126.978"), Now).Ok);
            Assert.Equal(PlantReasons.Occupied, TerritoryFlags.Plant(Defs, Kit.D, state, "me", null, owners, Req("general_c"), Now).Reason);
            var ally = Other("friend", "37.58,126.99", 5000);
            owners[ally.RegionId] = ally;
            string Al(string a) => a == "me" || a == "friend" ? "al" : null;
            Assert.True(TerritoryFlags.Plant(Defs, Kit.D, state, "me", Al, owners, Req("general_c", "37.578,126.988"), Now).Ok);
        }

        [Fact(DisplayName = "R-97 남의 원 안: 꽂지 않고 가장 가까운 깃발과 공성전, 보호 중이면 protected")]
        public void Siege()
        {
            var state = Kit.Veteran();
            var owners = new Dictionary<string, TerritoryOwner>();
            var far = Other("rival", "37.575,126.978", 3000);
            var near = Other("rival2", "37.568,126.978", 3000);
            owners[far.RegionId] = far;
            owners[near.RegionId] = near;
            var r = TerritoryFlags.Plant(Defs, Kit.D, state, "me", null, owners, Req("general_a"), Now);
            Assert.False(r.Ok);
            Assert.Equal(PlantReasons.Siege, r.Reason);
            Assert.Equal(near.RegionId, r.AttackKey);
            Assert.Equal(2, owners.Count); // 꽂지 않았다
            near.ProtectedUntilUnixSeconds = Now + 10;
            Assert.Equal(PlantReasons.Protected, TerritoryFlags.Plant(Defs, Kit.D, state, "me", null, owners, Req("general_a"), Now).Reason);
        }

        [Fact(DisplayName = "R-97 공성전 검사: 내 깃발 · 보호 중 · 반지름 밖은 못 치고, 기기(장부 모름)는 위치 · 범위만")]
        public void SiegeCheck()
        {
            var state = Kit.Veteran();
            var rival = Other("rival", "37.568,126.978", 3000);
            var owners = new Dictionary<string, TerritoryOwner> { [rival.RegionId] = rival };
            Assert.Null(TerritoryFlags.CheckSiege(Defs, Kit.D, state, "me", owners, rival.RegionId, Here, Now));
            Assert.NotNull(TerritoryFlags.CheckSiege(Defs, Kit.D, state, "rival", owners, rival.RegionId, Here, Now));
            rival.ProtectedUntilUnixSeconds = Now + 1;
            Assert.NotNull(TerritoryFlags.CheckSiege(Defs, Kit.D, state, "me", owners, rival.RegionId, Here, Now));
            state.LordLevel = 1;
            Assert.NotNull(TerritoryFlags.CheckSiege(Defs, Kit.D, state, "me", null, Other("x", "37.60,126.978", 1000).RegionId, Here, Now));
            Assert.Null(TerritoryFlags.CheckSiege(Defs, Kit.D, state, "me", null, rival.RegionId, Here, Now));
        }

        [Fact(DisplayName = "R-97 공성전 뒤: 이기면 이전 주인에게 쌓인 수입 우편 + 그 자리에 내 깃발(공격 장수 · 내 계급 반지름), 지든 이기든 보호 시간")]
        public void AfterSiege()
        {
            var me = Kit.Veteran();
            var rival = Other("rival", "37.568,126.978", 3000);
            rival.CollectedUnixSeconds = Now - 7200;
            var owners = new Dictionary<string, TerritoryOwner> { [rival.RegionId] = rival };
            var mails = new List<(string who, Mail m)>();
            TerritoryFlags.AfterSiege(Defs, Kit.D, owners, rival.RegionId, false, "me", me, "general_a", Now, (w, m) => mails.Add((w, m)));
            Assert.Equal("rival", owners[rival.RegionId].OwnerAccountId);
            Assert.Equal(Now + Flags.ProtectSeconds, owners[rival.RegionId].ProtectedUntilUnixSeconds);
            Assert.Empty(mails);
            TerritoryFlags.AfterSiege(Defs, Kit.D, owners, rival.RegionId, true, "me", me, "general_a", Now, (w, m) => mails.Add((w, m)));
            var f = owners[rival.RegionId];
            Assert.Equal("me", f.OwnerAccountId);
            Assert.Equal("general_a", f.GeneralId);
            Assert.Equal(10000, f.RadiusM);
            Assert.Equal(Now + Flags.ProtectSeconds, f.ProtectedUntilUnixSeconds);
            Assert.Single(f.Defense.Corps);
            Assert.Contains(mails, x => x.who == "rival" && x.m.Resource == "supply" && x.m.Amount == 3000);
        }

        [Fact(DisplayName = "R-97 깃발 수입: 깃발마다 시간당 (군량 1,500 · 돈 8), 받으면 우편, 최대 24시간")]
        public void Income()
        {
            var flag = Other("me", "37.568,126.978", 3000);
            flag.CollectedUnixSeconds = Now - 7200;
            var owners = new Dictionary<string, TerritoryOwner> { [flag.RegionId] = flag };
            var view = TerritoryLedger.View(Defs, owners, "me", Now);
            Assert.Equal(3000, view.Owners[0].Pending["supply"]);
            Assert.Equal(16, view.Owners[0].Pending["gold"]);
            var sent = TerritoryLedger.Collect(Defs, owners, "me", Now, (w, m) => { });
            Assert.Equal(2, sent.Count);
            Assert.Equal(Now, flag.CollectedUnixSeconds);
            Assert.Equal(1500 * 24, Flags.Accrued(0, 100 * 3600)["supply"]);
        }

        [Fact(DisplayName = "R-97 공성전 스텝: 깃발 키 → flags.json 틀, 출진 준비는 상대 수비 스냅샷으로 (반지름 안일 때)")]
        public void SiegeStep()
        {
            var rivalState = Kit.Veteran();
            var rival = Other("rival", "37.568,126.978", 3000);
            rival.Defense = TerritoryDefenses.SnapshotGeneral(Kit.D, rivalState, "general_b", out _);
            var step = TerritoryBattles.StepFor(Defs, rival.RegionId);
            Assert.True(step.IsTerritory);
            Assert.Equal("territory." + rival.RegionId, step.Id);
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var plan = flow.Prepare(Kit.Veteran(), new SortieRequest { StepId = step.Id, Seed = 1, Delegate = true, Location = Here, Defense = rival.Defense });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            var low = Kit.Veteran();
            low.LordLevel = 1;
            var far = Other("rival", "37.60,126.978", 1000);
            var bad = flow.Prepare(low, new SortieRequest { StepId = "territory." + far.RegionId, Seed = 1, Delegate = true, Location = Here, Defense = rival.Defense });
            Assert.False(bad.Ok);
        }
    }
}
