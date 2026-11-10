using System.Collections.Generic;
using Game.Campaign;
using Game.Data.Defs;
using Game.Economy;
using Game.Online;

namespace Game.Flow
{
    /// <summary>
    /// 결제 상품 받기 (R-64): 상점에 있는 상품(가격 있음)이고 영수증이 있으면 자원·광고 제거를 반영한다.
    /// 영수증이 진짜인지는 서버만 안다 (<see cref="ReceiptCheck"/>, 행동 검증의 serverCheck). 기기는 스토어가 성공을 알린 뒤에만 보낸다.
    /// </summary>
    public sealed class PurchaseService
    {
        private const long DaySeconds = 86400;
        private readonly DefinitionSet _defs;
        public PurchaseService(DefinitionSet defs) { _defs = defs; }

        /// <summary>상점에 보이는 상품 (가격이 있는 것), id 순.</summary>
        public List<ProductDef> Listed()
        {
            var list = new List<ProductDef>();
            foreach (var p in _defs.Products.Values) if (p.PriceKrw.HasValue) list.Add(p);
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        public bool Apply(CampaignState state, PlayerAction a)
        {
            if (a?.ProductId == null || string.IsNullOrEmpty(a.Receipt)) return false;
            if (!_defs.Products.TryGetValue(a.ProductId, out var p) || !p.PriceKrw.HasValue) return false;
            var wallet = new Wallet(state.Wallet);
            foreach (var kv in p.Grants) wallet.Add(kv.Key, kv.Value);
            if (p.AdFreeDays.HasValue)
            {
                if (p.AdFreeDays.Value <= 0) state.AdFreeForever = true;
                else
                {
                    long from = System.Math.Max(state.AdFreeUntilUnixSeconds, a.DoneAtUnixSeconds);
                    state.AdFreeUntilUnixSeconds = from + p.AdFreeDays.Value * DaySeconds;
                }
            }
            return true;
        }

        public static bool AdFree(CampaignState state, long nowUnixSeconds) => state.AdFreeForever || state.AdFreeUntilUnixSeconds > nowUnixSeconds;
    }

    /// <summary>
    /// 서버 쪽 영수증 검사 (R-64). 실제 스토어 검증(구글·애플)은 백엔드를 정한 뒤 구현을 더한다.
    /// 지금은 개발용 가짜 영수증("fake:")만 받을 수 있고, 받을지는 서버 설정. 같은 영수증은 한 번만.
    /// </summary>
    public sealed class ReceiptCheck
    {
        public const string FakePrefix = "fake:";
        private readonly bool _allowFake;
        private readonly HashSet<string> _used;

        public ReceiptCheck(bool allowFake, HashSet<string> usedReceipts = null)
        {
            _allowFake = allowFake;
            _used = usedReceipts ?? new HashSet<string>(System.StringComparer.Ordinal);
        }

        /// <summary>결제 행동이 아니면 true. 결제면 영수증이 받을 수 있는 것이고 처음 쓰는 것이어야 true.</summary>
        public bool Check(PlayerAction a)
        {
            if (a?.Kind != ActionKinds.StorePurchase) return true;
            if (string.IsNullOrEmpty(a.Receipt)) return false;
            if (!(_allowFake && a.Receipt.StartsWith(FakePrefix, System.StringComparison.Ordinal))) return false;
            return _used.Add(a.Receipt);
        }
    }
}
