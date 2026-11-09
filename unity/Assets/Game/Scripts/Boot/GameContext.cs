using System;
using System.Collections.Generic;
using Game.Campaign;
using Game.Campaign.Save;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Economy;
using Game.Flow;
using Game.Foundation.Save;
using Game.Foundation.Time;
using Game.Online;
using Game.Services;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Unity.Boot
{
    /// <summary>
    /// 조립 지점(composition root)이 만든 것들을 화면에 넘기는 묶음. 화면은 규칙을 직접 계산하지 않고 여기의 규칙 객체를 부른다.
    /// 엔진 쪽에서 만드는 것: 데이터 소스, 세이브 저장소(기기 폴더), 시간, 외부 서비스 구현.
    /// </summary>
    public sealed class GameContext
    {
        public const string ThemeId = "ancientwar";

        public DefinitionSet Defs { get; private set; }
        public IDataSource Data { get; private set; }
        public ITimeProvider Time { get; private set; }
        public StoryFlow Flow { get; private set; }
        public CampaignSaveService Saves { get; private set; }
        public CampaignState State { get; private set; }
        /// <summary>메인 화면 하단 탭 (Ui/mainTabs.json).</summary>
        public IReadOnlyList<MainTabEntry> MainTabs { get; private set; }

        private Game.Campaign.Roster.FormationEditor _formation;
        /// <summary>편성 탭에서 고친 상황 (null = 기본 편성).</summary>
        public string FormationSituation { get; private set; }
        /// <summary>편성 편집 중인 사본 (화면을 오가도 유지). 저장·되돌리기 전까지 출진에는 저장된 편성이 쓰인다.</summary>
        public Game.Campaign.Roster.FormationEditor Formation => _formation ?? (_formation = new Game.Campaign.Roster.FormationEditor(Defs, State, FormationSituation));
        public void DiscardFormationDraft() => _formation = null;

        /// <summary>편성 탭의 상황 바꾸기 (고치던 사본은 버린다).</summary>
        public void SelectFormationSituation(string situation)
        {
            FormationSituation = situation;
            _formation = null;
        }

        public IAuthService Auth { get; private set; }
        public IStoreService Store { get; private set; }
        public IRewardedAdService RewardedAd { get; private set; }
        public IBannerAdService BannerAd { get; private set; }
        /// <summary>위치 (영토 탭에서만, territory.md 3장).</summary>
        public ILocationService Location { get; private set; }
        /// <summary>영토 지역 데이터 (없거나 읽지 못하면 null → 영토 탭 잠김).</summary>
        public Game.Territory.TerritoryDefs Territory { get; private set; }
        /// <summary>서비스 id → 고른 구현 이름 (설정 화면 표시용).</summary>
        public IReadOnlyDictionary<string, string> ServiceChoices { get; private set; }

        // ---- 온라인 (docs/impl/server.md 5장) ----
        /// <summary>서버 시각 (서버에 연결되면 맞춘다). 시간 규칙은 이 시각으로 계산한다.</summary>
        public ServerClock Clock { get; private set; }
        public IOnlineService Online { get; private set; }
        public CloudSync Sync { get; private set; }
        /// <summary>전투 밖 행동(편성·구매·창설 …)의 규칙. 기기와 서버가 같은 것을 쓴다 (R-41).</summary>
        public PlayerActionRunner Actions { get; private set; }
        /// <summary>설정 이름: off / fake / http.</summary>
        public string OnlineMode { get; private set; } = OnlineConfig.Off;
        public OnlineLink Link { get; private set; } = new OnlineLink();
        /// <summary>마지막 동기화 결과 (SyncOutcomes, 없으면 null).</summary>
        public string OnlineStatus { get; private set; }
        public bool OnlineEnabled => OnlineMode != OnlineConfig.Off;
        private ISaveStore _saveStore;

        /// <summary>데이터·세이브·서비스를 준비한다. 실패하면 예외 (부팅 화면이 오류로 보여준다).</summary>
        public static GameContext Create(IDataSource data, IDataSource config, ISaveStore saveStore, ITimeProvider time, string serviceConfigFile,
            Func<string, IOnlineTransport> transportFactory = null)
        {
            var clock = new ServerClock(time);
            var c = new GameContext { Data = data, Time = clock, Clock = clock, _saveStore = saveStore };
            c.Defs = DefinitionSet.Load(data, ThemeId);
            c.Flow = new StoryFlow(c.Defs, data, clock);
            c.Saves = new CampaignSaveService(saveStore);
            c.State = c.Saves.Load() ?? StartingKitService.CreateNewGame(c.Defs);
            c.MainTabs = MainTabEntry.Load(data);
            c.Actions = new PlayerActionRunner(c.Defs);

            var choices = ReadServiceChoices(config, serviceConfigFile);
            var cfg = new ServiceConfig(choices);
            var reg = ServiceRegistry.WithBuiltIns();
            // 실제 SDK 구현은 여기서 reg.Register(...)로 더한다 (설정에서 이름으로 고른다).
            c.Auth = reg.Create<IAuthService>(ServiceIds.Auth, cfg);
            c.Store = reg.Create<IStoreService>(ServiceIds.Store, cfg);
            c.RewardedAd = reg.Create<IRewardedAdService>(ServiceIds.RewardedAd, cfg);
            c.BannerAd = reg.Create<IBannerAdService>(ServiceIds.BannerAd, cfg);
            c.Location = reg.Create<ILocationService>(ServiceIds.Location, cfg);
            if (c.Location is FakeLocationService fakeLoc && Game.Foundation.Geo.GeoPoint.TryParse(cfg.Value(ServiceIds.FakeLocationKey), out var fakePoint)) fakeLoc.Point = fakePoint;
            try { c.Territory = data.Exists(Game.Territory.TerritoryDefs.File) ? Game.Territory.TerritoryDefs.Load(data) : null; }
            catch (Exception e) { Debug.LogWarning("영토 데이터를 읽지 못해 영토 탭을 잠급니다: " + e.Message); c.Territory = null; }
            var shown = new Dictionary<string, string>();
            foreach (var id in new[] { ServiceIds.Auth, ServiceIds.Store, ServiceIds.RewardedAd, ServiceIds.BannerAd, ServiceIds.Location }) shown[id] = cfg.ChoiceFor(id);
            shown[OnlineConfig.Key] = cfg.ChoiceFor(OnlineConfig.Key);
            c.ServiceChoices = shown;

            // 온라인: off(서버 없음) / fake(기기 안 가짜 서버) / http(참고 서버·실제 서버)
            c.OnlineMode = cfg.ChoiceFor(OnlineConfig.Key);
            c.Link = c.ReadLink();
            if (c.OnlineMode == OnlineConfig.Fake) c.Online = new LocalOnlineService(c.Defs, data, clock, saveStore);
            else if (c.OnlineMode == OnlineConfig.Http && transportFactory != null && choices.TryGetValue(OnlineConfig.UrlKey, out var url) && !string.IsNullOrEmpty(url))
            {
                var http = new HttpOnlineService(transportFactory(url), choices.TryGetValue(OnlineConfig.GameKey, out var game) ? game : ThemeId);
                if (c.Link.Token != null) http.Restore(c.Link.AccountId, c.Link.Token);
                c.Online = http;
            }
            else { c.Online = new OfflineOnlineService(); c.OnlineMode = OnlineConfig.Off; }
            c.Sync = new CloudSync(c.Online, clock);
            return c;
        }

        private static Dictionary<string, string> ReadServiceChoices(IDataSource config, string file)
        {
            var choices = new Dictionary<string, string>();
            if (!config.Exists(file)) { Debug.LogWarning("서비스 설정 파일이 없어 모두 끔(off)으로 둡니다: " + file); return choices; }
            foreach (var p in JObject.Parse(config.ReadText(file)).Properties())
                if (!p.Name.StartsWith("_") && p.Value.Type == JTokenType.String) choices[p.Name] = p.Value.Value<string>();
            return choices;
        }

        /// <summary>
        /// 시작할 때: 로그인 → 서버 시각 → 서버 세이브를 받아 쓴다 (꺼져 있거나 닿지 않으면 기기 세이브 그대로).
        /// 지난번에 올리지 못한 기록(행동·오프라인 전투)이 있고 서버가 그대로면 기기 세이브를 두고 기록을 이어서 올린다 (R-42).
        /// 그사이 서버가 바뀌었으면(다른 기기) 서버가 주인: 기록을 버리고 서버 세이브. done은 끝나면 한 번.
        /// </summary>
        public void StartSync(Action done)
        {
            if (!OnlineEnabled) { done?.Invoke(); return; }
            Sync.Start(State, Link.Revision, r =>
            {
                OnlineStatus = r.Outcome;
                if (r.Outcome == SyncOutcomes.Offline) { done?.Invoke(); return; }
                if (r.Outcome == SyncOutcomes.Uploaded) Link.Journal.Clear(); // 올린 기기 세이브에 이미 들어 있다
                else if (Link.Journal.Count > 0 && r.Revision == Link.Revision)
                {
                    KeepAccount();
                    Flush(f =>
                    {
                        OnlineStatus = f == null || f.Outcome == SyncOutcomes.Accepted ? SyncOutcomes.FromServer
                            : f.Outcome == SyncOutcomes.Queued ? SyncOutcomes.Offline : f.Outcome;
                        done?.Invoke();
                    });
                    return;
                }
                else if (Link.Journal.Count > 0) { Link.Journal.Clear(); OnlineStatus = SyncOutcomes.Rejected; }
                Adopt(r);
                done?.Invoke();
            });
        }

        /// <summary>
        /// 전투 밖 행동 하나 (R-41): 규칙대로 적용·저장하고, 온라인이면 기록에 더해 서버로 보낸다 (서버가 같은 규칙으로 다시 적용).
        /// 규칙상 안 되면 false (아무것도 바뀌지 않음).
        /// </summary>
        public bool Do(PlayerAction action)
        {
            if (!Actions.Apply(State, action)) return false;
            Save();
            if (OnlineEnabled)
            {
                action.DoneAtUnixSeconds = Clock.UtcNow.ToUnixTimeSeconds();
                Link.Journal.Add(new JournalEntry { Action = action });
                WriteLink();
                Flush(null);
            }
            return true;
        }

        private bool _flushing;
        private readonly List<Action<SyncResult>> _afterFlush = new List<Action<SyncResult>>();

        /// <summary>
        /// 남은 기록(행동·전투)을 차례로 올린다 (CloudSync.FlushJournal). 다 맞으면 서버 세이브를, 거절되면 서버 세이브로 되돌린다.
        /// 닿지 않으면 기기 세이브와 남은 기록을 그대로 두고 다음에 이어서. 올리는 동안 더한 기록도 이어서 올린다.
        /// done은 끝나면 한 번 (보낼 것이 없으면 null).
        /// </summary>
        public void Flush(Action<SyncResult> done)
        {
            if (done != null) _afterFlush.Add(done);
            if (_flushing) return;
            if (!OnlineEnabled || Link.Journal.Count == 0) { RunAfterFlush(null); return; }
            _flushing = true;
            Sync.FlushJournal(Link, State, r =>
            {
                _flushing = false;
                if (r.Outcome == SyncOutcomes.Queued) WriteLink(); // 올린 만큼 Revision이 올랐다
                else
                {
                    if (r.Outcome == SyncOutcomes.Rejected) OnlineStatus = r.Outcome;
                    Adopt(r);
                }
                RunAfterFlush(r);
            });
        }

        private void RunAfterFlush(SyncResult r)
        {
            var list = new List<Action<SyncResult>>(_afterFlush);
            _afterFlush.Clear();
            foreach (var a in list) a?.Invoke(r);
        }

        /// <summary>
        /// 전투 뒤 (R-40, R-42): 전투를 기록에 더해 올린다. 서버가 같은 입력·같은 시각으로 다시 돌려 맞으면 서버 세이브를 받아 쓰고,
        /// 거절되면 서버 세이브로 되돌린다. 서버에 닿지 않으면 기기 결과 그대로 두고 다음 연결 때 올린다 (Queued).
        /// 전투 전에 한 행동이 남아 있으면 그것부터 올린다 (서버가 같은 편성으로 출진하도록).
        /// </summary>
        public void SubmitBattle(SortiePlan plan, IReadOnlyList<Game.Battle.Model.Commands.BattleCommand> commands, Game.Battle.Model.Simulation.BattleResult result, Action<SyncResult> done)
        {
            if (!OnlineEnabled) { done?.Invoke(null); return; }
            Link.Journal.Add(new JournalEntry { Battle = CloudSync.SubmissionOf(plan, commands, result, Link.Revision) });
            WriteLink();
            Flush(r =>
            {
                if (r != null) OnlineStatus = r.Outcome;
                done?.Invoke(r);
            });
        }

        // ---- 고용·우편 (R-43, hiring.md) ----
        private HireDraft _hires;
        private string _hireStep;

        /// <summary>그 스텝 출진에 고른 고용 (다른 스텝으로 가면 새로).</summary>
        public HireDraft HiresFor(string stepId)
        {
            if (_hires == null || _hireStep != stepId) { _hires = new HireDraft(Defs); _hireStep = stepId; }
            return _hires;
        }

        public void ClearHires() { _hires = null; _hireStep = null; }

        /// <summary>서버 계정 (고용 목록에서 내 등록을 가린다).</summary>
        public string AccountId => Online?.AccountId;

        /// <summary>고용 시장을 쓸 수 있는지 (온라인 fake / http).</summary>
        public bool HiringAvailable => OnlineEnabled;

        /// <summary>등록 목록 + 내 하루 고용 수·대기시간 (닿지 않으면 null).</summary>
        public void LoadListings(string kind, Action<HireListResponse> done) => Online.GetListings(kind, done);

        public void RegisterHire(HireListingRequest request, Action<HireListingResponse> done) => Online.RegisterHire(request, done);

        public void Unregister(string listingId, Action<bool?> done) => Online.Unregister(listingId, done);

        public void LoadMail(Action<List<Mail>> done) => Online.GetMail(r => done?.Invoke(r?.Mail));

        /// <summary>우편 받기 = 행동 mail.claim (서버가 우편이 있는지 확인하고 같은 규칙으로 더한다).</summary>
        public bool ClaimMail(Mail mail)
        {
            if (mail?.Id == null || ClaimedMail.Contains(mail.Id) || !Do(PlayerActionRunner.ClaimMail(mail))) return false;
            ClaimedMail.Add(mail.Id);
            return true;
        }

        /// <summary>이번 실행에서 받은 우편 (서버가 지우기 전에 다시 보여 두 번 받지 않게).</summary>
        public HashSet<string> ClaimedMail { get; } = new HashSet<string>();

        private void KeepAccount()
        {
            if (Online is HttpOnlineService http) { Link.AccountId = http.AccountId; Link.Token = http.Token; }
            WriteLink();
        }

        private void Adopt(SyncResult r)
        {
            State = r.State;
            Link.Revision = r.Revision;
            if (Online is HttpOnlineService http) { Link.AccountId = http.AccountId; Link.Token = http.Token; }
            _formation = null;
            Save();
            WriteLink();
        }

        private OnlineLink ReadLink()
        {
            try { return (_saveStore.Exists(OnlineLink.SaveKey) ? OnlineJson.Read<OnlineLink>(_saveStore.Read(OnlineLink.SaveKey)) ?? new OnlineLink() : new OnlineLink()).Upgrade(); }
            catch (Exception) { return new OnlineLink(); }
        }

        private void WriteLink() => _saveStore.Write(OnlineLink.SaveKey, OnlineJson.Write(Link));

        /// <summary>테마 문자열. 없는 키는 키를 그대로 (빠진 문구가 화면에서 보이도록).</summary>
        public string T(string key) => key != null && Defs.Strings.TryGetValue(key, out var s) ? s : key ?? "";

        public string T(string key, params object[] args) => string.Format(T(key), args);

        public string GeneralName(string generalId) =>
            generalId != null && Defs.Generals.TryGetValue(generalId, out var g) ? T(g.NameKey) : generalId ?? "?";

        public long Resource(string id) => new Wallet(State.Wallet).Get(id);

        public void Save() => Saves.Save(State);

        /// <summary>세이브를 지우고 새 게임 상태로.</summary>
        public void ResetSave()
        {
            State = StartingKitService.CreateNewGame(Defs);
            _formation = null;
            Saves.Save(State);
            // 서버와의 연결도 새로 (다음 시작에 서버 세이브를 받는다)
            Link = new OnlineLink();
            WriteLink();
        }

        /// <summary>출진 시드: 시간 서비스에서 (규칙 계산 밖, 리플레이는 시드가 결과에 남는다).</summary>
        public ulong NewSeed() => (ulong)Time.UtcNow.ToUnixTimeMilliseconds();
    }


    /// <summary>메인 화면 탭 하나 (main-screen.md 3장, `Ui/mainTabs.json`).</summary>
    public sealed class MainTabEntry
    {
        public const string Active = "active";
        public const string Tobe = "tobe";
        public const string Hidden = "hidden";
        public const string File = "Ui/mainTabs.json";

        public string Id { get; private set; }
        public string NameKey { get; private set; }
        public string Status { get; private set; }

        public static List<MainTabEntry> Load(IDataSource data)
        {
            var list = new List<MainTabEntry>();
            foreach (JObject t in (JArray)JObject.Parse(data.ReadText(File))["tabs"])
                list.Add(new MainTabEntry { Id = t.Value<string>("id"), NameKey = t.Value<string>("nameKey"), Status = t.Value<string>("status") ?? Active });
            return list;
        }
    }
}
