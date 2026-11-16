using System.Collections.Generic;
using Game.Data.Defs;
using Game.Online;
using Game.Territory;

namespace Game.Flow
{
    /// <summary>보낼 푸시 하나 (R-119). 글은 테마 문자열 push.{kind}.title · body (Args로 채움).</summary>
    public sealed class PushMessage
    {
        public string Kind { get; set; }
        public string AccountId { get; set; }
        public List<string> Args { get; set; } = new List<string>();
        /// <summary>눌렀을 때 열 곳 (깃발 키 등).</summary>
        public string RegionId { get; set; }

        public string TitleKey => "push." + Kind + ".title";
        public string BodyKey => "push." + Kind + ".body";
    }

    /// <summary>
    /// 누구에게 무엇을 보낼지 (R-119, 서버 규칙 — 보내기는 서버의 IPushSender):
    /// ① 공성전 준비 시작(서버가 기록하는 때, R-113) → 깃발 주인에게 attacked, 주인의 연합원(주인 · 공격자 빼고)에게 allyHelp.
    /// ② 깃발 수입이 쌓이는 최대 시간(incomeCapHours)에 닿음 → 주인에게 incomeFull 한 번 (깃발 여럿이면 묶어서 한 통). 받은 뒤 다시 차면 또.
    /// 받는 사람의 설정(PushSettings)에서 그 종류를 껐으면 서버가 보내지 않는다.
    /// </summary>
    public static class PushRules
    {
        public static List<PushMessage> ForSiegeStart(TerritoryOwner flag, string attackerId, Alliance defenderAlliance, string defenderName)
        {
            var list = new List<PushMessage>();
            if (flag?.OwnerAccountId == null) return list;
            list.Add(new PushMessage { Kind = PushKinds.Attacked, AccountId = flag.OwnerAccountId, RegionId = flag.RegionId });
            if (defenderAlliance?.Members != null)
                foreach (var m in defenderAlliance.Members)
                    if (m != flag.OwnerAccountId && m != attackerId)
                        list.Add(new PushMessage { Kind = PushKinds.AllyHelp, AccountId = m, RegionId = flag.RegionId, Args = { defenderName ?? "" } });
            return list;
        }

        /// <summary>수입이 다 찬 깃발 (아직 알리지 않은 것)을 주인별로 묶어 알림을 만들고, 그 깃발에 알린 시각을 적는다.</summary>
        public static List<PushMessage> IncomeFullDue(TerritoryDefs territory, Dictionary<string, TerritoryOwner> owners, long nowUnixSeconds)
        {
            var list = new List<PushMessage>();
            var fd = territory?.Flags;
            if (fd == null || owners == null) return list;
            long full = fd.IncomeCapHours * 3600;
            var count = new Dictionary<string, int>();
            var first = new Dictionary<string, string>();
            var keys = new List<string>(owners.Keys);
            keys.Sort(string.CompareOrdinal);
            foreach (var k in keys)
            {
                var o = owners[k];
                if (o?.OwnerAccountId == null || !FlagDefs.IsFlagKey(o.RegionId)) continue;
                if (nowUnixSeconds - o.CollectedUnixSeconds < full || o.IncomeFullNotifiedUnixSeconds > o.CollectedUnixSeconds) continue;
                o.IncomeFullNotifiedUnixSeconds = nowUnixSeconds;
                count.TryGetValue(o.OwnerAccountId, out int n);
                count[o.OwnerAccountId] = n + 1;
                if (!first.ContainsKey(o.OwnerAccountId)) first[o.OwnerAccountId] = o.RegionId;
            }
            var who = new List<string>(count.Keys);
            who.Sort(string.CompareOrdinal);
            foreach (var a in who)
                list.Add(new PushMessage { Kind = PushKinds.IncomeFull, AccountId = a, RegionId = first[a], Args = { count[a].ToString(System.Globalization.CultureInfo.InvariantCulture) } });
            return list;
        }

        /// <summary>테마 문자열로 글 (없으면 키 그대로).</summary>
        public static (string title, string body) Render(DefinitionSet defs, PushMessage m)
        {
            string T(string key)
            {
                string f = defs?.Strings != null && defs.Strings.TryGetValue(key, out var v) ? v : key;
                try { return string.Format(System.Globalization.CultureInfo.InvariantCulture, f, m.Args.ToArray()); }
                catch (System.FormatException) { return f; }
            }
            return (T(m.TitleKey), T(m.BodyKey));
        }
    }
}
