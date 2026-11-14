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

        /// <summary>영토 주인 목록 (R-46), 내 지역은 쌓인 수입까지 (R-47).</summary>
        TerritoryResponse Territory(string accountId);
        /// <summary>내 지역의 쌓인 수입을 우편으로 (R-47).</summary>
        MailListResponse CollectTerritory(string accountId);
        /// <summary>내 지역에 수비 편성 등록 (R-48).</summary>
        DefenseResponse RegisterDefense(string accountId, DefenseRequest request);
        /// <summary>깃발 꽂기 (R-96).</summary>
        PlantResponse PlantFlag(string accountId, PlantRequest request);

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
        /// 결제 영수증 (R-64): 개발용 가짜 영수증("fake:")을 받을지 (설정 Game:AllowFakeReceipts, 기본 true = 참고·테스트 서버).
        /// 실제 스토어 검증은 백엔드를 정한 뒤 더한다. 쓴 영수증은 지금은 메모리에만 (실제 서버는 저장소에).
        /// </summary>
        public bool AllowFakeReceipts { get; set; } = true;

        /// <summary>마스터 계정 (R-86, 설정 Game:Masters). 개발 행동 dev.*은 여기 있는 계정의 것만 받는다.</summary>
        public MasterAccounts Masters { get; set; } = new MasterAccounts();
        private readonly HashSet<string> _usedReceipts = new HashSet<string>(StringComparer.Ordinal);

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
            if (flagKey != null && _store.WithTerritory(Code, owners => TerritoryFlags.CheckSiege(_territory, _defs, FromJson(save.Json), accountId, owners, flagKey, s.Location, Now)) != null)
                return new BattleVerdict { Valid = false, Reason = VerdictReasons.BadSortie, Save = save };
            var verdict = BattleVerifier.VerifySave(_defs, _source, save, s, Now, out var json, out var at, out var hired, id => _store.GetListing(Code, id), accountId,
                _store.HireLog(Code, accountId, Now), region => _store.WithTerritory(Code, owners => TerritoryLedger.DefenseOf(owners, region)));
            if (!verdict.Valid) { verdict.Save = save; return verdict; }
            var (ok, current) = _store.PutSave(Code, accountId, save.Revision, json, at);
            if (!ok) return new BattleVerdict { Valid = false, Reason = VerdictReasons.StaleSave, Save = current };
            if (flagKey != null)
            {
                var after = FromJson(json);
                _store.WithTerritory(Code, owners => { TerritoryFlags.AfterSiege(_territory, _defs, owners, flagKey, verdict.Winner == StoryFlow.PlayerTeam, accountId, after, TerritoryFlags.AttackerGeneral(s, after), at, (who, m) => _store.AddMail(Code, who, m)); return 0; });
            }
            else if (TerritoryBattles.Captures(s, verdict))
                _store.WithTerritory(Code, owners => { TerritoryLedger.Capture(_territory, owners, TerritoryBattles.RegionOf(s.StepId), accountId, at, (who, m) => _store.AddMail(Code, who, m)); return 0; });
            // 고용 보상: 등록자 우편으로 (등록자 세이브는 직접 고치지 않는다 — 받을 때 행동 mail.claim)
            if (hired != null)
            {
                var log = new List<HireLogEntry>();
                foreach (var u in hired.Uses) log.Add(new HireLogEntry { ListingId = u.ListingId, AtUnixSeconds = at });
                _store.AddHireLog(Code, accountId, log);
                foreach (var o in hired.Outcomes) _store.RecordHire(Code, o.ListingId, o, hired.StepId, at, _defs.Balance.HireRecentMax);
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
            var receipts = new ReceiptCheck(AllowFakeReceipts, _usedReceipts);
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
            return new ActionVerdict { Valid = true, Reason = VerdictReasons.Ok, Save = current };
        }

        public HireListingResponse RegisterHire(string accountId, HireListingRequest req)
        {
            var state = FromJson(LoadOrCreate(accountId).Json);
            string source = req?.Kind == HireKinds.General ? req.GeneralId : req?.UnitId;
            int existing = _store.Listings(Code, req?.Kind).FindAll(l => l.OwnerAccountId == accountId && l.SourceId != source).Count;
            var r = _hire.Register(state, req, existing);
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
        public TerritoryResponse Territory(string accountId)
        {
            var alliances = _store.WithAlliances(Code, a => new Dictionary<string, Alliance>(a));
            return _store.WithTerritory(Code, owners => TerritoryLedger.View(_territory, owners, accountId, Now, who => AllianceBook.Of(alliances, who)));
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
            var r = _store.WithTerritory(Code, owners => TerritoryLedger.RegisterDefense(owners, req.RegionId, accountId, d, Now));
            return r == null ? new DefenseResponse { Ok = false, Reason = TerritoryDefenses.NotOwner } : new DefenseResponse { Ok = true, Reason = "ok", Defense = r };
        }

        public PlantResponse PlantFlag(string accountId, PlantRequest req)
        {
            var state = FromJson(LoadOrCreate(accountId).Json);
            var alliances = _store.WithAlliances(Code, a => new Dictionary<string, Alliance>(a));
            return _store.WithTerritory(Code, owners => TerritoryFlags.Plant(_territory, _defs, state, accountId, who => AllianceBook.Of(alliances, who)?.Id, owners, req, Now));
        }

        public MailListResponse CollectTerritory(string accountId) =>
            new MailListResponse { Mail = _store.WithTerritory(Code, owners => TerritoryLedger.Collect(_territory, owners, accountId, Now, (who, m) => _store.AddMail(Code, who, m))) };

        public static string ToJson(CampaignState state) => SaveJson.Write(state);
        public static CampaignState FromJson(string json) => SaveJson.Read(json);
    }
}
