using System;
using System.IO;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Data.Rules;
using Game.Foundation.Math;
using Newtonsoft.Json.Linq;

namespace Game.Tests
{
    internal static class TestData
    {
        public const string Theme = "ancientwar";

        public static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "data", "Rules"))) dir = dir.Parent;
                if (dir == null) throw new InvalidOperationException("data/Rules 폴더를 찾을 수 없습니다.");
                return dir.FullName;
            }
        }

        public static string DataRoot => Path.Combine(RepoRoot, "data");

        /// <summary>실제 데이터 (규칙 테스트 R은 이 값으로 계산한다).</summary>
        public static DefinitionSet Real() => DefinitionSet.Load(new FileDataSource(DataRoot), Theme);

        /// <summary>수치 예시 테스트(N) 전용 고정 수치.</summary>
        public static BalanceData Example()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "balance.example.json");
            return BalanceData.From(JsonUtil.ParseObject(File.ReadAllText(path)));
        }

        public static RuleFlags Flags(DefinitionSet d, string overrideJson = null) =>
            d.ResolveFlags(overrideJson == null ? null : JObject.Parse(overrideJson));

        /// <summary>실제 데이터를 복사한 뒤 파일 하나를 바꿔 끼운 임시 데이터 (데이터 검사 실패 경우 확인용).</summary>
        public static DefinitionSet WithReplacedFile(string relativePath, string content)
        {
            string temp = Path.Combine(Path.GetTempPath(), "gamedata-" + Guid.NewGuid().ToString("N"));
            CopyDir(DataRoot, temp);
            File.WriteAllText(Path.Combine(temp, relativePath.Replace('/', Path.DirectorySeparatorChar)), content);
            return DefinitionSet.Load(new FileDataSource(temp), Theme);
        }

        private static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
            foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        public static Fix F(int v) => Fix.FromInt(v);
        public static Fix F(string v) => Fix.Parse(v);
    }
}
