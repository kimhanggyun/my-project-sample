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
        /// <summary>편 (17 시안): ally · enemy. 데이터에 없으면 SideOf가 스텝 적 장수로 정한다.</summary>
        public string Side { get; internal set; }
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

        public const string Ally = "ally", Enemy = "enemy", Narration = "narration";

        /// <summary>대사 파일이 없을 때 화면이 쓰는 한 줄 (예전 한 줄 대사).</summary>
        public static DialogueLine Line(string speaker, string key) => new DialogueLine { Speaker = speaker, Key = key };

        /// <summary>말하는 편 (17 시안: 아군 왼쪽 파랑 · 적 오른쪽 빨강): 해설(말하는 장수 없음) → narration, 데이터 side, 스텝 적 장수면 enemy, 나머지 ally.</summary>
        public static string SideOf(DialogueLine line, Game.Data.Defs.StepDef step)
        {
            if (line?.Speaker == null) return Narration;
            if (line.Side == Ally || line.Side == Enemy) return line.Side;
            if (step?.Enemy != null) foreach (var e in step.Enemy) if (e.General == line.Speaker) return Enemy;
            return Ally;
        }

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
                list.Add(new DialogueLine { Speaker = l.Value<string>("speaker"), Key = JsonUtil.RequireString(l, "key", file), Side = l.Value<string>("side") });
            return list;
        }
    }
}
