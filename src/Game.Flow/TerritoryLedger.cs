using System;
using System.Collections.Generic;
using Game.Online;
using Game.Territory;

namespace Game.Flow
{
    /// <summary>
    /// 영토 주인·수입 장부 (verification R-46, R-47): 저장소와 무관한 계산. 참고 서버·가짜 서버가 같이 쓴다.
    /// 수입은 세이브를 직접 고치지 않고 우편으로 보낸다 (받기 = 행동 mail.claim).
    /// </summary>
    public static class TerritoryLedger
    {
        /// <summary>
        /// 차지 (R-46): 주인이 바뀌면 이전 주인에게 그때까지 쌓인 수입을 우편으로 보내고, 새 주인은 지금부터 쌓인다.
        /// </summary>
        public static void Capture(TerritoryDefs defs, Dictionary<string, TerritoryOwner> owners, string regionId, string accountId, long at, Action<string, Mail> sendMail)
        {
            if (owners.TryGetValue(regionId, out var old) && old.OwnerAccountId == accountId) return; // 이미 내 지역: 그대로 (수입도 이어서)
            if (old != null) foreach (var m in MailsFor(defs, regionId, old, at)) sendMail(old.OwnerAccountId, m);
            owners[regionId] = new TerritoryOwner { RegionId = regionId, OwnerAccountId = accountId, SinceUnixSeconds = at, CollectedUnixSeconds = at };
        }

        /// <summary>수비 편성 등록 (R-48): 그 지역의 주인만. 판 = 앞 판 + 1. 되면 등록한 것, 아니면 null.</summary>
        public static TerritoryDefense RegisterDefense(Dictionary<string, TerritoryOwner> owners, string regionId, string accountId, TerritoryDefense defense, long at)
        {
            if (defense == null || !owners.TryGetValue(regionId ?? "", out var o) || o.OwnerAccountId != accountId) return null;
            defense.Version = (o.Defense?.Version ?? 0) + 1;
            defense.RegisteredAtUnixSeconds = at;
            o.Defense = defense;
            return defense;
        }

        /// <summary>그 지역의 지금 수비 편성 (없으면 null = AI 수비군).</summary>
        public static TerritoryDefense DefenseOf(Dictionary<string, TerritoryOwner> owners, string regionId) =>
            regionId != null && owners.TryGetValue(regionId, out var o) ? o.Defense : null;

        /// <summary>수입 받기 (R-47): 내 지역마다 쌓인 수입을 우편으로, 받은 시각 = 지금. 보낸 우편 목록.</summary>
        public static List<Mail> Collect(TerritoryDefs defs, Dictionary<string, TerritoryOwner> owners, string accountId, long now, Action<string, Mail> sendMail)
        {
            var sent = new List<Mail>();
            if (defs == null) return sent;
            foreach (var o in owners.Values)
            {
                if (o.OwnerAccountId != accountId || AccruedOf(defs, o.RegionId, o.CollectedUnixSeconds, now) == null) continue;
                foreach (var m in MailsFor(defs, o.RegionId, o, now)) { sendMail(accountId, m); sent.Add(m); }
                o.CollectedUnixSeconds = now;
            }
            return sent;
        }

        /// <summary>목록 보기: 주인 사본, 요청한 계정의 지역에만 지금 받을 수 있는 수입.</summary>
        /// <param name="allianceOf">계정 → 연합 (없으면 null). 주인의 연합 이름을 붙인다 (R-49).</param>
        public static TerritoryResponse View(TerritoryDefs defs, Dictionary<string, TerritoryOwner> owners, string accountId, long now, Func<string, Alliance> allianceOf = null)
        {
            var r = new TerritoryResponse();
            foreach (var o in owners.Values)
            {
                var copy = new TerritoryOwner { RegionId = o.RegionId, OwnerAccountId = o.OwnerAccountId, SinceUnixSeconds = o.SinceUnixSeconds, CollectedUnixSeconds = o.CollectedUnixSeconds, Defense = o.Defense,
                    GeneralId = o.GeneralId, RadiusM = o.RadiusM, ProtectedUntilUnixSeconds = o.ProtectedUntilUnixSeconds };
                var al = allianceOf?.Invoke(o.OwnerAccountId);
                copy.OwnerAllianceId = al?.Id;
                copy.OwnerAllianceName = al?.Name;
                if (o.OwnerAccountId == accountId) copy.Pending = AccruedOf(defs, o.RegionId, o.CollectedUnixSeconds, now);
                r.Owners.Add(copy);
            }
            r.Owners.Sort((a, b) => string.CompareOrdinal(a.RegionId, b.RegionId));
            return r;
        }

        /// <summary>지역 또는 깃발("flag:" 키, R-97)의 쌓인 수입. 모르는 키면 null.</summary>
        public static Dictionary<string, long> AccruedOf(TerritoryDefs defs, string key, long since, long now)
        {
            if (defs == null || key == null) return null;
            if (FlagDefs.IsFlagKey(key)) return defs.Flags?.Accrued(since, now);
            return defs.Regions.TryGetValue(key, out var r) ? defs.Accrued(r, since, now) : null;
        }

        private static IEnumerable<Mail> MailsFor(TerritoryDefs defs, string key, TerritoryOwner o, long now)
        {
            foreach (var kv in AccruedOf(defs, key, o.CollectedUnixSeconds, now) ?? new Dictionary<string, long>())
                yield return new Mail { Kind = MailKinds.TerritoryIncome, Resource = kv.Key, Amount = kv.Value, RegionId = key, CreatedAtUnixSeconds = now };
        }
    }
}
