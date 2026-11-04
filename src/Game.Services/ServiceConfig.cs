using System;
using System.Collections.Generic;

namespace Game.Services
{
    /// <summary>
    /// 외부 서비스 설정 (개발 / 테스트 / 출시 빌드별 파일). 서비스 id → 구현 이름("off", "fake", 실제 구현 이름).
    /// 게임 규칙을 켜고 끄는 규칙 스위치(ruleFlags)와는 다른 파일이다 (dev-rules 4장).
    /// </summary>
    public sealed class ServiceConfig
    {
        private readonly Dictionary<string, string> _choices;
        public ServiceConfig(Dictionary<string, string> choices) { _choices = choices; }
        public string ChoiceFor(string serviceId) =>
            _choices.TryGetValue(serviceId, out var v) ? v : ServiceImpl.Off;
    }

    public static class ServiceImpl
    {
        public const string Off = "off";
        public const string Fake = "fake";
    }

    /// <summary>
    /// 서비스 구현을 이름으로 등록하고, 설정에 따라 골라 만든다.
    /// 실제 구현(SDK 사용)은 엔진·서버 쪽 어셈블리에서 등록한다. 여기에는 끔·가짜 구현만 있다.
    /// </summary>
    public sealed class ServiceRegistry
    {
        private readonly Dictionary<string, Dictionary<string, Func<object>>> _factories =
            new Dictionary<string, Dictionary<string, Func<object>>>();

        public ServiceRegistry Register<T>(string serviceId, string implName, Func<T> factory) where T : class
        {
            if (!_factories.TryGetValue(serviceId, out var byName))
                _factories[serviceId] = byName = new Dictionary<string, Func<object>>();
            byName[implName] = () => factory();
            return this;
        }

        public T Create<T>(string serviceId, ServiceConfig config) where T : class
        {
            string choice = config.ChoiceFor(serviceId);
            if (!_factories.TryGetValue(serviceId, out var byName) || !byName.TryGetValue(choice, out var f))
                throw new InvalidOperationException($"서비스 '{serviceId}'에 구현 '{choice}'가 등록되지 않았습니다.");
            return (T)f();
        }

        /// <summary>끔·가짜 구현을 모두 등록한 기본 등록표.</summary>
        public static ServiceRegistry WithBuiltIns()
        {
            var r = new ServiceRegistry();
            r.Register<IAuthService>(ServiceIds.Auth, ServiceImpl.Off, () => new LocalGuestAuthService());
            r.Register<IAuthService>(ServiceIds.Auth, ServiceImpl.Fake, () => new LocalGuestAuthService());
            r.Register<IStoreService>(ServiceIds.Store, ServiceImpl.Off, () => new OffStoreService());
            r.Register<IStoreService>(ServiceIds.Store, ServiceImpl.Fake, () => new FakeStoreService());
            r.Register<IRewardedAdService>(ServiceIds.RewardedAd, ServiceImpl.Off, () => new OffRewardedAdService());
            r.Register<IRewardedAdService>(ServiceIds.RewardedAd, ServiceImpl.Fake, () => new FakeRewardedAdService());
            r.Register<IBannerAdService>(ServiceIds.BannerAd, ServiceImpl.Off, () => new OffBannerAdService());
            r.Register<IBannerAdService>(ServiceIds.BannerAd, ServiceImpl.Fake, () => new OffBannerAdService());
            return r;
        }
    }

    public static class ServiceIds
    {
        public const string Auth = "auth";
        public const string Store = "store";
        public const string RewardedAd = "rewardedAd";
        public const string BannerAd = "bannerAd";
    }
}
