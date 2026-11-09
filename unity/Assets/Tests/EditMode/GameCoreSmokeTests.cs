using System;
using System.IO;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Data.Validation;
using Game.Flow;
using Game.Foundation.Time;
using NUnit.Framework;
using UnityEngine;

namespace Game.Unity.Tests
{
    /// <summary>
    /// Unity 안에서 규칙 코드(src, 로컬 패키지)가 돌아가는지 확인한다.
    /// 같은 입력이면 .NET 테스트와 같은 상태 지문이 나와야 한다 (기기·런타임과 무관한 결정적 전투, verification R-29).
    /// </summary>
    public class GameCoreSmokeTests
    {
        // .NET(dotnet test)에서 같은 입력으로 계산한 값. 규칙·데이터를 바꾸면 함께 다시 계산한다.
        private const string ExpectedStep01Seed1Hash = "4c0f3d23f9926df6";
        private const long ExpectedStep01Seed1Ticks = 514;

        private static string DataRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data"));

        private static DefinitionSet Defs() => DefinitionSet.Load(new FileDataSource(DataRoot), "ancientwar");

        [Test]
        public void DataLoadsAndIsValid()
        {
            Assert.IsTrue(Directory.Exists(DataRoot), "저장소의 data 폴더를 찾지 못했습니다: " + DataRoot);
            var errors = DataValidator.Validate(Defs());
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void Step01BattleMatchesDotNet()
        {
            var defs = Defs();
            var flow = new StoryFlow(defs, new FileDataSource(DataRoot), new ManualTimeProvider(new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero)));
            var state = StartingKitService.CreateNewGame(defs);
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 1, Delegate = true });
            Assert.IsTrue(plan.Ok, string.Join("\n", plan.Errors));
            var result = BattleSimulation.Run(plan.Setup);
            Assert.AreEqual(ExpectedStep01Seed1Ticks, result.Ticks);
            Assert.AreEqual("teamA", result.Winner);
            Assert.AreEqual(ExpectedStep01Seed1Hash, result.StateHash.ToString("x16"),
                "Unity(Mono)와 .NET의 전투 결과가 다릅니다. 결정적 계산이 깨졌습니다.");
        }
    }
}
