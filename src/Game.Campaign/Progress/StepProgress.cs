using System.Linq;
using System.Collections.Generic;
using System.Text;
using Game.Data.Defs;

namespace Game.Campaign.Progress
{
    /// <summary>
    /// 스텝 진행 (story.md 1.1): 차례 해금, 깬 스텝은 반복 가능, 구출 전투는 그 수용소에 아군 장수가 있을 때만.
    /// 탐색 기억은 스텝마다 저장한다 (승패 무관, 확보한 칸은 계속 늘어난다).
    /// </summary>
    public static class StepProgress
    {
        public static bool IsUnlocked(StepIndexDef index, CampaignState state, string stepId, IReadOnlyList<string> campsWithCaptives)
        {
            int i = -1;
            for (int k = 0; k < index.Order.Count; k++) if (index.Order[k] == stepId) i = k;
            if (i == 0) return true;
            if (i > 0) return state.ClearedSteps.Contains(index.Order[i - 1]);
            foreach (var kv in index.RescueSteps)
                if (kv.Value == stepId) return campsWithCaptives.Contains(kv.Key);
            return false;
        }

        /// <summary>스텝을 깼다고 기록. 처음 깼으면 true (보상은 처음만).</summary>
        public static bool MarkCleared(CampaignState state, string stepId)
        {
            if (state.ClearedSteps.Contains(stepId)) return false;
            state.ClearedSteps.Add(stepId);
            return true;
        }

        public static bool[] Explored(CampaignState state, string stepId, int cellCount)
        {
            var cells = new bool[cellCount];
            if (state.ExploredBySteps.TryGetValue(stepId, out var s))
                for (int i = 0; i < cellCount && i < s.Length; i++) cells[i] = s[i] == '1';
            return cells;
        }

        /// <summary>새로 탐색한 칸을 합친다 (이미 탐색한 칸은 그대로).</summary>
        public static void MergeExplored(CampaignState state, string stepId, bool[] cells)
        {
            var old = Explored(state, stepId, cells.Length);
            var sb = new StringBuilder(cells.Length);
            for (int i = 0; i < cells.Length; i++) sb.Append(old[i] || cells[i] ? '1' : '0');
            state.ExploredBySteps[stepId] = sb.ToString();
        }
    }
}
