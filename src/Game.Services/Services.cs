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
        void SignInAutomatically(Action<bool> done);
    }

    /// <summary>서버 없는 동안 쓰는 기기 게스트 계정.</summary>
    public sealed class LocalGuestAuthService : IAuthService
    {
        public bool IsSignedIn { get; private set; }
        public string AccountId { get; private set; }
        public bool IsGuest => true;
        public void SignInAutomatically(Action<bool> done)
        {
            IsSignedIn = true;
            AccountId = AccountId ?? "guest-local";
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
