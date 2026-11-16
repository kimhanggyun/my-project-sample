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
            // 치장 상품 (R-111): 얻고, 이미 가졌으면 엽전 (보상 중복과 같다)
            if (p.CosmeticId != null && new CosmeticService(_defs).GrantOrRefund(state, p.CosmeticId) < 0) return false;
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

    /// <summary>영수증 검증 결과 (R-114).</summary>
    public sealed class ReceiptVerdict
    {
        public bool Valid { get; set; }
        /// <summary><see cref="ReceiptReasons"/>.</summary>
        public string Reason { get; set; }
        /// <summary>스토어 거래 id (한 번만 쓰게 하는 키). 없으면 영수증 문자열 그대로.</summary>
        public string TransactionId { get; set; }

        public static ReceiptVerdict Ok(string transactionId) => new ReceiptVerdict { Valid = true, Reason = ReceiptReasons.Ok, TransactionId = transactionId };
        public static ReceiptVerdict No(string reason) => new ReceiptVerdict { Valid = false, Reason = reason };
    }

    public static class ReceiptReasons
    {
        public const string Ok = "ok";
        public const string Empty = "empty";
        /// <summary>이 검증기가 받지 않는 형식 (가짜 영수증을 끈 서버 등).</summary>
        public const string NotAccepted = "notAccepted";
        /// <summary>스토어가 아니라고 함 (취소 · 환불 · 다른 상품).</summary>
        public const string Rejected = "rejected";
        /// <summary>검증기 설정이 없거나 아직 만들지 않음 (구글 플레이 자리).</summary>
        public const string NotConfigured = "notConfigured";
        /// <summary>이미 쓴 영수증.</summary>
        public const string Used = "used";
        /// <summary>스토어에 닿지 않음 (네트워크 · 인증 · 스토어 오류). 다시 시도하면 될 수 있다.</summary>
        public const string Unavailable = "unavailable";
        /// <summary>결제 대기 중 (구글 purchaseState 2: 현금 결제 등).</summary>
        public const string Pending = "pending";
    }

    /// <summary>
    /// 결제 영수증 검증 (서버만, R-114, economy.md 4장). 설정 <c>Game:ReceiptVerifier</c>로 고른다: fake(기본, 개발용 "fake:" 영수증) · off(모두 거절) · googlePlay(자리, deploy-cloudrun.md).
    /// 규칙 코드는 이 인터페이스만 안다 — 스토어 SDK · HTTP는 서버 쪽 구현에.
    /// </summary>
    public interface IReceiptVerifier
    {
        /// <summary>이 상품의 영수증이 진짜인지. 한 번만 쓰기는 <see cref="ReceiptCheck"/>가 TransactionId로 본다.</summary>
        ReceiptVerdict Verify(string productId, string receipt);
    }

    public static class ReceiptVerifierIds
    {
        public const string Fake = "fake";
        public const string Off = "off";
        public const string GooglePlay = "googlePlay";
    }

    /// <summary>개발 · 테스트용: "fake:"로 시작하는 영수증만 받는다 (참고 서버 기본).</summary>
    public sealed class FakeReceiptVerifier : IReceiptVerifier
    {
        public ReceiptVerdict Verify(string productId, string receipt) =>
            receipt != null && receipt.StartsWith(ReceiptCheck.FakePrefix, System.StringComparison.Ordinal) ? ReceiptVerdict.Ok(receipt) : ReceiptVerdict.No(ReceiptReasons.NotAccepted);
    }

    /// <summary>모든 영수증 거절 (실제 검증 전의 배포 서버).</summary>
    public sealed class RejectAllReceiptVerifier : IReceiptVerifier
    {
        public ReceiptVerdict Verify(string productId, string receipt) => ReceiptVerdict.No(ReceiptReasons.NotAccepted);
    }

    /// <summary>
    /// 서버 쪽 영수증 검사 (R-64). 실제 스토어 검증(구글·애플)은 백엔드를 정한 뒤 구현을 더한다.
    /// 지금은 개발용 가짜 영수증("fake:")만 받을 수 있고, 받을지는 서버 설정. 같은 영수증은 한 번만.
    /// </summary>
    public sealed class ReceiptCheck
    {
        public const string FakePrefix = "fake:";
        private readonly IReceiptVerifier _verifier;
        private readonly HashSet<string> _used;
        private readonly System.Func<string, bool> _isUsed;
        private readonly HashSet<string> _batch = new HashSet<string>(System.StringComparer.Ordinal);

        /// <summary>예전 설정 (AllowFakeReceipts): true = 가짜 검증기, false = 모두 거절.</summary>
        public ReceiptCheck(bool allowFake, HashSet<string> usedReceipts = null)
            : this(allowFake ? (IReceiptVerifier)new FakeReceiptVerifier() : new RejectAllReceiptVerifier(), usedReceipts) { }

        /// <summary>쓴 영수증을 메모리 묶음에 바로 넣는다 (기기 안 가짜 서버 · 테스트).</summary>
        public ReceiptCheck(IReceiptVerifier verifier, HashSet<string> usedReceipts = null)
        {
            _verifier = verifier ?? new RejectAllReceiptVerifier();
            _used = usedReceipts ?? new HashSet<string>(System.StringComparer.Ordinal);
        }

        /// <summary>
        /// 서버 (R-116): 쓴 거래인지는 저장소에 묻고(isUsed), 이 묶음에서 통과한 것은 <see cref="Pending"/>에 모은다.
        /// 서버는 세이브를 저장한 뒤에만 Pending을 저장소에 기록한다 (행동이 거절되면 영수증이 타지 않는다).
        /// </summary>
        public ReceiptCheck(IReceiptVerifier verifier, System.Func<string, bool> isUsed)
        {
            _verifier = verifier ?? new RejectAllReceiptVerifier();
            _isUsed = isUsed ?? (_ => false);
        }

        /// <summary>마지막 결제 검사의 결과 (서버 기록 · 문제 찾기용).</summary>
        public ReceiptVerdict Last { get; private set; }

        /// <summary>이 묶음에서 통과한 결제 (거래 id · 상품 · 영수증).</summary>
        public List<ReceiptRecord> Pending { get; } = new List<ReceiptRecord>();

        /// <summary>결제 행동이 아니면 true. 결제면 영수증이 받을 수 있는 것이고 처음 쓰는 것이어야 true.</summary>
        public bool Check(PlayerAction a)
        {
            if (a?.Kind != ActionKinds.StorePurchase) return true;
            if (string.IsNullOrEmpty(a.Receipt)) { Last = ReceiptVerdict.No(ReceiptReasons.Empty); return false; }
            var v = _verifier.Verify(a.ProductId, a.Receipt) ?? ReceiptVerdict.No(ReceiptReasons.Rejected);
            Last = v;
            if (!v.Valid) return false;
            string tx = v.TransactionId ?? a.Receipt;
            bool fresh = _isUsed != null ? !_isUsed(tx) && _batch.Add(tx) : _used.Add(tx);
            if (!fresh) { Last = ReceiptVerdict.No(ReceiptReasons.Used); return false; }
            Pending.Add(new ReceiptRecord { TransactionId = tx, ProductId = a.ProductId, Receipt = a.Receipt });
            return true;
        }
    }

    /// <summary>반영한 결제 하나 (서버 저장소, R-116). 같은 거래를 다시 쓰지 못하게 하고, 복원 때 "이미 반영"을 안다.</summary>
    public sealed class ReceiptRecord
    {
        public string TransactionId { get; set; }
        public string ProductId { get; set; }
        public string AccountId { get; set; }
        /// <summary>스토어에 다시 묻거나 확인 처리(acknowledge · consume)할 때 쓰는 원래 영수증.</summary>
        public string Receipt { get; set; }
        public long AtUnixSeconds { get; set; }
    }

    /// <summary>
    /// 반영한 뒤 스토어에 알리기 (R-116): 구글은 3일 안에 확인(acknowledge)하지 않으면 환불한다. 소모성 상품은 consume해야 다시 살 수 있다.
    /// 검증기가 이것도 구현하면 서버가 세이브 저장 뒤 부른다. 실패해도 지급은 그대로 (기록 · 다음 기회에 다시).
    /// </summary>
    public interface IReceiptSettler
    {
        bool Settle(string productId, string receipt, bool consumable);
    }
}
