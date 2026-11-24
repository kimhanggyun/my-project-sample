using System.Collections.Generic;
using Game.Data.Defs;
using Game.Flow;
using UnityEngine;

namespace Game.Unity.Battle3D
{
    /// <summary>
    /// 치장 그림 값 → 색 (cosmetics.md 3장 · 9장 3): 데이터의 look은 색 이름만 ("leather", "navy" …), 실제 색은 여기 (테마 그림 쪽).
    /// 팀 색(아군 · 적)과 가까운 갑옷 색은 전장에서 채도를 낮추고 어둡게 그린다 (미리보기는 원래 색).
    /// </summary>
    public static class CosmeticLooks
    {
        private static readonly Dictionary<string, Color> Named = new Dictionary<string, Color>
        {
            ["leather"] = new Color(0.55f, 0.36f, 0.19f),
            ["navy"] = new Color(0.13f, 0.17f, 0.34f),
            ["iron"] = new Color(0.55f, 0.57f, 0.6f),
            ["gold"] = new Color(0.86f, 0.68f, 0.24f),
            ["crimson"] = new Color(0.6f, 0.12f, 0.14f),
            ["black"] = new Color(0.12f, 0.11f, 0.1f),
            ["white"] = new Color(0.92f, 0.9f, 0.86f),
            ["darkred"] = new Color(0.4f, 0.08f, 0.09f),
            ["brown"] = new Color(0.36f, 0.24f, 0.13f),
            ["steel"] = new Color(0.34f, 0.36f, 0.4f),
        };

        private static CosmeticService _svc;
        private static DefinitionSet _svcDefs;

        /// <summary>치장 id의 그림 값 (모르면 null).</summary>
        public static IReadOnlyDictionary<string, string> Of(DefinitionSet defs, string id)
        {
            if (defs == null || id == null) return null;
            if (_svc == null || _svcDefs != defs) { _svc = new CosmeticService(defs); _svcDefs = defs; }
            return _svc.Find(id)?.Look;
        }

        /// <summary>요소의 색 ("모양:색"이면 색 쪽). 없으면 fallback.</summary>
        public static Color ColorOf(IReadOnlyDictionary<string, string> look, string element, Color fallback)
        {
            string v = null;
            if (look == null || !look.TryGetValue(element, out v) || v == null)
            {
                // 요소가 없으면 갑옷의 다른 요소 색을 쓴다 (한 벌이라 대개 같은 색)
                v = null;
                if (look != null) foreach (var kv in look) { v = kv.Value; break; }
                if (v == null) return fallback;
            }
            int colon = v.IndexOf(':');
            string name = colon >= 0 ? v.Substring(colon + 1) : v;
            return Named.TryGetValue(name, out var c) ? c : fallback;
        }

        /// <summary>색 이름 → 색 (R-149 SoldierLook).</summary>
        public static bool TryNamed(string name, out Color c) { c = default; return name != null && Named.TryGetValue(name, out c); }

        /// <summary>팀 색과 가까우면(색 거리 &lt; 0.25) 채도를 낮추고 어둡게 (팀 구분 유지).</summary>
        public static Color AwayFromTeams(Color c, params Color[] teams)
        {
            foreach (var t in teams)
            {
                float d = Mathf.Abs(c.r - t.r) + Mathf.Abs(c.g - t.g) + Mathf.Abs(c.b - t.b);
                if (d >= 0.25f) continue;
                float gray = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                var o = Color.Lerp(new Color(gray, gray, gray), c, 0.35f) * 0.7f;
                o.a = 1f;
                return o;
            }
            return c;
        }
    }
}
