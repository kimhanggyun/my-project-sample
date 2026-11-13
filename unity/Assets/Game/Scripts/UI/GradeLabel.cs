using UnityEngine;

namespace Game.Unity.UI
{
    /// <summary>장수 등급 글씨 (generals.md 등급, E25): 일반 회색 · 고급 초록 · 희귀 파랑 · 영웅 보라 · 전설 금색.</summary>
    public static class GradeLabel
    {
        public static Color ColorOf(string grade)
        {
            var c = Base(grade);
            return Palette.IsLight ? new Color(c.r * 0.6f, c.g * 0.6f, c.b * 0.6f) : c; // 한지 바탕에서는 짙게
        }

        private static Color Base(string grade)
        {
            switch (grade)
            {
                case "advanced": return new Color(0.45f, 0.80f, 0.45f);
                case "rare": return new Color(0.40f, 0.65f, 1f);
                case "hero": return new Color(0.75f, 0.50f, 0.95f);
                case "legend": return new Color(1f, 0.80f, 0.30f);
                default: return new Color(0.70f, 0.70f, 0.70f);
            }
        }

        /// <summary>"[희귀]" 같은 색 글씨 (등급이 없으면 빈 문자열).</summary>
        public static string Tag(Game.Unity.Boot.GameContext ctx, string grade) =>
            string.IsNullOrEmpty(grade) ? "" : Ui.Colored("[" + ctx.T("grade." + grade) + "]", ColorOf(grade)) + " ";
    }
}
