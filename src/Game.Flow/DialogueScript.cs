using System.Collections.Generic;
using Game.Data.Json;
using Newtonsoft.Json.Linq;

namespace Game.Flow
{
    /// <summary>대사 한 줄: 말하는 장수 id(없으면 해설), 문자열 키.</summary>
    public sealed class DialogueLine
    {
        public string Speaker { get; internal set; }
        public string Key { get; internal set; }
    }

    /// <summary>
    /// 스텝 전·후 대사 (story.md 1.3·3장, data/Dialogue/&lt;스텝&gt;.json). 파일이 없으면 빈 대사 (화면은 예전 한 줄 대사로 대신한다).
    /// 대사를 바꾸거나 더할 때 코드는 바꾸지 않는다.
    /// </summary>
    public sealed class DialogueScript
    {
        public IReadOnlyList<DialogueLine> Before { get; private set; } = new List<DialogueLine>();
        public IReadOnlyList<DialogueLine> After { get; private set; } = new List<DialogueLine>();

        public static string FileOf(string stepId) => "Dialogue/" + stepId + ".json";

        public static DialogueScript Load(IDataSource source, string stepId)
        {
            var s = new DialogueScript();
            if (source == null || stepId == null || !source.Exists(FileOf(stepId))) return s;
            var o = JsonUtil.ParseObject(source.ReadText(FileOf(stepId)));
            s.Before = Read(o["before"] as JArray, FileOf(stepId));
            s.After = Read(o["after"] as JArray, FileOf(stepId));
            return s;
        }

        private static List<DialogueLine> Read(JArray a, string file)
        {
            var list = new List<DialogueLine>();
            if (a == null) return list;
            foreach (JObject l in a)
                list.Add(new DialogueLine { Speaker = l.Value<string>("speaker"), Key = JsonUtil.RequireString(l, "key", file) });
            return list;
        }
    }
}
