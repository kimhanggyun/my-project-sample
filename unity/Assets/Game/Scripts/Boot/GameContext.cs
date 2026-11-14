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
        private ServiceRegistry _services;
        private ServiceConfig _serviceConfig;
        /// <summary>앱 시작 순서 (Ui/startup.json)와 기기에 남긴 시작 기록 (약관 판·로그인 수단).</summary>
        public StartupFlow Startup { get; private set; }
        public StartupRecord StartupRecord { get; private set; } = new StartupRecord();
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
            Func<string, IOnlineTransport> transportFactory = null, Func<ILocationService> deviceLocation = null, MonoBehaviour host = null)
        {
            var clock = new ServerClock(time);
            var c = new GameContext { Data = data, Time = clock, Clock = clock, _saveStore = saveStore };
            c.Defs = DefinitionSet.Load(data, ThemeId);
            c.Flow = new StoryFlow(c.Defs, data, clock);
            c.Saves = new CampaignSaveService(saveStore);
            c.State = c.Saves.Load() ?? StartingKitService.CreateNewGame(c.Defs);
            c.MainTabs = MainTabEntry.Load(data);
            c.Startup = StartupFlow.Load(data);
            c.StartupRecord = StartupFlow.ReadRecord(saveStore);
            c.Actions = new PlayerActionRunner(c.Defs);
            c.ReplayRules = ReplayRules.Load(data);
            c.ReplayService = new ReplayService(c.Flow);
            c.Replays = ReplayLibrary.Read(saveStore);
            c.SuspendedBattle = BattleResume.Read(saveStore);

            var choices = ReadServiceChoices(config, serviceConfigFile);
            // 개발 빌드: 설정 화면에서 고른 서버 연결이 설정 파일보다 앞선다 (폰에서 다시 빌드하지 않고 PC 서버에 붙기)
            c.OnlineOverride = ReadOnlineOverride(saveStore);
            if (Debug.isDebugBuild && c.OnlineOverride != null && !string.IsNullOrEmpty(c.OnlineOverride.Mode))
            {
                choices[OnlineConfig.Key] = c.OnlineOverride.Mode;
                if (!string.IsNullOrEmpty(c.OnlineOverride.Url)) choices[OnlineConfig.UrlKey] = c.OnlineOverride.Url;
            }
            c.OnlineUrl = choices.TryGetValue(OnlineConfig.UrlKey, out var u0) ? u0 : null;
            var cfg = new ServiceConfig(choices);
            var reg = ServiceRegistry.WithBuiltIns();
            // 실제 SDK 구현은 여기서 reg.Register(...)로 더한다 (설정에서 이름으로 고른다).
            if (deviceLocation != null) reg.Register(ServiceIds.Location, Game.Unity.Online.UnityLocationService.ImplName, deviceLocation);
            if (host != null)
                reg.Register<IAuthService>(ServiceIds.AuthFor(ServiceIds.SignInGoogle), Game.Unity.Online.GoogleFirebaseAuthService.ImplName,
                    () => new Game.Unity.Online.GoogleFirebaseAuthService(host, cfg.Value(Game.Unity.Online.GoogleFirebaseAuthService.ApiKeyKey), cfg.Value(Game.Unity.Online.GoogleFirebaseAuthService.WebClientIdKey)));
            c.Auth = reg.Create<IAuthService>(ServiceIds.Auth, cfg);
            c._services = reg;
            c._serviceConfig = cfg;
            c.Store = reg.Create<IStoreService>(ServiceIds.Store, cfg);
            c.RewardedAd = reg.Create<IRewardedAdService>(ServiceIds.RewardedAd, cfg);
            c.BannerAd = reg.Create<IBannerAdService>(ServiceIds.BannerAd, cfg);
            c.Location = reg.Create<ILocationService>(ServiceIds.Location, cfg);
            if (c.Location is FakeLocationService fakeLoc && Game.Foundation.Geo.GeoPoint.TryParse(cfg.Value(ServiceIds.FakeLocationKey), out var fakePoint)) fakeLoc.Point = fakePoint;
            try { c.Territory = data.Exists(Game.Territory.TerritoryDefs.File) ? Game.Territory.TerritoryDefs.Load(data) : null; }
            catch (Exception e) { Debug.LogWarning("영토 데이터를 읽지 못해 영토 탭을 잠급니다: " + e.Message); c.Territory = null; }
            var shown = new Dictionary<string, string>();
            foreach (var id in new[] { ServiceIds.Auth, ServiceIds.AuthFor(ServiceIds.SignInGoogle), ServiceIds.AuthFor(ServiceIds.SignInApple), ServiceIds.Store, ServiceIds.RewardedAd, ServiceIds.BannerAd, ServiceIds.Location }) shown[id] = cfg.ChoiceFor(id);
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

        /// <summary>개발용 서버 연결 선택 (설정 화면, 기기에 저장). 설정 파일의 online·onlineUrl보다 앞선다.</summary>
        public sealed class OnlineChoice
        {
            public string Mode { get; set; }
            public string Url { get; set; }
        }

        public const string OnlineOverrideKey = "dev.online";
        public OnlineChoice OnlineOverride { get; private set; }
        /// <summary>지금 쓰는 서버 주소 (http일 때).</summary>
        public string OnlineUrl { get; private set; }

        private static OnlineChoice ReadOnlineOverride(ISaveStore store)
        {
            try { return store.Exists(OnlineOverrideKey) ? OnlineJson.Read<OnlineChoice>(store.Read(OnlineOverrideKey)) : null; }
            catch (Exception) { return null; }
        }

        /// <summary>서버 연결을 바꿔 저장한다 (mode = off / fake / http, 다시 시작하면 적용). null이면 설정 파일대로.</summary>
        public void SetOnlineOverride(string mode, string url)
        {
            if (mode == null) { if (_saveStore.Exists(OnlineOverrideKey)) _saveStore.Delete(OnlineOverrideKey); OnlineOverride = null; return; }
            OnlineOverride = new OnlineChoice { Mode = mode, Url = url };
            _saveStore.Write(OnlineOverrideKey, OnlineJson.Write(OnlineOverride));
        }

        /// <summary>개발 빌드: 화면 스타일을 골라 저장한다 (다시 시작하면 적용).</summary>
        public void SetStyleOverride(string styleId) => _saveStore.Write(Game.Unity.UI.UiStyle.OverrideKey, styleId);

        // ---- 기기 설정 (E26): 세이브가 아니라 이 기기에만 (서버 검증과 무관한 값) ----
        private const string SoundKey = "prefs.sound", SpeedKey = "prefs.battleSpeed";

        /// <summary>소리 켜짐 (기본 켜짐). 켜고 끄면 전체 음량(AudioListener)에 바로 반영.</summary>
        public bool SoundOn => !_saveStore.Exists(SoundKey) || _saveStore.Read(SoundKey) != "off";

        public void SetSound(bool on)
        {
            _saveStore.Write(SoundKey, on ? "on" : "off");
            UnityEngine.AudioListener.volume = on ? 1f : 0f;
        }

        /// <summary>전투 시작 배속 (1·2·4, 기본 1).</summary>
        public int DefaultBattleSpeed => _saveStore.Exists(SpeedKey) && int.TryParse(_saveStore.Read(SpeedKey), out var v) && (v == 1 || v == 2 || v == 4) ? v : 1;

        public void SetDefaultBattleSpeed(int speed) => _saveStore.Write(SpeedKey, speed.ToString());

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
            if (action.DoneAtUnixSeconds <= 0) action.DoneAtUnixSeconds = Clock.UtcNow.ToUnixTimeSeconds(); // 기간 계산(광고 제거 등)도 이 시각으로
            if (!Actions.Apply(State, action)) return false;
            Save();
            if (OnlineEnabled)
            {
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
            if (_hires == null || _hireStep != stepId)
            {
                _hires = new HireDraft(Defs); _hireStep = stepId;
                // 용병 탭에서 담아 둔 고용을 이 출진으로 (09, hiring-v2.md 3장)
                if (_next != null) { _hires.Picks.AddRange(_next.Picks); _next = null; }
            }
            return _hires;
        }

        private HireDraft _next;
        /// <summary>용병 탭에서 고른, 다음 출진에 넣을 고용 (09).</summary>
        public HireDraft NextHires => _next ?? (_next = new HireDraft(Defs));
        public void ClearNextHires() { _next = null; }

        public void ClearHires() { _hires = null; _hireStep = null; }

        // ---- 닉네임 (공통 계정) ----
        /// <summary>내 닉네임 (아직 모르거나 없으면 null).</summary>
        public string Nickname { get; private set; }
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>();

        /// <summary>서버가 마스터 계정이라고 했다 (R-86). 서버 없이는 false (개발 빌드면 화면은 그래도 연다).</summary>
        public bool IsMaster { get; private set; }

        /// <summary>개발 줄 탭·마스터 화면을 보여 줄지: 개발 빌드이거나 서버 마스터 계정.</summary>
        public bool DevToolsVisible => UnityEngine.Debug.isDebugBuild || IsMaster;

        /// <summary>닉네임 장수가 없는 예전 세이브면 만든다 (R-98, 행동 avatar.create).</summary>
        public void EnsureAvatar()
        {
            if (State != null && State.FindGeneral(AvatarGeneral.GeneralId) == null && Defs.Generals.ContainsKey(AvatarGeneral.GeneralId)) Do(PlayerActionRunner.AvatarCreate());
        }

        public void LoadProfile(Action<ProfileResponse> done) => Online.GetProfile(p =>
        {
            if (p != null) { Nickname = p.Nickname; IsMaster = p.IsMaster; }
            EnsureAvatar();
            done?.Invoke(p);
        });

        public void SetNickname(string nickname, Action<NicknameResponse> done) => Online.SetNickname(nickname, r =>
        {
            if (r != null && r.Ok) { Nickname = r.Nickname; if (AccountId != null) _names[AccountId] = r.Nickname; }
            done?.Invoke(r);
        });

        /// <summary>모르는 계정의 닉네임을 받아 둔다 (화면에 이름으로 보이게). 다 받으면(또는 닿지 않으면) done.</summary>
        public void LoadNames(IEnumerable<string> accountIds, Action done)
        {
            var missing = new List<string>();
            foreach (var id in accountIds) if (id != null && !_names.ContainsKey(id) && !missing.Contains(id)) missing.Add(id);
            if (missing.Count == 0 || !OnlineEnabled) { done?.Invoke(); return; }
            Online.GetNames(missing, r =>
            {
                if (r != null) foreach (var kv in r.Names) _names[kv.Key] = kv.Value;
                done?.Invoke();
            });
        }

        /// <summary>계정 → 보일 이름: 닉네임, 없으면 id 앞부분.</summary>
        public string DisplayName(string accountId)
        {
            if (accountId == null) return "?";
            if (_names.TryGetValue(accountId, out var n)) return n;
            return accountId.Length > 12 ? accountId.Substring(0, 12) + "…" : accountId;
        }

        /// <summary>
        /// 로그인 (startup.md 2장): 지금은 게스트만 (기기 계정). 구글·Apple은 구현이 생기면 같은 자리에서 수단 id로 고른다.
        /// 되면 수단을 기록해 다음 실행에 자동 로그인.
        /// </summary>
        public void SignIn(string method, Action<bool> done)
        {
            var option = Startup.SignInOptions.Find(o => o.Id == method);
            if (option == null || !option.IsActive) { done?.Invoke(false); return; }
            // 방식마다 서비스 auth.<방식> (게스트는 auth). 설정에서 off면 로그인 실패 (막아 둔 방식)
            IAuthService provider;
            try { provider = method == "guest" ? Auth : _services.Create<IAuthService>(ServiceIds.AuthFor(method), _serviceConfig); }
            catch (Exception e) { Debug.LogWarning("로그인 방식 '" + method + "': " + e.Message); done?.Invoke(false); return; }
            provider.SignInAutomatically(ok =>
            {
                if (!ok) { done?.Invoke(false); return; }
                void Finish()
                {
                    Auth = provider;
                    StartupRecord.SignInMethod = method;
                    StartupFlow.WriteRecord(_saveStore, StartupRecord);
                    done?.Invoke(true);
                }
                // 서버(http)가 있고 로그인 증명이 있으면 서버 계정도 그 사용자 것으로 (Firebase, deploy-cloudrun.md 3장)
                if (provider.IdToken == null || !(Online is HttpOnlineService http)) { Finish(); return; }
                string before = http.AccountId;
                http.SignInWithFirebase(provider.IdToken, serverOk =>
                {
                    if (!serverOk) { Debug.LogWarning("서버 로그인(Firebase) 실패"); done?.Invoke(false); return; }
                    if (before != http.AccountId)
                    {
                        // 다른 계정: 처음 맞추는 것으로 (진행이 더 앞선 쪽이 남는다, CloudSync), 이전 계정의 보내지 못한 기록은 버린다
                        Link.Revision = 0;
                        Link.Journal.Clear();
                    }
                    KeepAccount();
                    Finish();
                });
            });
        }

        /// <summary>지금 판 약관에 동의 (기기에 기록).</summary>
        public void AcceptTerms()
        {
            StartupRecord.TermsVersion = Startup.TermsVersion;
            StartupRecord.TermsAcceptedAtUnixSeconds = Clock.UtcNow.ToUnixTimeSeconds();
            StartupFlow.WriteRecord(_saveStore, StartupRecord);
        }

        /// <summary>
        /// 장수 모집 (R-51): 세이브 사본으로 결과를 먼저 구한 뒤 같은 시드의 행동으로 적용한다 (결정적이라 같은 결과, 온라인이면 서버도 같은 규칙).
        /// </summary>
        public GachaResult DrawGacha(string bannerId, int count)
        {
            ulong seed = NewSeed();
            var preview = new GachaService(Defs).Draw(SaveJson.Read(SaveJson.Write(State)), bannerId, count, seed);
            if (!preview.Ok) return preview;
            Do(PlayerActionRunner.DrawGacha(bannerId, count, seed));
            _formation = null;
            return preview;
        }

        /// <summary>무료 모집 1회 (R-100): DrawGacha와 같게 사본으로 먼저 구한 뒤 같은 시드·시각의 행동으로.</summary>
        public GachaResult DrawGachaFree(string bannerId)
        {
            ulong seed = NewSeed();
            long now = Clock.UtcNow.ToUnixTimeSeconds();
            var preview = new GachaService(Defs).DrawFree(SaveJson.Read(SaveJson.Write(State)), bannerId, seed, now);
            if (!preview.Ok) return preview;
            Do(PlayerActionRunner.DrawGachaFree(bannerId, seed, now));
            _formation = null;
            return preview;
        }

        /// <summary>서버 계정 (고용 목록에서 내 등록을 가린다).</summary>
        public string AccountId => Online?.AccountId;

        /// <summary>고용 시장을 쓸 수 있는지 (온라인 fake / http).</summary>
        public bool HiringAvailable => OnlineEnabled;

        /// <summary>등록 목록 + 내 하루 고용 수·대기시간 (닿지 않으면 null).</summary>
        public void LoadListings(string kind, Action<HireListResponse> done) => Online.GetListings(kind, done);

        public void RegisterHire(HireListingRequest request, Action<HireListingResponse> done) => Online.RegisterHire(request, done);

        public void Unregister(string listingId, Action<bool?> done) => Online.Unregister(listingId, done);

        public void LoadMail(Action<List<Mail>> done) => Online.GetMail(r => done?.Invoke(r?.Mail));

        /// <summary>영토 주인 목록 (닿지 않으면 null, R-46).</summary>
        public void LoadTerritory(Action<TerritoryResponse> done) => Online.GetTerritory(done);

        /// <summary>내 지역의 쌓인 수입을 우편으로 (R-47). 받기는 우편함에서.</summary>
        public void CollectTerritory(Action<MailListResponse> done) => Online.CollectTerritory(done);

        // ---- 연합 (R-49) ----
        public void LoadAlliances(Action<AllianceListResponse> done) => Online.GetAlliances(done);
        public void CreateAlliance(string name, Action<AllianceResponse> done) => Online.CreateAlliance(name, done);
        public void JoinAlliance(string id, Action<AllianceResponse> done) => Online.JoinAlliance(id, done);
        public void LeaveAlliance(Action<AllianceResponse> done) => Online.LeaveAlliance(done);
        public void ManageAlliance(string action, string accountId, Action<AllianceResponse> done) =>
            Online.ManageAlliance(new AllianceManageRequest { Action = action, AccountId = accountId }, done);

        /// <summary>내 지역에 지금 기본 편성을 수비 편성으로 등록 (서버 세이브 기준, R-48).</summary>
        public void RegisterDefense(string regionId, Action<DefenseResponse> done) => Online.RegisterDefense(new DefenseRequest { RegionId = regionId }, done);

        /// <summary>깃발 꽂기 (R-96).</summary>
        public void PlantFlag(PlantRequest request, Action<PlantResponse> done) => Online.PlantFlag(request, done);

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

        public string GeneralName(string generalId)
        {
            if (generalId == AvatarGeneral.GeneralId && !string.IsNullOrEmpty(Nickname)) return Nickname; // 닉네임 장수 (R-98)
            if (generalId != null && Defs.Generals.TryGetValue(generalId, out var g)) return T(g.NameKey);
            var opp = generalId == null ? null : Defs.Practice?.Opponent(generalId); // 연습 상대 (대결 연습)
            return opp != null ? T(opp.NameKey) : generalId ?? "?";
        }

        public long Resource(string id) => new Wallet(State.Wallet).Get(id);

        /// <summary>
        /// 화면에 보이는 부대 이름 "제N부대" (기본 부대의 보유 순서 — 승급 부대는 번호를 차지하지 않는다). 내부 id(u01…)는 보이지 않게.
        /// 승급으로 갈라져 나온 부대는 맨 위 기본 부대 이름 + " ›" (예: "제1부대 ›"). 고용 부대는 "고용 부대".
        /// </summary>
        public string UnitLabel(string unitId)
        {
            if (Game.Flow.HireRules.IsHiredUnit(unitId)) return T("ui.unit.hired");
            var unit = State.FindUnit(unitId);
            if (unit == null) return unitId ?? "?";
            if (unit.UnitId == unit.TroopType && Defs.TroopTypes.TryGetValue(unit.TroopType ?? "", out var tt)) return T(tt.NameKey); // 병종 묶음 (army-v2.md)
            if (unit.ParentUnitId != null && State.FindUnit(unit.ParentUnitId) != null)
            {
                var top = unit;
                for (int guard = 0; guard < 8 && top.ParentUnitId != null && State.FindUnit(top.ParentUnitId) != null; guard++) top = State.FindUnit(top.ParentUnitId);
                return T("ui.unit.childLabel", UnitLabel(top.UnitId));
            }
            int n = 0;
            foreach (var u in State.Units)
            {
                if (u.ParentUnitId == null || State.FindUnit(u.ParentUnitId) == null) n++;
                if (u.UnitId == unitId) break;
            }
            return T("ui.unit.label", n);
        }

        public void Save() => Saves.Save(State);

        // ---- 전투 녹화 (replay.md, 기기에만) ----
        public ReplayRules ReplayRules { get; private set; }
        public ReplayService ReplayService { get; private set; }
        public ReplayLibrary Replays { get; private set; } = new ReplayLibrary();

        /// <summary>끝난 전투를 내 녹화에 더한다. 한도를 넘으면 가장 오래된 녹화를 지운다.</summary>
        public ReplayRecord SaveReplay(SortiePlan plan, IReadOnlyList<Game.Battle.Model.Commands.BattleCommand> commands, Game.Battle.Model.Simulation.BattleResult result)
        {
            var r = ReplayService.Capture(plan, commands, result, Clock.UtcNow.ToUnixTimeSeconds());
            Replays.Add(r, ReplayRules.MaxSaved);
            Replays.Write(_saveStore);
            return r;
        }

        // ---- 전투 이어하기 (R-55, 기기에만) ----
        /// <summary>하다 만 전투 (없으면 null).</summary>
        public SuspendedBattle SuspendedBattle { get; private set; }

        /// <summary>지금 전투를 이어하기 기록으로 남긴다 (고용 출진·끝난 전투는 남기지 않는다).</summary>
        public void SuspendBattle(SortiePlan plan, Game.Battle.Model.Simulation.BattleSimulation sim)
        {
            var s = BattleResume.Capture(plan, sim, Clock.UtcNow.ToUnixTimeSeconds());
            if (s == null) return;
            SuspendedBattle = s;
            BattleResume.Write(_saveStore, s);
        }

        public void ClearSuspendedBattle()
        {
            SuspendedBattle = null;
            BattleResume.Clear(_saveStore);
        }

        /// <summary>남긴 틱까지 다시 돌린 전투. 이어 할 수 없으면 기록을 지우고 null (가져간 군량은 이미 썼다).</summary>
        public ResumedBattle ResumeBattle()
        {
            var r = SuspendedBattle == null ? null : BattleResume.Restore(Flow, SuspendedBattle);
            if (r == null) ClearSuspendedBattle();
            return r;
        }

        public void DeleteReplay(string id)
        {
            if (Replays.Remove(id)) Replays.Write(_saveStore);
        }

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
        /// <summary>줄: release(아래, 배포) / dev(그 위, 개발 빌드·마스터만).</summary>
        public string Row { get; private set; }
        public const string RowRelease = "release";
        public const string RowDev = "dev";

        public static List<MainTabEntry> Load(IDataSource data)
        {
            var list = new List<MainTabEntry>();
            foreach (JObject t in (JArray)JObject.Parse(data.ReadText(File))["tabs"])
                list.Add(new MainTabEntry { Id = t.Value<string>("id"), NameKey = t.Value<string>("nameKey"), Status = t.Value<string>("status") ?? Active, Row = t.Value<string>("row") ?? RowRelease });
            return list;
        }
    }
}
