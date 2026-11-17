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

[Fact(DisplayName = "17 공성전 짧은 대사: 깃발 공성전은 공통 파일 siege.json, $attacker · $defender를 그 전투 장수로 (없으면 해설), 편은 데이터대로, 문구가 있다")]
        public void SiegeDialogue()
        {
            Assert.Equal(DialogueScript.SiegeFile, DialogueScript.FileOf("territory.flag:37566000,126978000"));
            Assert.Equal("Dialogue/step01.json", DialogueScript.FileOf("step01"));
            var d = DialogueScript.Load(Kit.Source, "territory.flag:37566000,126978000");
            Assert.InRange(d.Before.Count, 2, 3);
            Assert.Contains(d.Before, l => l.Speaker == DialogueScript.AttackerToken);
            Assert.Contains(d.Before, l => l.Speaker == DialogueScript.DefenderToken);
            foreach (var l in d.Before) Assert.True(Kit.D.Strings.ContainsKey(l.Key), l.Key);
            var lines = DialogueScript.Resolve(d.Before, "general_a", "enemy_general_01");
            Assert.All(lines, l => Assert.True(l.Speaker == "general_a" || l.Speaker == "enemy_general_01"));
            Assert.Equal(DialogueScript.Enemy, DialogueScript.SideOf(lines.First(l => l.Speaker == "enemy_general_01"), null));
            Assert.Equal(DialogueScript.Ally, DialogueScript.SideOf(lines.First(l => l.Speaker == "general_a"), null));
            var noDefender = DialogueScript.Resolve(d.Before, "general_a", null);
            Assert.Contains(noDefender, l => l.Speaker == null && DialogueScript.SideOf(l, null) == DialogueScript.Narration);
        }

                private static void Check(string id, DialogueLine l)
        {
            Assert.True(l.Speaker == null || Kit.D.Generals.ContainsKey(l.Speaker), id + ": 말하는 장수 " + l.Speaker);
            Assert.True(Kit.D.Strings.ContainsKey(l.Key), id + ": 문구 " + l.Key);
        }
    }
}
