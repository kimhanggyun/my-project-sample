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
        /// <summary>편성 편집 중인 사본 (화면을 오가도 유지). 저장·되돌리기 전까지 출진에는 저장된 편성이 쓰인다.</summary>
        public Game.Campaign.Roster.FormationEditor Formation => _formation ?? (_formation = new Game.Campaign.Roster.FormationEditor(Defs, State));
        public void DiscardFormationDraft() => _formation = null;

        public IAuthService Auth { get; private set; }
        public IStoreService Store { get; private set; }
        public IRewardedAdService RewardedAd { get; private set; }
        public IBannerAdService BannerAd { get; private set; }
        /// <summary>서비스 id → 고른 구현 이름 (설정 화면 표시용).</summary>
        public IReadOnlyDictionary<string, string> ServiceChoices { get; private set; }

        /// <summary>데이터·세이브·서비스를 준비한다. 실패하면 예외 (부팅 화면이 오류로 보여준다).</summary>
        public static GameContext Create(IDataSource data, IDataSource config, ISaveStore saveStore, ITimeProvider time, string serviceConfigFile)
        {
            var c = new GameContext { Data = data, Time = time };
            c.Defs = DefinitionSet.Load(data, ThemeId);
            c.Flow = new StoryFlow(c.Defs, data, time);
            c.Saves = new CampaignSaveService(saveStore);
            c.State = c.Saves.Load() ?? StartingKitService.CreateNewGame(c.Defs);
            c.MainTabs = MainTabEntry.Load(data);

            var choices = ReadServiceChoices(config, serviceConfigFile);
            var cfg = new ServiceConfig(choices);
            var reg = ServiceRegistry.WithBuiltIns();
            // 실제 SDK 구현은 여기서 reg.Register(...)로 더한다 (설정에서 이름으로 고른다).
            c.Auth = reg.Create<IAuthService>(ServiceIds.Auth, cfg);
            c.Store = reg.Create<IStoreService>(ServiceIds.Store, cfg);
            c.RewardedAd = reg.Create<IRewardedAdService>(ServiceIds.RewardedAd, cfg);
            c.BannerAd = reg.Create<IBannerAdService>(ServiceIds.BannerAd, cfg);
            var shown = new Dictionary<string, string>();
            foreach (var id in new[] { ServiceIds.Auth, ServiceIds.Store, ServiceIds.RewardedAd, ServiceIds.BannerAd }) shown[id] = cfg.ChoiceFor(id);
            c.ServiceChoices = shown;
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
