using System.Collections.Generic;
using Game.Battle.Model.Commands;
using Game.Foundation.Math;

namespace Game.Battle.Model.Simulation
{
    /// <summary>
    /// 전투 결과 (경계 객체). 손실의 부상·사망 나누기, 군량 몰수, 포로는 바깥 모듈이 이 값으로 처리한다.
    /// </summary>
    public sealed class BattleResult
    {
        /// <summary>이긴 팀. 무승부면 null.</summary>
        public string Winner { get; internal set; }
        public string EndReason { get; internal set; }
        public long Ticks { get; internal set; }
        public Fix ElapsedSeconds { get; internal set; }
        public IReadOnlyList<CorpsResult> Corps { get; internal set; }
        /// <summary>받아들인 명령 기록 (리플레이·서버 검증용).</summary>
        public IReadOnlyList<BattleCommand> Commands { get; internal set; }
        public ulong StateHash { get; internal set; }
        /// <summary>팀 → 탐색한 칸 (y × 폭 + x). 세이브에 넣어 같은 스텝 재도전의 탐색 기억으로 쓴다 (fogMemory).</summary>
        public IReadOnlyDictionary<string, bool[]> Explored { get; internal set; }
        /// <summary>전투 직전 대결 결과 (장수 전적·성장 보상용).</summary>
        public IReadOnlyList<Contests.ContestResult> Contests { get; internal set; }
        /// <summary>장수 id → "light"(경상) 또는 "heavy"(중상) (R-23, 스위치 generalInjury).</summary>
        public IReadOnlyDictionary<string, string> GeneralInjuries { get; internal set; }
        /// <summary>포로가 된 장수 (포위·일기토). 전투 뒤 처리(구출·등용·해방)는 캠페인 모듈.</summary>
        public IReadOnlyList<CaptureRecord> Captures { get; internal set; }

        public CorpsResult Find(string corpsId)
        {
            foreach (var c in Corps) if (c.Id == corpsId) return c;
            return null;
        }
    }

    public sealed class CorpsResult
    {
        public string Id { get; internal set; }
        public string Team { get; internal set; }
        public CorpsStatus Status { get; internal set; }
        public long StartTroops { get; internal set; }
        public long RemainingTroops { get; internal set; }
        public long Losses => StartTroops - RemainingTroops;
        public long StartSupply { get; internal set; }
        public long RemainingSupply { get; internal set; }
        public Fix FinalMorale { get; internal set; }
        public Fix SkillPointsLeft { get; internal set; }
        public IReadOnlyList<UnitResult> Units { get; internal set; }
    }

    public sealed class UnitResult
    {
        public string UnitId { get; internal set; }
        public string Slot { get; internal set; }
        public string TroopType { get; internal set; }
        public long StartTroops { get; internal set; }
        public long RemainingTroops { get; internal set; }
        public long Losses => StartTroops - RemainingTroops;
    }
}
