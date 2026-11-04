using System;
using System.Collections.Generic;

namespace Game.Economy
{
    /// <summary>보유 자원 (군량 supply, 돈 gold, 뽑기 재화 gem …). 자원 종류는 데이터로 정한다.</summary>
    public sealed class Wallet
    {
        private readonly Dictionary<string, long> _amounts;

        /// <param name="amounts">세이브의 지갑 사전을 그대로 넘긴다 (상태의 주인은 Campaign).</param>
        public Wallet(Dictionary<string, long> amounts) { _amounts = amounts; }

        public long Get(string resourceId) => _amounts.TryGetValue(resourceId, out var v) ? v : 0;

        public void Add(string resourceId, long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            _amounts[resourceId] = checked(Get(resourceId) + amount);
        }

        public bool TrySpend(string resourceId, long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            long have = Get(resourceId);
            if (have < amount) return false;
            _amounts[resourceId] = have - amount;
            return true;
        }
    }

    public static class ResourceIds
    {
        public const string Supply = "supply";
        public const string Gold = "gold";
        public const string Gem = "gem";
    }
}
