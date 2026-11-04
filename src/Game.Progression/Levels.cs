using System.Collections.Generic;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Progression
{
    public sealed class LevelUpResult
    {
        public int Level { get; internal set; }
        public long Exp { get; internal set; }
        public int LevelsGained { get; internal set; }
        /// <summary>오른 능력치 합 (능력치 id → 값) = 성장률 × 오른 레벨 수.</summary>
        public Dictionary<string, Fix> StatGains { get; } = new Dictionary<string, Fix>();
    }

    /// <summary>
    /// 장수 레벨업 (battle.md 2.19.1): 경험치가 다음 레벨 필요치를 넘으면 레벨업, 능력치는 장수 성장률만큼 (무작위 없음).
    /// 최대 레벨이면 경험치는 더 쌓이지 않는다.
    /// </summary>
    public static class LevelEngine
    {
        public const string BattleLevelUpFlag = "battleLevelUp";

        public static LevelUpResult AddExp(LevelRules rules, IReadOnlyDictionary<string, Fix> growthRates, int level, long exp, long gained)
        {
            var r = new LevelUpResult { Level = level, Exp = exp };
            if (gained <= 0 || level >= rules.MaxLevel) return r;
            r.Exp = checked(r.Exp + gained);
            while (r.Level < rules.MaxLevel && r.Exp >= rules.ExpToNext(r.Level))
            {
                r.Exp -= rules.ExpToNext(r.Level);
                r.Level++;
                r.LevelsGained++;
            }
            if (r.Level >= rules.MaxLevel) r.Exp = 0;
            if (r.LevelsGained > 0)
                foreach (var kv in growthRates) r.StatGains[kv.Key] = kv.Value * r.LevelsGained;
            return r;
        }

        /// <summary>전투 경험치 = 스텝 경험치 × 전투 배율 (아주 더디게), 내림.</summary>
        public static long BattleExp(LevelRules rules, long stepExp) => (rules.BattleExpMultiplier * stepExp).FloorToLong();
    }
}
