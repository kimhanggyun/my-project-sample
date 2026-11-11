using System;
using System.Collections.Generic;

namespace Game.Services
{
    // 외부 서비스 경계. 게임 코드는 이 인터페이스만 안다. 서비스가 꺼져도 게임은 멈추지 않아야 한다.

    public interface IAuthService
    {
        bool IsSignedIn { get; }
        string AccountId { get; }
        bool IsGuest { get; }
        /// <summary>서버에 보여 줄 로그인 증명 (Firebase ID 토큰, docs/impl/deploy-cloudrun.md 3장). 게스트·가짜·끔은 null (서버는 게스트로).</summary>
        string IdToken { get; }
        void SignInAutomatically(Action<bool> done);
    }

    /// <summary>서버 없는 동안 쓰는 기기 게스트 계정.</summary>
    public sealed class LocalGuestAuthService : IAuthService
    {
        public bool IsSignedIn { get; private set; }
        public string AccountId { get; private set; }
        public bool IsGuest => true;
        public string IdToken => null;
        public void SignInAutomatically(Action<bool> done)
        {
            IsSignedIn = true;
            AccountId = AccountId ?? "guest-local";
            done?.Invoke(true);
        }
    }

    /// <summary>로그인 방식이 꺼져 있을 때 (실제 SDK 연결 전·막아 둔 방식): 로그인은 늘 실패.</summary>
    public sealed class OffAuthService : IAuthService
    {
        public bool IsSignedIn => false;
        public string AccountId => null;
        public bool IsGuest => false;
        public string IdToken => null;
        public void SignInAutomatically(Action<bool> done) => done?.Invoke(false);
    }

    /// <summary>개발용 가짜 외부 로그인 (구글·애플 SDK 연결 전 흐름 확인): 늘 성공, 계정 = "방식-fake-local".</summary>
    public sealed class FakeProviderAuthService : IAuthService
    {
        private readonly string _provider;
        public FakeProviderAuthService(string provider) { _provider = provider; }
        public bool IsSignedIn { get; private set; }
        public string AccountId { get; private set; }
        public bool IsGuest => false;
        public string IdToken => null;
        public void SignInAutomatically(Action<bool> done)
        {
            IsSignedIn = true;
            AccountId = _provider + "-fake-local";
            done?.Invoke(true);
        }
    }

    public enum PurchaseOutcome { Succeeded, Cancelled, Unavailable, Failed }

    public interface IStoreService
    {
        bool IsAvailable { get; }
        void Purchase(string productId, Action<PurchaseOutcome> done);
        void RestorePurchases(Action<IReadOnlyList<string>> done);
    }

    public sealed class OffStoreService : IStoreService
    {
        public bool IsAvailable => false;
        public void Purchase(string productId, Action<PurchaseOutcome> done) => done?.Invoke(PurchaseOutcome.Unavailable);
        public void RestorePurchases(Action<IReadOnlyList<string>> done) => done?.Invoke(Array.Empty<string>());
    }

    /// <summary>개발·테스트용: 모든 구매가 바로 성공한다.</summary>
    public sealed class FakeStoreService : IStoreService
    {
        private readonly List<string> _owned = new List<string>();
        public bool IsAvailable => true;
        public void Purchase(string productId, Action<PurchaseOutcome> done) { _owned.Add(productId); done?.Invoke(PurchaseOutcome.Succeeded); }
        public void RestorePurchases(Action<IReadOnlyList<string>> done) => done?.Invoke(_owned.ToArray());
    }

    public enum AdOutcome { Completed, Skipped, Unavailable }

    public interface IRewardedAdService
    {
        bool IsReady { get; }
        /// <summary>끝까지 본 경우에만 Completed.</summary>
        void Show(string placementId, Action<AdOutcome> done);
    }

    public sealed class OffRewardedAdService : IRewardedAdService
    {
        public bool IsReady => false;
        public void Show(string placementId, Action<AdOutcome> done) => done?.Invoke(AdOutcome.Unavailable);
    }

    public sealed class FakeRewardedAdService : IRewardedAdService
    {
        public bool IsReady => true;
        public void Show(string placementId, Action<AdOutcome> done) => done?.Invoke(AdOutcome.Completed);
    }

    public interface IBannerAdService
    {
        bool IsShowing { get; }
        /// <summary>화면 배치용 배너 높이 (0이면 자리를 접는다).</summary>
        float HeightPixels { get; }
        void Show();
        void Hide();
    }

    public sealed class OffBannerAdService : IBannerAdService
    {
        public bool IsShowing => false;
        public float HeightPixels => 0f;
        public void Show() { }
        public void Hide() { }
    }
}

namespace Game.Services
{
    /// <summary>위치 요청 결과. 실패하면 Ok = false, Reason = <see cref="LocationReasons"/>.</summary>
    public sealed class LocationResult
    {
        public bool Ok { get; set; }
        public Game.Foundation.Geo.GeoPoint Point { get; set; }
        public string Reason { get; set; }
    }

    public static class LocationReasons
    {
        public const string Off = "off";
        public const string Denied = "denied";
        public const string Unavailable = "unavailable";
    }

    /// <summary>
    /// 위치 서비스 (territory.md 3장): 영토 탭을 열 때와 공격을 신청할 때만 부른다 (계속 추적하지 않음).
    /// 구현: 끔 / 가짜(개발용 고정 좌표) / 실제(기기 위치, 엔진 쪽). 꺼도 게임은 동작한다 (영토 탭만 잠김).
    /// </summary>
    public interface ILocationService
    {
        bool IsAvailable { get; }
        void Request(System.Action<LocationResult> done);
    }

    public sealed class OffLocationService : ILocationService
    {
        public bool IsAvailable => false;
        public void Request(System.Action<LocationResult> done) => done?.Invoke(new LocationResult { Ok = false, Reason = LocationReasons.Off });
    }

    /// <summary>개발용 고정 좌표 (기본: 개발 지역 데이터의 가운데). 설정 fakeLocation으로 바꾼다.</summary>
    public sealed class FakeLocationService : ILocationService
    {
        public static readonly Game.Foundation.Geo.GeoPoint Default = new Game.Foundation.Geo.GeoPoint(37_566_500, 126_978_000);
        public Game.Foundation.Geo.GeoPoint Point { get; set; } = Default;
        public bool IsAvailable => true;
        public void Request(System.Action<LocationResult> done) => done?.Invoke(new LocationResult { Ok = true, Point = Point });
    }
}
