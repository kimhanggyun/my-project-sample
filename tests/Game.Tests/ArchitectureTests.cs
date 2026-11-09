using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Game.Tests
{
    /// <summary>
    /// 의존 방향 검사 (docs/dev-rules.md 1장): 규칙 모듈은 아래쪽 모듈만 참조하고, 엔진·SDK를 참조하지 않는다.
    /// </summary>
    public class ArchitectureTests
    {
        private static readonly Dictionary<string, string[]> Allowed = new Dictionary<string, string[]>
        {
            ["Game.Foundation"] = new string[0],
            ["Game.Data"] = new[] { "Game.Foundation", "Newtonsoft.Json" },
            ["Game.Services"] = new[] { "Game.Foundation" },
            ["Game.Battle.Model"] = new[] { "Game.Foundation", "Game.Data", "Newtonsoft.Json" },
            ["Game.Campaign"] = new[] { "Game.Foundation", "Game.Data", "Newtonsoft.Json" },
            ["Game.Economy"] = new[] { "Game.Foundation", "Game.Data", "Newtonsoft.Json" },
            // 온라인 경계: 서버와 주고받는 메시지에 전투 명령 기록이 들어간다.
            ["Game.Online"] = new[] { "Game.Foundation", "Game.Data", "Game.Battle.Model", "Newtonsoft.Json" },
            ["Game.Progression"] = new[] { "Game.Foundation", "Game.Data" },
            // 흐름(조립) 모듈: 규칙 모듈들을 경계 객체로 잇는다. 규칙 모듈은 Flow를 모른다.
            ["Game.Flow"] = new[] { "Game.Foundation", "Game.Data", "Game.Battle.Model", "Game.Campaign", "Game.Economy", "Game.Progression", "Game.Online", "Newtonsoft.Json" },
        };

        private static IEnumerable<string> GameReferences(string assemblyName) =>
            Assembly.Load(assemblyName).GetReferencedAssemblies()
                .Select(a => a.Name)
                .Where(n => n.StartsWith("Game.") || n.StartsWith("Unity") || n.StartsWith("Newtonsoft"));

        [Fact(DisplayName = "규칙 모듈은 허용된 모듈만 참조한다 (한 방향 의존)")]
        public void DependencyDirection()
        {
            foreach (var kv in Allowed)
            {
                var illegal = GameReferences(kv.Key).Where(r => !kv.Value.Contains(r)).ToList();
                Assert.True(illegal.Count == 0, $"{kv.Key}가 허용되지 않은 모듈을 참조: {string.Join(", ", illegal)}");
            }
        }

        [Fact(DisplayName = "규칙 모듈은 Unity를 참조하지 않는다")]
        public void NoEngineReference()
        {
            foreach (var name in Allowed.Keys)
                Assert.DoesNotContain(GameReferences(name), r => r.StartsWith("Unity"));
        }
    }
}
