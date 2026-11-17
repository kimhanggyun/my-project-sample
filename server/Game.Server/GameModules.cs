using System;
using System.Collections.Generic;
using Game.Campaign;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Flow;
using Game.Foundation.Time;
using Game.Online;

namespace Game.Server
{
    /// <summary>
    /// 게임 하나의 서버 처리 (docs/impl/server.md 4.5). 게임을 늘리면 이 경계를 구현해 등록표에 더한다 (기존 코드 수정 없음).
    /// 공통 계정·토큰은 플랫폼 저장소, 세이브는 (게임 코드, 계정)별 게임 저장소.
    /// </summary>
    public interface IGameModule
    {
        /// <summary>직접 정한 게임 코드 (주 키, 예: "ancientwar").</summary>
        string Code { get; }
        /// <summary>이 게임으로 연결되는 앱 id (안드로이드 package명·iOS 번들 id). 키로 쓰지 않고 찾기만.</summary>
        IReadOnlyList<string> PackageIds { get; }
        CloudSave LoadOrCreate(string accountId);
        SavePutResponse Put(string accountId, SavePutRequest request);
        BattleVerdict SubmitBattle(string accountId, BattleSubmission submission);
        /// <summary>기기 행동(편성·구매·창설 …)을 서버 세이브에 다시 적용한다 (R-41).</summary>
        ActionVerdict SubmitActions(string accountId, ActionSubmission submission);

        // ---- 고용 (R-43, hiring.md) ----
        /// <summary>내 서버 세이브의 장수·부대를 등록 (같은 대상이면 갱신).</summary>
        HireListingResponse RegisterHire(string accountId, HireListingRequest request);
        /// <summary>등록 목록 (kind = general / unit / null = 전부).</summary>
        /// <param name="accountId">요청한 계정 (하루 고용 수·대기시간을 채운다).</param>
        HireListResponse Listings(string kind, string accountId);
        bool Unregister(string accountId, string listingId);
        MailListResponse MailOf(string accountId);
        /// <summary>출석 (하루 한 번, 서버 시각, R-112): 받으면 보상 우편.</summary>
        AttendanceResponse CheckIn(string accountId);

        // ---- 푸시 (R-119) ----
        PushSettings PushSettingsOf(string accountId);
        PushSettings SetPushSettings(string accountId, PushSettings settings);
        /// <summary>기기 토큰 등록 · 지우기. 토큰이 비었으면 false.</summary>
        bool RegisterPushToken(string accountId, PushTokenRequest request);
        /// <summary>수입이 다 찬 깃발 주인에게 알림 (관리자 · 예약 호출, R-119). 보낸 기기 수.</summary>
        int SweepIncomeFull();
        /// <summary>내 용병이 고용된 전투 녹화 (R-120): 그 전투의 등록자 · 고용한 사람만. 아니면 null.</summary>
        HireReplayResponse HireReplay(string accountId, string id);

        /// <summary>영토 주인 목록 (R-46), 내 지역은 쌓인 수입까지 (R-47).</summary>
        TerritoryResponse Territory(string accountId);
        /// <summary>내 지역의 쌓인 수입을 우편으로 (R-47).</summary>
        /// <param name="regionId">이 지역 · 깃발만 (null = 모두).</param>
        MailListResponse CollectTerritory(string accountId, string regionId = null);
        /// <summary>내 지역에 수비 편성 등록 (R-48).</summary>
        DefenseResponse RegisterDefense(string accountId, DefenseRequest request);
        /// <summary>깃발 꽂기 (R-96).</summary>
        PlantResponse PlantFlag(string accountId, PlantRequest request);
        /// <summary>깃발에서 장수 회수 (R-109).</summary>
        RecallResponse RecallFlag(string accountId, RecallRequest request);
        /// <summary>공성전 준비 시작 · 준비 중 취소 (R-113).</summary>
        SiegeResponse StartSiege(string accountId, SiegeRequest request);
        bool CancelSiege(string accountId, string flagKey);

        // ---- 연합 (R-49) ----
        AllianceListResponse Alliances(string accountId);
        AllianceResponse CreateAlliance(string accountId, AllianceRequest request);
        AllianceResponse JoinAlliance(string accountId, string allianceId);
        AllianceResponse LeaveAlliance(string accountId);
        AllianceResponse ManageAlliance(string accountId, AllianceManageRequest request);

        // ---- 녹화 공유 (R-81) ----
        ShareReplayResponse ShareReplay(string accountId, string nickname, ShareReplayRequest request);
        SharedReplayListResponse SharedReplays(string stepId);
        SharedReplayResponse SharedReplay(string id);
    }

    /// <summary>게임 등록표: 게임 코드·앱 id → 게임.</summary>
    public sealed class GameRegistry
    {
        private readonly Dictionary<string, IGameModule> _byCode = new Dictionary<string, IGameModule>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _byPackage = new Dictionary<string, string>(StringComparer.Ordinal);

        public GameRegistry Register(IGameModule game)
        {
            if (_byCode.ContainsKey(game.Code)) throw new ArgumentException("같은 게임 코드가 두 번 등록됨: " + game.Code);
            _byCode[game.Code] = game;
            foreach (var p in game.PackageIds)
            {
                if (_byPackage.ContainsKey(p)) throw new ArgumentException("같은 앱 id가 두 게임에 연결됨: " + p);
                _byPackage[p] = game.Code;
            }
            return this;
        }

        public IGameModule Find(string code) => code != null && _byCode.TryGetValue(code, out var g) ? g : null;
        public string CodeOfPackage(string packageId) => packageId != null && _byPackage.TryGetValue(packageId, out var c) ? c : null;

        public IReadOnlyList<string> Codes
        {
            get
            {
                var list = new List<string>(_byCode.Keys);
                list.Sort(StringComparer.Ordinal);
                return list;
            }
        }
    }

    /// <summary>
    /// 이 저장소의 전략 전투 게임. 세이브는 서버가 주인: 새 가입이면 새 게임, 전투 결과는 서버가 검증·반영 (R-40).
    /// </summary>
    public sealed class StrategyGameModule : IGameModule
    {
        private readonly DefinitionSet _defs;
        private readonly IDataSource _source;
        private readonly ITimeProvider _time;
        private readonly IGameStore _store;
        private readonly PlayerActionRunner _actions;
        private readonly HireRules _hire;
        private readonly Game.Territory.TerritoryDefs _territory;

        /// <summary>
        /// 결제 영수증 검증기 (R-64 · R-114): 설정 Game:ReceiptVerifier (fake 기본 · off · googlePlay). 참고 · 테스트 서버는 가짜 영수증("fake:")을 받는다.
        /// </summary>
        public IReceiptVerifier Receipts { get; set; } = new FakeReceiptVerifier();

        /// <summary>마스터 계정 (R-86, 설정 Game:Masters). 개발 행동 dev.*은 여기 있는 계정의 것만 받는다.</summary>
        public MasterAccounts Masters { get; set; } = new MasterAccounts();

        public StrategyGameModule(string code, IReadOnlyList<string> packageIds, IDataSource source, string theme, ITimeProvider time, IGameStore store)
        {
            Code = code;
            PackageIds = packageIds ?? Array.Empty<string>();
            _source = source;
            _defs = DefinitionSet.Load(source, theme);
            _time = time;
            _store = store;
            _actions = new PlayerActionRunner(_defs);
            _hire = new HireRules(_defs);
            _territory = TerritoryBattles.TryLoad(source);
            _replays = new ReplayService(new StoryFlow(_defs, source, time));
            _replayRules = ReplayRules.Load(source);
        }

        private readonly ReplayService _replays;
        private readonly ReplayRules _replayRules;

        /// <summary>녹화 공유 (R-81): 지금 규칙으로 다시 돌려 같을 때만 받는다.</summary>
        public ShareReplayResponse ShareReplay(string accountId, string nickname, ShareReplayRequest req)
        {
            var res = Game.Flow.SharedReplays.Validate(_replays, req?.ReplayJson, out _);
            if (!res.Ok) return res;
            res.Replay.OwnerAccountId = accountId;
            res.Replay.OwnerName = nickname;
            res.Replay.SharedAtUnixSeconds = Now;
            res.Replay = _store.AddSharedReplay(Code, res.Replay, req.ReplayJson, _replayRules.SharedStoreMax);
            return res;
        }

        public SharedReplayListResponse SharedReplays(string stepId) =>
            new SharedReplayListResponse { Items = Game.Flow.SharedReplays.Pick(_store.SharedReplays(Code), stepId, _replayRules.SharedListMax) };

        public SharedReplayResponse SharedReplay(string id)
        {
            var (s, json) = _store.GetSharedReplay(Code, id);
            return s == null ? null : new SharedReplayResponse { Replay = s, ReplayJson = json };
        }

        public string Code { get; }
        public IReadOnlyList<string> PackageIds { get; }
        private long Now => _time.UtcNow.ToUnixTimeSeconds();

        public CloudSave LoadOrCreate(string accountId)
        {
            var save = _store.GetSave(Code, accountId);
            if (save != null) return save;
            var (ok, current) = _store.PutSave(Code, accountId, 0, ToJson(StartingKitService.CreateNewGame(_defs)), Now);
            return ok ? current : _store.GetSave(Code, accountId);
        }

        /// <summary>
        /// 처음 연결 때 기기 진행 가져오기 전용 (R-41): 서버 세이브가 아직 새 게임(Revision 1)일 때만, 형식이 맞는 세이브만.
        /// 그 뒤의 진행은 전투 제출(R-40)·행동 제출(R-41)로만 바뀐다.
        /// </summary>
        public SavePutResponse Put(string accountId, SavePutRequest req)
        {
            var save = LoadOrCreate(accountId);
            if (save.Revision != 1 || req?.Json == null || FromJson(req.Json) == null) return new SavePutResponse { Ok = false, Current = save };
            var (ok, current) = _store.PutSave(Code, accountId, req.BaseRevision, req.Json, Now);
            return new SavePutResponse { Ok = ok, Current = current };
        }

        public BattleVerdict SubmitBattle(string accountId, BattleSubmission s)
        {
            var save = LoadOrCreate(accountId);
            if (s == null || s.BaseRevision != save.Revision)
                return new BattleVerdict { Valid = false, Reason = VerdictReasons.StaleSave, Save = save };
            // 깃발 공성전 (R-97): 깃발 · 보호 · 범위 검사
            string flagKey = TerritoryBattles.IsTerritoryStep(s.StepId) && Game.Territory.FlagDefs.IsFlagKey(TerritoryBattles.RegionOf(s.StepId)) ? TerritoryBattles.RegionOf(s.StepId) : null;
            if (flagKey != null && _store.WithTerritory(Code, TerritoryScope.Keys(flagKey), owners => TerritoryFlags.CheckSiege(_territory, _defs, FromJson(save.Json), accountId, owners, flagKey, s.Location, Now)
                    ?? TerritoryFlags.CheckSiegeReady(_territory, owners, flagKey, accountId, Now)) != null) // 준비 1분 (R-113)
                return new BattleVerdict { Valid = false, Reason = VerdictReasons.BadSortie, Save = save };
            var verdict = BattleVerifier.VerifySave(_defs, _source, save, s, Now, out var json, out var at, out var hired, id => _store.GetListing(Code, id), accountId,
                _store.HireLog(Code, accountId, Now), region => _store.WithTerritory(Code, TerritoryScope.Keys(region), owners => TerritoryLedger.DefenseOf(owners, region)));
            if (!verdict.Valid) { verdict.Save = save; return verdict; }
            var (ok, current) = _store.PutSave(Code, accountId, save.Revision, json, at);
            if (!ok) return new BattleVerdict { Valid = false, Reason = VerdictReasons.StaleSave, Save = current };
            if (flagKey != null)
            {
                var after = FromJson(json);
                _store.WithTerritory(Code, TerritoryScope.Keys(flagKey), owners => { TerritoryFlags.AfterSiege(_territory, _defs, owners, flagKey, verdict.Winner == StoryFlow.PlayerTeam, accountId, after, TerritoryFlags.AttackerGeneral(s, after), at, (who, m) => _store.AddMail(Code, who, m), TakeAlert); return 0; });
            }
            else if (TerritoryBattles.Captures(s, verdict))
                _store.WithTerritory(Code, TerritoryScope.Keys(TerritoryBattles.RegionOf(s.StepId)), owners => { TerritoryLedger.Capture(_territory, owners, TerritoryBattles.RegionOf(s.StepId), accountId, at, (who, m) => _store.AddMail(Code, who, m)); return 0; });
            // 고용 보상: 등록자 우편으로 (등록자 세이브는 직접 고치지 않는다 — 받을 때 행동 mail.claim)
            if (hired != null)
            {
                var log = new List<HireLogEntry>();
                foreach (var u in hired.Uses) log.Add(new HireLogEntry { ListingId = u.ListingId, AtUnixSeconds = at });
                _store.AddHireLog(Code, accountId, log);
                // 그 전투 녹화를 등록자들 · 고용한 사람이 볼 수 있게 (R-120)
                string replayId = null;
                if (hired.Replay != null && hired.Outcomes.Count > 0)
                {
                    hired.Replay.SavedAtUnixSeconds = at;
                    var viewers = new HashSet<string>(StringComparer.Ordinal) { accountId };
                    foreach (var o in hired.Outcomes) { var l = _store.GetListing(Code, o.ListingId); if (l?.OwnerAccountId != null) viewers.Add(l.OwnerAccountId); }
                    replayId = _store.AddHireReplay(Code, ReplayService.Write(hired.Replay), viewers, at, HireReplayMax);
                }
                foreach (var o in hired.Outcomes) _store.RecordHire(Code, o.ListingId, o, hired.StepId, at, _defs.Balance.HireRecentMax, replayId);
            }
            if (hired != null)
                foreach (var c in hired.Charges)
                {
                    if (hired.BetrayedOwners.Contains(c.OwnerAccountId)) continue; // 배신한 용병은 보상 없음 (R-99)
                    long reward = _hire.OwnerReward(c.Cost);
                    if (reward > 0) _store.AddMail(Code, c.OwnerAccountId, new Mail { Kind = MailKinds.HireReward, Resource = c.Resource, Amount = reward, ListingId = c.ListingId, CreatedAtUnixSeconds = Now });
                }
            verdict.Save = current;
            return verdict;
        }

        public ActionVerdict SubmitActions(string accountId, ActionSubmission s)
        {
            var save = LoadOrCreate(accountId);
            if (s == null || s.BaseRevision != save.Revision)
                return new ActionVerdict { Valid = false, Reason = VerdictReasons.StaleSave, Save = save };
            // 우편 받기: 내 우편함에 같은 자원·양으로 있어야 하고, 한 묶음에서 두 번 받을 수 없다
            var box = _store.MailOf(Code, accountId);
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            var receipts = new ReceiptCheck(Receipts, tx => _store.ReceiptOf(Code, tx) != null) // 쓴 거래는 저장소에 (서버를 다시 띄워도 남음, R-116)
            {
                OwnerOf = tx => _store.ReceiptOf(Code, tx)?.AccountId, // 복원: 이미 반영한 계정 (R-117)
                AccountId = accountId,
            };
            bool ServerCheck(PlayerAction a)
            {
                if (!receipts.Check(a)) return false;
                if (ActionKinds.IsDev(a?.Kind)) return Masters.Contains(accountId);
                if (a?.Kind != ActionKinds.MailClaim) return true;
                var m = box.Find(x => x.Id == a.MailId);
                return m != null && m.Resource == a.PayWith && m.Amount == a.Count && claimed.Add(m.Id);
            }
            var v = ActionVerifier.Apply(_actions, save, s, Now, out var json, out var at, ServerCheck);
            if (!v.Valid) { v.Save = save; return v; }
            var (ok, current) = _store.PutSave(Code, accountId, save.Revision, json, at);
            if (!ok) return new ActionVerdict { Valid = false, Reason = VerdictReasons.StaleSave, Save = current };
            foreach (var id in claimed) _store.TakeMail(Code, accountId, id);
            if (receipts.Pending.Count > 0) SettleReceipts(accountId, receipts.Pending, at);
            return new ActionVerdict { Valid = true, Reason = VerdictReasons.Ok, Save = current };
        }

        /// <summary>반영한 결제를 저장소에 남기고 스토어에 알린다 (영구 = acknowledge, 소모성 = consume, R-116). 알리기 실패는 지급에 영향 없음.</summary>
        private void SettleReceipts(string accountId, List<ReceiptRecord> pending, long at)
        {
            foreach (var r in pending) { r.AccountId = accountId; r.AtUnixSeconds = at; }
            _store.RecordReceipts(Code, pending);
            if (!(Receipts is IReceiptSettler settler)) return;
            foreach (var r in pending)
            {
                _defs.Products.TryGetValue(r.ProductId ?? "", out var p);
                if (!settler.Settle(r.ProductId, r.Receipt, consumable: p == null || !p.Permanent))
                    Console.Error.WriteLine($"[결제] 스토어 확인 처리 실패: 상품 {r.ProductId}, 거래 {r.TransactionId} (3일 안에 다시 확인 필요)");
            }
        }

        public HireListingResponse RegisterHire(string accountId, HireListingRequest req)
        {
            var state = FromJson(LoadOrCreate(accountId).Json);
            string source = req?.Kind == HireKinds.General ? req.GeneralId : req?.UnitId;
            int existing = _store.Listings(Code, req?.Kind).FindAll(l => l.OwnerAccountId == accountId && l.SourceId != source).Count;
            var onFlags = _store.WithTerritory(Code, TerritoryScope.Owner(accountId), owners => TerritoryFlags.GeneralsOnFlags(owners, accountId)); // 깃발에 나간 닉네임 장수는 등록 불가 (R-110)
            var r = _hire.Register(state, req, existing, onFlags);
            if (!r.Ok) return r;
            r.Listing.OwnerAccountId = accountId;
            r.Listing.CreatedAtUnixSeconds = Now;
            r.Listing = _store.UpsertListing(Code, r.Listing);
            return r;
        }

        public HireListResponse Listings(string kind, string accountId)
        {
            var (used, cooldowns) = HireBook.Status(_defs.Balance, _store.HireLog(Code, accountId, Now), Now);
            return new HireListResponse { Listings = _store.Listings(Code, kind), DailyUsed = used, DailyMax = _defs.Balance.HireDailyMax, CooldownUntil = cooldowns };
        }
        public bool Unregister(string accountId, string listingId) => _store.RemoveListing(Code, accountId, listingId);
        public MailListResponse MailOf(string accountId) => new MailListResponse { Mail = _store.MailOf(Code, accountId) };

        /// <summary>푸시 보내기 (R-119, 설정 Game:Push, 기본 꺼짐).</summary>
        public IPushSender PushSender { get => _pushSender; set { _pushSender = value; _push = null; } }
        private IPushSender _pushSender = new OffPushSender();
        private PushService _push;
        private PushService Push => _push ?? (_push = new PushService(Code, _store, _pushSender, _defs));

        /// <summary>한 계정이 둘 수 있는 기기 토큰 수 (오래된 것부터 지움).</summary>
        public const int MaxPushTokens = 5;

        public PushSettings PushSettingsOf(string accountId) => _store.WithPush(Code, accountId, p => p.Settings);

        public PushSettings SetPushSettings(string accountId, PushSettings settings) =>
            _store.WithPush(Code, accountId, p => { if (settings != null) p.Settings = settings; return p.Settings; });

        public bool RegisterPushToken(string accountId, PushTokenRequest req)
        {
            string token = req?.Token?.Trim();
            if (string.IsNullOrEmpty(token) || token.Length > 4096) return false;
            return _store.WithPush(Code, accountId, p =>
            {
                p.Tokens.RemoveAll(t => t.Token == token);
                if (!req.Remove)
                {
                    p.Tokens.Add(new PushTokenEntry { Token = token, Platform = req.Platform, AtUnixSeconds = Now });
                    while (p.Tokens.Count > MaxPushTokens) p.Tokens.RemoveAt(0);
                }
                return true;
            });
        }

        /// <summary>남겨 두는 고용된 전투 녹화 수 (게임마다, 오래된 것부터 지움, R-120).</summary>
        public const int HireReplayMax = 500;

        public HireReplayResponse HireReplay(string accountId, string id)
        {
            string json = _store.HireReplayFor(Code, id, accountId);
            return json == null ? null : new HireReplayResponse { Id = id, ReplayJson = json };
        }

        // ---- 영토 장부 범위 (db.md 4장): 요청마다 필요한 깃발만 ----
        /// <summary>가장 큰 깃발 반지름 (모든 계급 중) — 이 거리 안의 깃발만 꽂기 · 공성전 대상이 될 수 있다.</summary>
        private long MaxFlagRadiusM
        {
            get
            {
                long max = 0;
                foreach (var r in _defs.Campaign.Ranks) max = Math.Max(max, r.FlagRadiusM);
                return max;
            }
        }

        /// <summary>
        /// 꽂기: 꽂을 칸에서 가장 큰 반지름 안의 깃발(같은 칸 · 남의 원 안 · 우리 연합) + 내 깃발(장수가 다른 깃발에 있는지).
        /// 추가 파견(FlagKey)은 그 깃발 + 내 깃발.
        /// </summary>
        private TerritoryScope PlantScope(string accountId, PlantRequest req)
        {
            var mine = TerritoryScope.Owner(accountId);
            if (!string.IsNullOrEmpty(req?.FlagKey)) return mine.Or(TerritoryScope.Keys(req.FlagKey));
            string at = !string.IsNullOrEmpty(req?.Target) ? req.Target : req?.Location;
            if (_territory?.Flags == null || !Game.Foundation.Geo.GeoPoint.TryParse(at, out var p)) return mine;
            return mine.Or(TerritoryScope.Near(_territory.Flags, p, MaxFlagRadiusM));
        }

        /// <summary>수입 가득 훑기: 받은 지 쌓이는 최대 시간이 지난 깃발만.</summary>
        private TerritoryScope IncomeDueScope() =>
            _territory?.Flags == null ? TerritoryScope.Keys() : TerritoryScope.IncomeDue(Now - _territory.Flags.IncomeCapHours * 3600);

        public int SweepIncomeFull() => Push.Send(_store.WithTerritory(Code, IncomeDueScope(), owners => PushRules.IncomeFullDue(_territory, owners, Now)));

        public AttendanceResponse CheckIn(string accountId) =>
            _store.WithAttendance(Code, accountId, rec => new DailyEconomy(_defs).CheckIn(rec, Now, m => _store.AddMail(Code, accountId, m)));
        public TerritoryResponse Territory(string accountId)
        {
            var alliances = _store.WithAlliances(Code, a => new Dictionary<string, Alliance>(a));
            return _store.WithTerritory(Code, TerritoryScope.All(), owners => TerritoryLedger.View(_territory, owners, accountId, Now, who => AllianceBook.Of(alliances, who)));
        }

        public AllianceListResponse Alliances(string accountId) => _territory == null ? new AllianceListResponse() : _store.WithAlliances(Code, a => AllianceBook.List(_territory, a, accountId));

        public AllianceResponse CreateAlliance(string accountId, AllianceRequest req) =>
            _territory == null ? new AllianceResponse { Ok = false, Reason = AllianceReasons.NotFound }
                : _store.WithAlliances(Code, a => AllianceBook.Create(_territory, a, accountId, req?.Name, _store.NewKey("a-"), Now));

        public AllianceResponse JoinAlliance(string accountId, string allianceId) =>
            _territory == null ? new AllianceResponse { Ok = false, Reason = AllianceReasons.NotFound }
                : _store.WithAlliances(Code, a => AllianceBook.Join(_territory, a, accountId, allianceId));

        public AllianceResponse LeaveAlliance(string accountId) => _store.WithAlliances(Code, a => AllianceBook.Leave(a, accountId));

        public AllianceResponse ManageAlliance(string accountId, AllianceManageRequest req) =>
            _territory == null ? new AllianceResponse { Ok = false, Reason = AllianceReasons.NotFound }
                : _store.WithAlliances(Code, a => AllianceBook.Manage(_territory, a, accountId, req));

        public DefenseResponse RegisterDefense(string accountId, DefenseRequest req)
        {
            var d = TerritoryDefenses.Snapshot(_defs, FromJson(LoadOrCreate(accountId).Json), req?.Situation, out var error);
            if (d == null) return new DefenseResponse { Ok = false, Reason = error };
            var r = _store.WithTerritory(Code, TerritoryScope.Keys(req.RegionId), owners => TerritoryLedger.RegisterDefense(owners, req.RegionId, accountId, d, Now));
            return r == null ? new DefenseResponse { Ok = false, Reason = TerritoryDefenses.NotOwner } : new DefenseResponse { Ok = true, Reason = "ok", Defense = r };
        }

        public PlantResponse PlantFlag(string accountId, PlantRequest req)
        {
            var state = FromJson(LoadOrCreate(accountId).Json);
            var alliances = _store.WithAlliances(Code, a => new Dictionary<string, Alliance>(a));
            return _store.WithTerritory(Code, PlantScope(accountId, req), owners => TerritoryFlags.Plant(_territory, _defs, state, accountId, who => AllianceBook.Of(alliances, who)?.Id, owners, req, Now));
        }

        public SiegeResponse StartSiege(string accountId, SiegeRequest req)
        {
            var state = FromJson(LoadOrCreate(accountId).Json);
            var pushes = new List<PushMessage>();
            var res = _store.WithTerritory(Code, TerritoryScope.Keys(req?.FlagKey), owners => TerritoryFlags.StartSiege(_territory, _defs, state, accountId, owners, req, Now, (who, m) =>
            {
                _store.AddMail(Code, who, m);
                // 공성전 준비 시작 = 수비자 · 그 연합원에게 푸시 (R-119)
                if (m.Kind == MailKinds.SiegeAlert && owners.TryGetValue(m.RegionId ?? "", out var flag))
                    pushes.AddRange(PushRules.ForSiegeStart(flag, accountId, _store.WithAlliances(Code, a => AllianceBook.Of(a, who)), null));
            }));
            Push.Send(pushes);
            return res;
        }

        public bool CancelSiege(string accountId, string flagKey) =>
            _store.WithTerritory(Code, TerritoryScope.Keys(flagKey), owners => TerritoryFlags.CancelSiege(_territory, owners, flagKey, accountId, Now, TakeAlert));

        /// <summary>수비자의 그 깃발 공성전 알림 우편을 지운다 (공성전이 끝나거나 취소, R-113).</summary>
        private void TakeAlert(string who, string flagKey)
        {
            foreach (var m in _store.MailOf(Code, who))
                if (m.Kind == MailKinds.SiegeAlert && m.RegionId == flagKey) _store.TakeMail(Code, who, m.Id);
        }

        public RecallResponse RecallFlag(string accountId, RecallRequest req)
        {
            var state = FromJson(LoadOrCreate(accountId).Json);
            return _store.WithTerritory(Code, TerritoryScope.Keys(req?.FlagKey), owners => TerritoryFlags.Recall(_territory, _defs, state, accountId, owners, req, Now, (who, m) => _store.AddMail(Code, who, m)));
        }

        public MailListResponse CollectTerritory(string accountId, string regionId = null) =>
            new MailListResponse { Mail = _store.WithTerritory(Code, TerritoryScope.Owner(accountId), owners => TerritoryLedger.Collect(_territory, owners, accountId, Now, (who, m) => _store.AddMail(Code, who, m), regionId)) };

        public static string ToJson(CampaignState state) => SaveJson.Write(state);
        public static CampaignState FromJson(string json) => SaveJson.Read(json);
    }
}
