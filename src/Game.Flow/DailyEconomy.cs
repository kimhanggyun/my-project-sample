using System.Collections.Generic;
using Game.Campaign;
using Game.Data.Defs;
using Game.Economy;
using Game.Online;
using Newtonsoft.Json.Linq;

namespace Game.Flow
{
    /// <summary>출석 보상 하루치 (economy.json attendance.cycle).</summary>
    public sealed class AttendanceReward
    {
        public string Resource { get; internal set; }
        public long Amount { get; internal set; }
    }

    /// <summary>교환 비율 하나 (economy.json exchange.rates).</summary>
    public sealed class ExchangeRate
    {
        public string From { get; internal set; }
        public string To { get; internal set; }
        public long Give { get; internal set; }
        public long Get { get; internal set; }
        public long LossPct { get; internal set; }
        /// <summary>하루에 낼 수 있는 양 (낸 자원 단위). 0 = 한도 없음.</summary>
        public long DailyLimit { get; internal set; }
        public string Key => From + ">" + To;
    }

    /// <summary>계정 하나의 출석 기록 (서버 저장소, 세이브 밖: 기기가 고칠 수 없다).</summary>
    public sealed class AttendanceRecord
    {
        /// <summary>마지막으로 받은 날 (<see cref="DailyEconomy.DayOf"/>, 0 = 아직 없음).</summary>
        public long LastDay { get; set; }
        /// <summary>다음에 받을 차례 (cycle 안 번호).</summary>
        public int Next { get; set; }
    }

    /// <summary>
    /// 출석 · 교환 · 금괴 스킬 해금 (economy.md 1장 · 1.0장 · 6장, verification R-112). 수치는 data/Rules/economy.json (임시값).
    /// 하루 = 서버 시각을 dayStartUtcOffsetHours만큼 옮긴 날짜. 출석은 서버가 기록 · 우편으로 보낸다. 교환 · 해금은 행동(PlayerActionRunner)으로 기기 · 서버가 같이 적용한다.
    /// </summary>
    public sealed class DailyEconomy
    {
        private const long SecondsPerDay = 86400;

        public long OffsetSeconds { get; }
        public bool AttendanceOn => Cycle.Count > 0;
        public bool MissedResets { get; }
        public IReadOnlyList<AttendanceReward> Cycle { get; }
        public IReadOnlyList<ExchangeRate> Rates { get; }
        public IReadOnlyDictionary<string, long> UnlockGemByTier { get; }

        private readonly DefinitionSet _defs;

        public DailyEconomy(DefinitionSet defs)
        {
            _defs = defs;
            var o = defs?.EconomyDoc;
            OffsetSeconds = (o?["dayStartUtcOffsetHours"]?.Type == JTokenType.Integer ? (long)o["dayStartUtcOffsetHours"] : 0) * 3600;
            var att = o?["attendance"] as JObject;
            MissedResets = att?["missedResets"]?.Type == JTokenType.Boolean && (bool)att["missedResets"];
            var cycle = new List<AttendanceReward>();
            if (att?["cycle"] is JArray ca)
                foreach (var t in ca)
                    if (t is JObject d) cycle.Add(new AttendanceReward { Resource = (string)d["resource"], Amount = d["amount"]?.Type == JTokenType.Integer ? (long)d["amount"] : 0 });
            Cycle = cycle;
            var rates = new List<ExchangeRate>();
            if (o?["exchange"]?["rates"] is JArray ra)
                foreach (var t in ra)
                    if (t is JObject d)
                        rates.Add(new ExchangeRate
                        {
                            From = (string)d["from"], To = (string)d["to"],
                            Give = Long(d, "give"), Get = Long(d, "get"), LossPct = Long(d, "lossPct"), DailyLimit = Long(d, "dailyLimit"),
                        });
            Rates = rates;
            var tiers = new Dictionary<string, long>();
            if (o?["skillLearnUnlock"]?["gemByTier"] is JObject g)
                foreach (var p in g.Properties()) if (p.Value.Type == JTokenType.Integer) tiers[p.Name] = (long)p.Value;
            UnlockGemByTier = tiers;
        }

        private static long Long(JObject d, string k) => d[k]?.Type == JTokenType.Integer ? (long)d[k] : 0;

        /// <summary>그 시각의 날 (하루 시작 시각을 옮긴 unix 일).</summary>
        public long DayOf(long unixSeconds) => (unixSeconds + OffsetSeconds) / SecondsPerDay;

        /// <summary>다음 하루가 시작하는 시각.</summary>
        public long NextDayAt(long unixSeconds) => (DayOf(unixSeconds) + 1) * SecondsPerDay - OffsetSeconds;

        // ---------------- 출석 (서버) ----------------

        /// <summary>
        /// 출석: 오늘 아직 안 받았으면 기록을 고치고 보상 우편을 만들어 send로 보낸다. 날짜는 서버 시각 (기기 시각은 보지 않는다).
        /// 하루라도 빠지면 missedResets일 때 처음 차례부터. cycle 끝 다음은 처음으로.
        /// </summary>
        public AttendanceResponse CheckIn(AttendanceRecord rec, long nowUnixSeconds, System.Action<Mail> send)
        {
            var r = new AttendanceResponse { NextDayAtUnixSeconds = NextDayAt(nowUnixSeconds) };
            if (!AttendanceOn || rec == null) { r.Result = AttendanceResults.Off; return r; }
            long today = DayOf(nowUnixSeconds);
            if (rec.LastDay == today) { r.Result = AttendanceResults.Already; r.Index = (rec.Next + Cycle.Count - 1) % Cycle.Count; return r; }
            int index = rec.Next;
            if (index < 0 || index >= Cycle.Count) index = 0;
            if (MissedResets && rec.LastDay != 0 && today > rec.LastDay + 1) index = 0;
            var reward = Cycle[index];
            rec.LastDay = today;
            rec.Next = (index + 1) % Cycle.Count;
            var mail = new Mail { Kind = MailKinds.Attendance, Resource = reward.Resource, Amount = reward.Amount, CreatedAtUnixSeconds = nowUnixSeconds };
            send?.Invoke(mail);
            r.Result = AttendanceResults.Ok;
            r.Index = index;
            r.Mail = mail;
            return r;
        }

        // ---------------- 교환 (행동) ----------------

        public ExchangeRate Rate(string from, string to)
        {
            foreach (var x in Rates) if (x.From == from && x.To == to) return x;
            return null;
        }

        /// <summary>낸 양으로 받는 양 (내림 두 번). 비율이 없으면 0.</summary>
        public static long Output(ExchangeRate rate, long amount)
        {
            if (rate == null || amount <= 0 || rate.Give <= 0 || rate.Get <= 0) return 0;
            long raw = amount / rate.Give * rate.Get + amount % rate.Give * rate.Get / rate.Give;
            return raw * (100 - System.Math.Max(0, System.Math.Min(100, rate.LossPct))) / 100;
        }

        /// <summary>오늘 이 비율로 더 낼 수 있는 양 (한도 없으면 long.MaxValue).</summary>
        public long LeftToday(CampaignState s, ExchangeRate rate, long nowUnixSeconds)
        {
            if (rate == null) return 0;
            if (rate.DailyLimit <= 0) return long.MaxValue;
            long used = 0;
            if (s.ExchangeDay == DayOf(nowUnixSeconds)) s.ExchangeUsed.TryGetValue(rate.Key, out used);
            return System.Math.Max(0, rate.DailyLimit - used);
        }

        /// <summary>교환 (from 자원 amount를 내고 to 자원을 받는다). 받은 양, 안 되면 0 (비율 없음 · 모자람 · 한도 · 받는 양 0).</summary>
        public long Exchange(CampaignState s, string from, string to, long amount, long nowUnixSeconds)
        {
            var rate = Rate(from, to);
            long get = Output(rate, amount);
            if (get <= 0 || amount > LeftToday(s, rate, nowUnixSeconds)) return 0;
            var w = new Wallet(s.Wallet);
            if (!w.TrySpend(from, amount)) return 0;
            w.Add(to, get);
            if (rate.DailyLimit > 0)
            {
                long day = DayOf(nowUnixSeconds);
                if (s.ExchangeDay != day) { s.ExchangeDay = day; s.ExchangeUsed.Clear(); }
                s.ExchangeUsed.TryGetValue(rate.Key, out var used);
                s.ExchangeUsed[rate.Key] = used + amount;
            }
            return get;
        }

        // ---------------- 금괴 스킬 해금 (행동) ----------------

        /// <summary>그 스킬의 해금 금괴 (스킬 단계 값). 해금 대상이 아니면 null (습득 조건이 없거나 단계 값이 없음).</summary>
        public long? UnlockPrice(string skillId)
        {
            if (_defs == null || !_defs.Skills.TryGetValue(skillId ?? "", out var skill) || skill.LearnConditions.Count == 0 || skill.Tier == null) return null;
            return UnlockGemByTier.TryGetValue(skill.Tier, out var p) ? p : (long?)null;
        }

        /// <summary>금괴로 스킬 얻기 조건을 해금 (계정 전체, 한 번). 이미 해금 · 대상 아님 · 금괴 모자람이면 false.</summary>
        public bool UnlockLearn(CampaignState s, string skillId)
        {
            var price = UnlockPrice(skillId);
            if (price == null || s.SkillLearnUnlocks.Contains(skillId)) return false;
            if (!new Wallet(s.Wallet).TrySpend(ResourceIds.Gem, price.Value)) return false;
            s.SkillLearnUnlocks.Add(skillId);
            return true;
        }

        /// <summary>데이터 검사: 자원 id, 양 · 비율, 한 방향(금괴로 되돌리는 교환 없음), 단계 이름.</summary>
        public List<string> Validate(ICollection<string> tierIds)
        {
            var errors = new List<string>();
            if (_defs?.EconomyDoc == null) return errors;
            bool Known(string r) => r == ResourceIds.Gold || r == ResourceIds.Supply || r == ResourceIds.Gem;
            for (int i = 0; i < Cycle.Count; i++)
                if (!Known(Cycle[i].Resource) || Cycle[i].Amount <= 0) errors.Add("economy attendance.cycle[" + i + "]: 자원 · 양");
            foreach (var x in Rates)
            {
                if (!Known(x.From) || !Known(x.To) || x.From == x.To) errors.Add("economy exchange " + x.Key + ": 자원");
                if (x.To == ResourceIds.Gem) errors.Add("economy exchange " + x.Key + ": 금괴로 바꾸는 길은 없다 (한 방향)");
                if (x.Give <= 0 || x.Get <= 0 || x.LossPct < 0 || x.LossPct >= 100 || x.DailyLimit < 0) errors.Add("economy exchange " + x.Key + ": 비율 · 손실 · 한도");
                if (Output(x, x.Give) <= 0) errors.Add("economy exchange " + x.Key + ": give만큼 내도 받는 것이 없다");
            }
            foreach (var kv in UnlockGemByTier)
                if (kv.Value <= 0 || (tierIds != null && !tierIds.Contains(kv.Key))) errors.Add("economy skillLearnUnlock.gemByTier." + kv.Key);
            return errors;
        }
    }
}
