using System.Collections.Generic;
using Game.Foundation.Math;

namespace Game.Battle.Model.Skills
{
    /// <summary>
    /// 장수 한 명의 책략 점수와 스킬별 남은 횟수 (verification R-11~R-13).
    /// </summary>
    public sealed class SkillPointPool
    {
        private readonly Dictionary<string, int> _usesLeft = new Dictionary<string, int>();

        public Fix Start { get; }
        public Fix Points { get; private set; }

        /// <param name="intellect">시작 점수 = 지략.</param>
        /// <param name="usesPerBattle">스킬 id → 전투당 사용 횟수.</param>
        public SkillPointPool(Fix intellect, IReadOnlyDictionary<string, int> usesPerBattle)
        {
            Start = intellect;
            Points = intellect;
            foreach (var kv in usesPerBattle) _usesLeft[kv.Key] = kv.Value;
        }

        public int UsesLeft(string skillId) => _usesLeft.TryGetValue(skillId, out var n) ? n : 0;

        public bool CanUse(string skillId, Fix cost, bool conditionsMet) =>
            conditionsMet && Points >= cost && UsesLeft(skillId) >= 1;

        /// <summary>
        /// 사용 시도. 성공·실패·취소와 상관없이 점수와 횟수를 소모한다 (R-12).
        /// 사용할 수 없으면 false, 아무것도 바뀌지 않는다.
        /// </summary>
        public bool TryConsume(string skillId, Fix cost, bool conditionsMet)
        {
            if (!CanUse(skillId, cost, conditionsMet)) return false;
            Points -= cost;
            _usesLeft[skillId] = _usesLeft[skillId] - 1;
            return true;
        }

        /// <summary>점수 회복 (R-13): 스위치가 켜져 있고 사기 ≥ 기준일 때만, 시작 점수를 넘지 않는다.</summary>
        public void Regenerate(Fix amount, Fix morale, Fix moraleBase, bool regenEnabled)
        {
            if (!regenEnabled || morale < moraleBase || amount <= Fix.Zero) return;
            Points = Fix.Min(Start, Points + amount);
        }
    }
}
