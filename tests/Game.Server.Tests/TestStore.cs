using System;
using Microsoft.AspNetCore.Hosting;

namespace Game.Server.Tests
{
    /// <summary>
    /// Firestore로 돌리는 테스트(CI firestore-tests, 환경 변수 Game__Store=firestore)에서 테스트 서버마다 다른 프로젝트 id(= 다른 데이터베이스)를 쓴다.
    /// 메모리 저장소처럼 서버마다 자료가 따로라야, 시계가 다른 서버(고정 시계 · 실제 시계)가 서로의 계정 삭제 · 깃발을 건드리지 않는다.
    /// </summary>
    public static class TestStore
    {
        public static void Isolate(IWebHostBuilder builder)
        {
            if (string.Equals(Environment.GetEnvironmentVariable("Game__Store"), "firestore", StringComparison.OrdinalIgnoreCase))
                builder.UseSetting("Game:Firestore:ProjectId", "demo-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        }
    }
}
