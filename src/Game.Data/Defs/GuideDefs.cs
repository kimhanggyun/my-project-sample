using System.Collections.Generic;
using Game.Data.Json;
using Newtonsoft.Json.Linq;

namespace Game.Data.Defs
{
    /// <summary>안내 하나 (R-62): 어느 자리(place)에서 처음 열 때 보여줄 문장 키들.</summary>
    public sealed class GuideDef
    {
        public string Id { get; internal set; }
        /// <summary>자리 id: "tab.&lt;탭 id&gt;", "screen.step", "screen.battle" … (화면이 자기 자리 id로 묻는다).</summary>
        public string Place { get; internal set; }
        public IReadOnlyList<string> Lines { get; internal set; } = new List<string>();
    }

    /// <summary>
    /// 처음 안내 (Rules/guides.json, R-62). 읽을 때 검사: id가 겹치지 않음, 자리·문장이 있음.
    /// 파일이 없으면 안내 없음.
    /// </summary>
    public sealed class GuideRules
    {
        public const string File = "guides.json";
        public static readonly GuideRules None = new GuideRules();

        public IReadOnlyList<GuideDef> Guides { get; private set; } = new List<GuideDef>();

        public GuideDef Find(string id)
        {
            foreach (var g in Guides) if (g.Id == id) return g;
            return null;
        }

        public static GuideRules From(JObject o)
        {
            const string c = "guides.json";
            var list = new List<GuideDef>();
            var ids = new HashSet<string>();
            if (!(o["guides"] is JArray arr)) throw new DataException(c + ".guides: 배열이 필요합니다.");
            foreach (var t in arr)
            {
                var g = t as JObject ?? throw new DataException(c + ": 안내는 객체여야 합니다.");
                string id = JsonUtil.RequireString(g, "id", c);
                if (!ids.Add(id)) throw new DataException($"{c}: 안내 id '{id}'가 겹칩니다.");
                string place = JsonUtil.RequireString(g, "place", c + "." + id);
                var lines = JsonUtil.StringList(g, "lines");
                if (lines.Count == 0) throw new DataException($"{c}.{id}: lines가 비었습니다.");
                list.Add(new GuideDef { Id = id, Place = place, Lines = lines });
            }
            return new GuideRules { Guides = list };
        }
    }
}
