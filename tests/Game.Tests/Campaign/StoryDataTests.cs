using System.Linq;
using Game.Data.Defs;
using Game.Flow;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>스토리 데이터: 모든 스텝을 읽을 수 있고, 대사의 말하는 장수·문자열이 있다.</summary>
    public class StoryDataTests
    {
        [Fact(DisplayName = "스텝 순서의 모든 스텝: 맵·적 장수가 있고, 대사 파일이 있으면 말하는 장수와 문구가 데이터에 있다")]
        public void StepsAndDialogue()
        {
            Assert.True(Kit.D.StepIndex.Order.Count >= 6);
            foreach (var id in Kit.D.StepIndex.Order)
            {
                var step = StepDef.Load(Kit.Source, id);
                Assert.True(Kit.Source.Exists("Maps/" + step.Map + ".json"), id + ": 맵 " + step.Map);
                foreach (var e in step.Enemy) Assert.True(Kit.D.Generals.ContainsKey(e.General), id + ": 장수 " + e.General);
                Assert.True(Kit.D.Strings.ContainsKey(step.NameKey), id + ": 이름 " + step.NameKey);
                var d = DialogueScript.Load(Kit.Source, id);
                Assert.NotEmpty(d.Before);
                foreach (var l in d.Before) Check(id, l);
                foreach (var l in d.After) Check(id, l);
                if (step.Chapter > 0) Assert.True(Kit.D.Strings.ContainsKey("chapter." + step.Chapter + ".name"), id + ": 장 이름");
                foreach (var l in d.Before)
                {
                    string side = DialogueScript.SideOf(l, step);
                    Assert.Equal(l.Speaker == null ? DialogueScript.Narration : step.Enemy.Any(e => e.General == l.Speaker) ? DialogueScript.Enemy : DialogueScript.Ally, side);
                }
            }
            Assert.Empty(DialogueScript.Load(Kit.Source, "no_such_step").Before);
        }

        private static void Check(string id, DialogueLine l)
        {
            Assert.True(l.Speaker == null || Kit.D.Generals.ContainsKey(l.Speaker), id + ": 말하는 장수 " + l.Speaker);
            Assert.True(Kit.D.Strings.ContainsKey(l.Key), id + ": 문구 " + l.Key);
        }
    }
}
