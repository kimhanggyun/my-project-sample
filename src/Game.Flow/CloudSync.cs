using System;
using System.Collections.Generic;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Campaign.Save;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Foundation.Save;
using Game.Foundation.Time;
using Game.Online;

namespace Game.Flow
{
    /// <summary>세이브 ↔ 클라우드 세이브 JSON (기기·서버 공통 형식).</summary>
    public static class SaveJson
    {
        public static string Write(CampaignState state)
        {
            var mem = new InMemorySaveStore();
            new CampaignSaveService(mem).Save(state);
            return mem.Read(CampaignSaveService.DefaultKey);
        }

        public static CampaignState Read(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var mem = new InMemorySaveStore();
                mem.Write(CampaignSaveService.DefaultKey, json);
                return new CampaignSaveService(mem).Load();
            }
            catch (Exception) { return null; }
        }
    }

    /// <summary>서버 시각 = 기기 시각 + 차이 (서버 시각을 받으면 맞춘다). 시간에 달린 규칙(부상 회복 …)이 서버와 같게 계산된다.</summary>
    public sealed class ServerClock : ITimeProvider
    {
        private readonly ITimeProvider _device;
        public long OffsetSeconds { get; private set; }

        public ServerClock(ITimeProvider device) { _device = device; }

        public DateTimeOffset UtcNow => _device.UtcNow.AddSeconds(OffsetSeconds);

        public void Sync(long serverUnixSeconds) => OffsetSeconds = serverUnixSeconds - _device.UtcNow.ToUnixTimeSeconds();
    }

    public static class SyncOutcomes
    {
        /// <summary>서버 세이브를 받아 씀.</summary>
        public const string FromServer = "fromServer";
        /// <summary>처음 연결: 기기 진행이 더 많아 기기 세이브를 올림.</summary>
        public const string Uploaded = "uploaded";
        /// <summary>서버에 연결하지 못함: 기기 세이브 그대로.</summary>
        public const string Offline = "offline";
        /// <summary>전투가 서버에서 반영됨.</summary>
        public const string Accepted = "accepted";
        /// <summary>서버가 전투를 거절: 서버 세이브로 되돌림.</summary>
        public const string Rejected = "rejected";
        /// <summary>서버에 닿지 않아 기록(행동·전투)을 기기에 남겨 둠: 다음 연결 때 차례로 올린다 (R-42).</summary>
        public const string Queued = "queued";
    }

    public sealed class SyncResult
    {
        public string Outcome { get; internal set; }
        /// <summary>앞으로 쓸 진행 상태 (Offline이면 넘긴 기기 상태 그대로).</summary>
        public CampaignState State { get; internal set; }
        /// <summary>서버 Revision (Offline이면 그대로).</summary>
        public long Revision { get; internal set; }
        public BattleVerdict Verdict { get; internal set; }
    }

    /// <summary>
    /// 기기 쪽 동기화 흐름 (docs/impl/server.md 5장). 서버가 세이브의 주인이다 (R-40).
    /// 시작: 로그인 → 서버 시각 → 서버 세이브. 처음 연결이고 기기 진행(클리어한 스텝)이 더 많으면 기기 세이브를 올리고, 아니면 서버 세이브를 받아 쓴다.
    /// 전투 뒤: 결과를 제출하고, 서버가 반영한 세이브를 받아 쓴다. 거절되면 서버 세이브로 되돌린다. 서버에 닿지 않으면 기기 결과를 그대로 둔다.
    /// </summary>
    public sealed class CloudSync
    {
        private readonly IOnlineService _online;
        private readonly ServerClock _clock;

        public CloudSync(IOnlineService online, ServerClock clock)
        {
            _online = online;
            _clock = clock;
        }

        /// <param name="localRevision">이 기기가 마지막으로 맞춘 서버 Revision (한 번도 안 맞췄으면 0).</param>
        public void Start(CampaignState local, long localRevision, Action<SyncResult> done)
        {
            var offline = new SyncResult { Outcome = SyncOutcomes.Offline, State = local, Revision = localRevision };
            _online.SignIn(ok =>
            {
                if (!ok) { done(offline); return; }
                _online.GetTime(t =>
                {
                    if (t != null) _clock?.Sync(t.UnixSeconds);
                    _online.LoadSave(save =>
                    {
                        var server = save == null ? null : SaveJson.Read(save.Json);
                        if (server == null) { done(offline); return; }
                        bool firstLink = localRevision == 0;
                        if (firstLink && local != null && local.ClearedSteps.Count > server.ClearedSteps.Count)
                        {
                            _online.PutSave(new SavePutRequest { BaseRevision = save.Revision, Json = SaveJson.Write(local) }, put =>
                            {
                                if (put != null && put.Ok) done(new SyncResult { Outcome = SyncOutcomes.Uploaded, State = local, Revision = put.Current.Revision });
                                else done(new SyncResult { Outcome = SyncOutcomes.FromServer, State = server, Revision = save.Revision });
                            });
                            return;
                        }
                        done(new SyncResult { Outcome = SyncOutcomes.FromServer, State = server, Revision = save.Revision });
                    });
                });
            });
        }

        /// <summary>전투 제출 메시지 (출진 준비 결과 + 명령 기록 + 기기 결과).</summary>
        public static BattleSubmission SubmissionOf(SortiePlan plan, IReadOnlyList<Game.Battle.Model.Commands.BattleCommand> commands, BattleResult result, long baseRevision)
        {
            var s = new BattleSubmission
            {
                BaseRevision = baseRevision,
                StepId = plan.Step.Id,
                Seed = plan.Setup.Seed,
                Supply = plan.SupplyTaken,
                Commands = new List<Game.Battle.Model.Commands.BattleCommand>(commands),
                ClaimedHash = result.StateHash.ToString("x16"),
                ClaimedTicks = result.Ticks,
                PlayedAtUnixSeconds = plan.PreparedAtUnixSeconds,
                Location = plan.Location,
                DefenseVersion = plan.Defense?.Version ?? 0,
            };
            if (plan.Hired != null && !plan.Hired.IsEmpty)
            {
                s.Hires = new List<HireUse>(plan.Hired.Uses);
                s.Compositions = PlayerActionRunner.ToActionCorps(plan.Compositions);
            }
            bool all = true;
            foreach (var c in plan.Setup.Corps)
                if (c.Team == StoryFlow.PlayerTeam)
                {
                    if (c.Delegated) s.DelegateGenerals.Add(c.General.Id);
                    else all = false;
                }
            if (all && s.DelegateGenerals.Count > 0) { s.Delegate = true; s.DelegateGenerals.Clear(); }
            foreach (var c in plan.Setup.Contests)
                s.ContestChoices[c.ContestId] = c.ChoicesA == null ? new List<string>() : new List<string>(c.ChoicesA);
            return s;
        }

        /// <summary>
        /// 기기 행동 제출 (R-41): 서버가 자기 세이브에 다시 적용한 세이브를 받는다. 거절되면 서버 세이브로 되돌린다. 서버에 닿지 않으면 Offline (행동은 기기에 남겨 두고 나중에 다시).
        /// </summary>
        /// <param name="local">기기에서 행동을 적용한 상태 (서버에 닿지 않으면 그대로 쓴다).</param>
        public void SubmitActions(ActionSubmission submission, CampaignState local, Action<SyncResult> done)
        {
            _online.SubmitActions(submission, verdict =>
            {
                var server = verdict?.Save == null ? null : SaveJson.Read(verdict.Save.Json);
                if (verdict == null || server == null) { done(new SyncResult { Outcome = SyncOutcomes.Offline, State = local, Revision = submission.BaseRevision }); return; }
                done(new SyncResult { Outcome = verdict.Valid ? SyncOutcomes.Accepted : SyncOutcomes.Rejected, State = server, Revision = verdict.Save.Revision });
            });
        }

        /// <summary>
        /// 남은 기록(link.Journal: 행동·전투, 한 순서)을 차례로 보낸다 (R-41, R-42). 이어진 행동은 한 묶음으로, 전투는 하나씩.
        /// 받아들여질 때마다 기록을 지우고 link.Revision을 올린다. 끝까지 맞으면 Accepted + 마지막 서버 세이브.
        /// 하나라도 거절되면 남은 기록을 모두 버리고 Rejected + 서버 세이브 (기기 세이브가 서버와 갈라졌으므로).
        /// 서버에 닿지 않으면 남은 기록을 그대로 두고 Queued + 기기 상태 (다음에 이어서).
        /// </summary>
        public void FlushJournal(OnlineLink link, CampaignState local, Action<SyncResult> done)
        {
            CampaignState lastServer = null;
            BattleVerdict lastBattle = null;

            void Handle(int count, bool valid, CloudSave save, BattleVerdict battle)
            {
                if (battle != null) lastBattle = battle;
                var server = save == null ? null : SaveJson.Read(save.Json);
                if (!valid)
                {
                    link.Journal.Clear();
                    if (server != null) { done(new SyncResult { Outcome = SyncOutcomes.Rejected, State = server, Revision = save.Revision, Verdict = lastBattle }); return; }
                    _online.LoadSave(s =>
                    {
                        var st = s == null ? null : SaveJson.Read(s.Json);
                        done(st == null
                            ? new SyncResult { Outcome = SyncOutcomes.Queued, State = local, Revision = link.Revision, Verdict = lastBattle }
                            : new SyncResult { Outcome = SyncOutcomes.Rejected, State = st, Revision = s.Revision, Verdict = lastBattle });
                    });
                    return;
                }
                if (server == null) { done(new SyncResult { Outcome = SyncOutcomes.Queued, State = local, Revision = link.Revision, Verdict = lastBattle }); return; }
                link.Journal.RemoveRange(0, Math.Min(count, link.Journal.Count));
                link.Revision = save.Revision;
                lastServer = server;
                Next();
            }

            void Next()
            {
                if (link.Journal.Count == 0)
                {
                    done(new SyncResult { Outcome = SyncOutcomes.Accepted, State = lastServer ?? local, Revision = link.Revision, Verdict = lastBattle });
                    return;
                }
                var head = link.Journal[0];
                if (head.Battle != null)
                {
                    head.Battle.BaseRevision = link.Revision;
                    _online.SubmitBattle(head.Battle, v =>
                    {
                        if (v == null) { done(new SyncResult { Outcome = SyncOutcomes.Queued, State = local, Revision = link.Revision, Verdict = lastBattle }); return; }
                        Handle(1, v.Valid, v.Save, v);
                    });
                    return;
                }
                var actions = new List<PlayerAction>();
                while (actions.Count < link.Journal.Count && actions.Count < PlayerActionRunner.MaxPerSubmission && link.Journal[actions.Count].Action != null)
                    actions.Add(link.Journal[actions.Count].Action);
                int n = actions.Count;
                _online.SubmitActions(new ActionSubmission { BaseRevision = link.Revision, Actions = actions }, v =>
                {
                    if (v == null) { done(new SyncResult { Outcome = SyncOutcomes.Queued, State = local, Revision = link.Revision, Verdict = lastBattle }); return; }
                    Handle(n, v.Valid, v.Save, null);
                });
            }

            Next();
        }

        /// <param name="localAfter">기기에서 결과를 반영한 상태 (서버에 닿지 않으면 그대로 쓴다).</param>
        public void SubmitBattle(BattleSubmission submission, CampaignState localAfter, Action<SyncResult> done)
        {
            _online.SubmitBattle(submission, verdict =>
            {
                if (verdict == null) { done(new SyncResult { Outcome = SyncOutcomes.Offline, State = localAfter, Revision = submission.BaseRevision }); return; }
                var server = verdict.Save == null ? null : SaveJson.Read(verdict.Save.Json);
                if (verdict.Valid && server != null)
                {
                    done(new SyncResult { Outcome = SyncOutcomes.Accepted, State = server, Revision = verdict.Save.Revision, Verdict = verdict });
                    return;
                }
                if (server != null)
                {
                    done(new SyncResult { Outcome = SyncOutcomes.Rejected, State = server, Revision = verdict.Save.Revision, Verdict = verdict });
                    return;
                }
                // 거절 + 세이브 없음(지문 불일치 등): 서버 세이브를 다시 받아 되돌린다
                _online.LoadSave(save =>
                {
                    var s = save == null ? null : SaveJson.Read(save.Json);
                    done(s == null
                        ? new SyncResult { Outcome = SyncOutcomes.Offline, State = localAfter, Revision = submission.BaseRevision, Verdict = verdict }
                        : new SyncResult { Outcome = SyncOutcomes.Rejected, State = s, Revision = save.Revision, Verdict = verdict });
                });
            });
        }
    }

    /// <summary>
    /// 가짜 온라인 (서비스 설정 online = fake): 서버 없이 기기 안에서 같은 규칙(세이브 Revision, 전투 검증 R-40)을 흉내 낸다.
    /// 화면 흐름을 서버 없이 확인할 때 쓴다.
    /// </summary>
    public sealed class LocalOnlineService : IOnlineService
    {
        private readonly DefinitionSet _defs;
        private readonly IDataSource _source;
        private readonly ITimeProvider _time;
        private readonly ISaveStore _persist;
        private readonly PlayerActionRunner _actions;
        private readonly HireRules _hire;
        /// <summary>고용 시장: 가짜 등록(data/Dev/fakeHireMarket.json, 계정 "npc") + 내가 등록한 것. 메모리만.</summary>
        private readonly List<HireListing> _market = new List<HireListing>();
        private readonly List<HireLogEntry> _hireLog = new List<HireLogEntry>();
        private CloudSave _save;

        public const string FakeMarketFile = "Dev/fakeHireMarket.json";
        public const string NpcAccount = "npc";

        public const string PersistKey = "fake-server";

        /// <param name="persist">가짜 서버의 세이브를 남겨 둘 곳 (없으면 메모리만: 앱을 다시 켜면 새 서버).</param>
        public LocalOnlineService(DefinitionSet defs, IDataSource source, ITimeProvider time, ISaveStore persist = null)
        {
            _defs = defs;
            _source = source;
            _time = time;
            _persist = persist;
            _actions = new PlayerActionRunner(defs);
            _hire = new HireRules(defs);
            LoadFakeMarket();
            if (persist != null && persist.Exists(PersistKey))
                try { _save = OnlineJson.Read<CloudSave>(persist.Read(PersistKey)); } catch (Exception) { _save = null; }
        }

        private void Keep(CloudSave s)
        {
            _save = s;
            _persist?.Write(PersistKey, OnlineJson.Write(s));
        }

        public bool IsAvailable => true;
        public string AccountId => "local";
        public void SignIn(Action<bool> done) => done?.Invoke(true);
        public void GetTime(Action<ServerTimeResponse> done) => done?.Invoke(new ServerTimeResponse { UnixSeconds = _time.UtcNow.ToUnixTimeSeconds() });

        public void LoadSave(Action<CloudSave> done)
        {
            if (_save == null) Keep(new CloudSave { Revision = 1, Json = SaveJson.Write(StartingKitService.CreateNewGame(_defs)), SavedAtUnixSeconds = Now });
            done?.Invoke(Copy(_save));
        }

        /// <summary>처음 가져오기 전용 (R-41): 서버 세이브가 새 게임(Revision 1)일 때만.</summary>
        public void PutSave(SavePutRequest request, Action<SavePutResponse> done)
        {
            LoadSave(_ => { });
            long rev = _save.Revision;
            if (request == null || rev != 1 || request.BaseRevision != rev || SaveJson.Read(request.Json) == null) { done?.Invoke(new SavePutResponse { Ok = false, Current = _save == null ? null : Copy(_save) }); return; }
            Keep(new CloudSave { Revision = rev + 1, Json = request.Json, SavedAtUnixSeconds = Now });
            done?.Invoke(new SavePutResponse { Ok = true, Current = Copy(_save) });
        }

        public void SubmitBattle(BattleSubmission s, Action<BattleVerdict> done)
        {
            LoadSave(_ => { });
            if (s == null || s.BaseRevision != _save.Revision) { done?.Invoke(new BattleVerdict { Valid = false, Reason = VerdictReasons.StaleSave, Save = Copy(_save) }); return; }
            _hireLog.RemoveAll(e => Now - e.AtUnixSeconds >= HireBook.DaySeconds);
            var v = BattleVerifier.VerifySave(_defs, _source, _save, s, Now, out var json, out var at, out var hired, FindListing, AccountId, _hireLog,
                region => TerritoryLedger.DefenseOf(_owners, region));
            if (v.Valid) Keep(new CloudSave { Revision = _save.Revision + 1, Json = json, SavedAtUnixSeconds = at });
            if (v.Valid && TerritoryBattles.Captures(s, v)) TerritoryLedger.Capture(TerritoryData, _owners, TerritoryBattles.RegionOf(s.StepId), AccountId, at, SendMail);
            if (v.Valid && hired != null)
            {
                foreach (var u in hired.Uses) _hireLog.Add(new HireLogEntry { ListingId = u.ListingId, AtUnixSeconds = at });
                foreach (var o in hired.Outcomes) { var l = FindListing(o.ListingId); if (l != null) HireBook.Record(l, o, hired.StepId, at, _defs.Balance.HireRecentMax); }
            }
            v.Save = Copy(_save);
            done?.Invoke(v);
        }

        public void SubmitActions(ActionSubmission s, Action<ActionVerdict> done)
        {
            LoadSave(_ => { });
            if (s == null || s.BaseRevision != _save.Revision) { done?.Invoke(new ActionVerdict { Valid = false, Reason = VerdictReasons.StaleSave, Save = Copy(_save) }); return; }
            // 우편 받기: 내 우편함에 같은 자원·양으로 있어야 (참고 서버와 같은 검사)
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            bool ServerCheck(PlayerAction x)
            {
                if (x?.Kind != ActionKinds.MailClaim) return true;
                var m = _mail.Find(y => y.Id == x.MailId);
                return m != null && m.Resource == x.PayWith && m.Amount == x.Count && claimed.Add(m.Id);
            }
            var v = ActionVerifier.Apply(_actions, _save, s, Now, out var json, out var at, ServerCheck);
            if (v.Valid) { Keep(new CloudSave { Revision = _save.Revision + 1, Json = json, SavedAtUnixSeconds = at }); _mail.RemoveAll(m => claimed.Contains(m.Id)); }
            v.Save = Copy(_save);
            done?.Invoke(v);
        }

        private void LoadFakeMarket()
        {
            if (_source == null || !_source.Exists(FakeMarketFile)) return;
            try
            {
                int n = 0;
                foreach (Newtonsoft.Json.Linq.JObject o in (Newtonsoft.Json.Linq.JArray)Newtonsoft.Json.Linq.JObject.Parse(_source.ReadText(FakeMarketFile))["listings"])
                {
                    var l = new HireListing
                    {
                        Id = "npc-" + (++n), Version = 1, OwnerAccountId = NpcAccount, Kind = o.Value<string>("kind"),
                        Price = o.Value<long>("price"), PayWith = o.Value<string>("payWith"),
                    };
                    if (l.Kind == HireKinds.General)
                    {
                        var g = new Game.Campaign.Roster.GeneralState { GeneralId = o.Value<string>("generalId"), Level = o.Value<int?>("level") ?? 1 };
                        if (!_defs.Generals.ContainsKey(g.GeneralId ?? "")) continue;
                        l.GeneralId = g.GeneralId; l.Level = g.Level; l.SourceId = g.GeneralId;
                        l.SnapshotJson = OnlineJson.Write(g);
                    }
                    else
                    {
                        var u = new Game.Campaign.Roster.UnitState { UnitId = "npc" + n, TroopType = o.Value<string>("troopType"), Troops = o.Value<long>("troops"), Training = JsonUtil.FixOr(o, "training", Game.Foundation.Math.Fix.Zero, FakeMarketFile) };
                        if (!_defs.TroopTypes.ContainsKey(u.TroopType ?? "")) continue;
                        l.TroopType = u.TroopType; l.Troops = u.Troops; l.TrainingRaw = u.Training.Raw; l.SourceId = u.UnitId;
                        l.SnapshotJson = OnlineJson.Write(u);
                    }
                    _market.Add(l);
                }
            }
            catch (Exception) { _market.Clear(); }
        }

        private HireListing FindListing(string id) => _market.Find(l => l.Id == id);

        public void GetListings(string kind, Action<HireListResponse> done)
        {
            var (used, cooldowns) = HireBook.Status(_defs.Balance, _hireLog, Now);
            var r = new HireListResponse { DailyUsed = used, DailyMax = _defs.Balance.HireDailyMax, CooldownUntil = cooldowns };
            foreach (var l in _market) if (kind == null || l.Kind == kind) r.Listings.Add(OnlineJson.Read<HireListing>(OnlineJson.Write(l)));
            done?.Invoke(r);
        }

        public void RegisterHire(HireListingRequest req, Action<HireListingResponse> done)
        {
            LoadSave(_ => { });
            string source = req?.Kind == HireKinds.General ? req.GeneralId : req?.UnitId;
            int existing = _market.FindAll(l => l.OwnerAccountId == AccountId && l.Kind == req?.Kind && l.SourceId != source).Count;
            var r = _hire.Register(SaveJson.Read(_save.Json), req, existing);
            if (r.Ok)
            {
                var old = _market.Find(l => l.OwnerAccountId == AccountId && l.Kind == r.Listing.Kind && l.SourceId == r.Listing.SourceId);
                r.Listing.OwnerAccountId = AccountId;
                r.Listing.CreatedAtUnixSeconds = Now;
                r.Listing.Id = old?.Id ?? "mine-" + (_market.Count + 1);
                r.Listing.Version = (old?.Version ?? 0) + 1;
                if (old != null) _market[_market.IndexOf(old)] = r.Listing; else _market.Add(r.Listing);
            }
            done?.Invoke(r);
        }

        public void Unregister(string listingId, Action<bool?> done) =>
            done?.Invoke(_market.RemoveAll(l => l.Id == listingId && l.OwnerAccountId == AccountId) > 0);

        /// <summary>영토 주인 (가짜: 메모리만, 이긴 영토 전투로).</summary>
        private readonly Dictionary<string, TerritoryOwner> _owners = new Dictionary<string, TerritoryOwner>();

        private readonly List<Mail> _mail = new List<Mail>();
        private Game.Territory.TerritoryDefs _territory;
        private Game.Territory.TerritoryDefs TerritoryData => _territory ?? (_territory = TerritoryBattles.TryLoad(_source));

        public void GetTerritory(Action<TerritoryResponse> done) => done?.Invoke(TerritoryLedger.View(TerritoryData, _owners, AccountId, Now));

        public void CollectTerritory(Action<MailListResponse> done) =>
            done?.Invoke(new MailListResponse { Mail = TerritoryLedger.Collect(TerritoryData, _owners, AccountId, Now, (who, m) => SendMail(who, m)) });

        public void RegisterDefense(DefenseRequest req, Action<DefenseResponse> done)
        {
            LoadSave(_ => { });
            var d = TerritoryDefenses.Snapshot(_defs, SaveJson.Read(_save.Json), req?.Situation, out var error);
            if (d == null) { done?.Invoke(new DefenseResponse { Ok = false, Reason = error }); return; }
            var r = TerritoryLedger.RegisterDefense(_owners, req.RegionId, AccountId, d, Now);
            done?.Invoke(r == null ? new DefenseResponse { Ok = false, Reason = TerritoryDefenses.NotOwner } : new DefenseResponse { Ok = true, Reason = "ok", Defense = r });
        }

        private void SendMail(string account, Mail m)
        {
            if (account != AccountId) return; // 가짜 서버에는 나 말고 다른 유저가 없다
            m.Id = "m-" + (_mail.Count + 1) + "-" + m.CreatedAtUnixSeconds;
            _mail.Add(m);
        }

        /// <summary>우편: 영토 수입만 (가짜 시장의 등록자는 내 등록을 고용하지 않는다).</summary>
        public void GetMail(Action<MailListResponse> done) => done?.Invoke(new MailListResponse { Mail = new List<Mail>(_mail) });

        private long Now => _time.UtcNow.ToUnixTimeSeconds();
        private static CloudSave Copy(CloudSave s) => new CloudSave { Revision = s.Revision, Json = s.Json, SavedAtUnixSeconds = s.SavedAtUnixSeconds };
    }
}
