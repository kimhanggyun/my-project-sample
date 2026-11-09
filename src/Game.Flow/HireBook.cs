using System.Collections.Generic;
using Game.Battle.Model.Simulation;
using Game.Data.Rules;
using Game.Online;

namespace Game.Flow
{
    /// <summary>고용 하나의 전투 결과 (서버가 재현해 확정한 전투에서만).</summary>
    public sealed class HireOutcome
    {
        public string ListingId { get; set; }
        /// <summary><see cref="HireResults"/>.</summary>
        public string Result { get; set; }
        public long TroopsStart { get; set; }
        public long TroopsLost { get; set; }
    }

    /// <summary>고용 기록 한 줄 (고용한 계정 쪽, 제한 계산용).</summary>
    public sealed class HireLogEntry
    {
        public string ListingId { get; set; }
        public long AtUnixSeconds { get; set; }
    }

    /// <summary>
    /// 고용 이력·제한 (hiring.md 3·4장, verification R-44): 저장소와 무관한 계산. 참고 서버·가짜 서버가 같이 쓴다.
    /// </summary>
    public static class HireBook
    {
        public const long DaySeconds = 24 * 3600;

        /// <summary>
        /// 전투 결과 → 고용마다 결과: 그 고용이 든 군단(장수 = 지휘한 군단, 부대 = 들어간 군단)의 상태로.
        /// 포로 → captured, 궤멸 → destroyed, 퇴각 → retreat, 그 밖 → 아군 승리면 win 아니면 loss. 잃은 병력은 장수 = 군단 전체, 부대 = 그 부대.
        /// </summary>
        public static List<HireOutcome> Outcomes(SortiePlan plan, BattleResult result, HiredForces hired)
        {
            var list = new List<HireOutcome>();
            if (hired == null) return list;
            var byCorps = new Dictionary<string, CorpsResult>();
            foreach (var c in result.Corps) byCorps[c.Id] = c;
            for (int i = 0; i < hired.Uses.Count; i++)
            {
                var use = hired.Uses[i];
                string unitId = HireRules.UnitIdOf(use.ListingId);
                bool isUnit = hired.Units.Exists(u => u.UnitId == unitId);
                string generalId = isUnit ? null : GeneralOf(hired, i);
                foreach (var cs in plan.Setup.Corps)
                {
                    if (cs.Team != StoryFlow.PlayerTeam || !byCorps.TryGetValue(cs.Id, out var cr)) continue;
                    if (isUnit)
                    {
                        var ur = Find(cr, unitId);
                        if (ur == null) continue;
                        list.Add(new HireOutcome { ListingId = use.ListingId, Result = ResultOf(cr, result), TroopsStart = ur.StartTroops, TroopsLost = ur.Losses });
                        break;
                    }
                    if (cs.General?.Id == generalId)
                    {
                        list.Add(new HireOutcome { ListingId = use.ListingId, Result = ResultOf(cr, result), TroopsStart = cr.StartTroops, TroopsLost = cr.Losses });
                        break;
                    }
                }
            }
            return list;
        }

        /// <summary>장수 고용은 Uses 순서대로 Generals에 들어 있다 (부대 고용은 건너뛴다).</summary>
        private static string GeneralOf(HiredForces hired, int useIndex)
        {
            int g = 0;
            for (int i = 0; i < useIndex; i++) if (hired.Uses[i].Count == 0) g++;
            return g < hired.Generals.Count ? hired.Generals[g].GeneralId : null;
        }

        private static UnitResult Find(CorpsResult cr, string unitId)
        {
            foreach (var u in cr.Units) if (u.UnitId == unitId) return u;
            return null;
        }

        private static string ResultOf(CorpsResult cr, BattleResult result)
        {
            switch (cr.Status)
            {
                case CorpsStatus.Captured: return HireResults.Captured;
                case CorpsStatus.Destroyed: return HireResults.Destroyed;
                case CorpsStatus.Retreating:
                case CorpsStatus.Retreated: return HireResults.Retreat;
                default: return result.Winner == StoryFlow.PlayerTeam ? HireResults.Win : HireResults.Loss;
            }
        }

        /// <summary>등록의 이력에 결과 하나를 더한다 (최근 전투는 앞에, recentMax개까지).</summary>
        public static void Record(HireListing listing, HireOutcome o, string stepId, long at, int recentMax)
        {
            if (listing.Stats == null) listing.Stats = new HireStats();
            if (listing.Recent == null) listing.Recent = new List<HireRecent>();
            var s = listing.Stats;
            s.Hires++;
            switch (o.Result)
            {
                case HireResults.Win: s.Wins++; break;
                case HireResults.Loss: s.Losses++; break;
                case HireResults.Retreat: s.Retreats++; break;
                case HireResults.Captured: s.Captured++; break;
                case HireResults.Destroyed: s.Destroyed++; break;
            }
            s.TroopsLost += o.TroopsLost;
            s.TroopsHired += o.TroopsStart;
            listing.Recent.Insert(0, new HireRecent { AtUnixSeconds = at, StepId = stepId, Result = o.Result, TroopsStart = o.TroopsStart, TroopsLost = o.TroopsLost });
            if (recentMax >= 0 && listing.Recent.Count > recentMax) listing.Recent.RemoveRange(recentMax, listing.Recent.Count - recentMax);
        }

        /// <summary>
        /// 고용 제한 (R-44): 지난 24시간 고용 수 + 이번 고용 수 ≤ 하루 상한, 같은 등록은 마지막 고용 뒤 대기시간이 지나야. 되면 null, 아니면 까닭.
        /// </summary>
        public static string CheckLimits(BalanceData b, IReadOnlyList<HireLogEntry> log, IReadOnlyList<HireUse> uses, long at)
        {
            if (uses == null || uses.Count == 0) return null;
            var (used, cooldowns) = Status(b, log, at);
            if (used + uses.Count > b.HireDailyMax) return $"하루 고용 수 {b.HireDailyMax}을 넘습니다 (지난 24시간 {used}).";
            foreach (var u in uses)
                if (u != null && cooldowns.TryGetValue(u.ListingId ?? "", out var until)) return $"등록 '{u.ListingId}'은 {until}까지 다시 고용할 수 없습니다.";
            return null;
        }

        /// <summary>지난 24시간 고용 수, 등록 id → 다시 고용할 수 있는 시각 (대기 중인 것만).</summary>
        public static (int used, Dictionary<string, long> cooldownUntil) Status(BalanceData b, IReadOnlyList<HireLogEntry> log, long now)
        {
            int used = 0;
            var cooldowns = new Dictionary<string, long>();
            long wait = b.HireCooldownMinutes * 60;
            if (log == null) return (0, cooldowns);
            foreach (var e in log)
            {
                if (e == null || e.AtUnixSeconds > now) continue;
                if (now - e.AtUnixSeconds < DaySeconds) used++;
                long until = e.AtUnixSeconds + wait;
                if (until > now && (!cooldowns.TryGetValue(e.ListingId, out var cur) || until > cur)) cooldowns[e.ListingId] = until;
            }
            return (used, cooldowns);
        }
    }
}
