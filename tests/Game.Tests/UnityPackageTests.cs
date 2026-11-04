using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Game.Tests
{
    /// <summary>
    /// src/는 Unity 로컬 패키지(com.myproject.gamecore)이기도 하다. Unity 없이도 CI에서 잡을 수 있는 것을 확인한다.
    /// </summary>
    public class UnityPackageTests
    {
        private static string Src => Path.Combine(TestData.RepoRoot, "src");

        private static IEnumerable<string> Entries()
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(Src, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(Src, path).Replace('\\', '/');
                var parts = rel.Split('/');
                if (parts.Any(p => p == "bin" || p == "obj" || p.StartsWith("."))) continue;
                yield return rel;
            }
        }

        [Fact(DisplayName = "src의 모든 파일·폴더에 .meta가 있고, 짝 없는 .meta는 없다 (새 파일은 python3 tools/unity_meta.py)")]
        public void MetaFiles()
        {
            var all = Entries().ToList();
            var set = new HashSet<string>(all);
            var missing = all.Where(e => !e.EndsWith(".meta") && !set.Contains(e + ".meta")).ToList();
            var orphan = all.Where(e => e.EndsWith(".meta") && !set.Contains(e.Substring(0, e.Length - 5))).ToList();
            Assert.True(missing.Count == 0, ".meta 없음: " + string.Join(", ", missing));
            Assert.True(orphan.Count == 0, "짝 없는 .meta: " + string.Join(", ", orphan));
        }

        [Fact(DisplayName = "모듈마다 asmdef가 있고, 참조가 csproj의 프로젝트 참조와 같으며, 규칙 어셈블리는 UnityEngine을 참조하지 않는다")]
        public void AsmdefsMatchProjects()
        {
            foreach (var csproj in Directory.GetFiles(Src, "*.csproj", SearchOption.AllDirectories))
            {
                string name = Path.GetFileNameWithoutExtension(csproj);
                string asmdefPath = Path.Combine(Path.GetDirectoryName(csproj), name + ".asmdef");
                Assert.True(File.Exists(asmdefPath), $"{name}: asmdef가 없습니다.");
                var asmdef = JObject.Parse(File.ReadAllText(asmdefPath));
                Assert.Equal(name, asmdef.Value<string>("name"));
                Assert.True(asmdef.Value<bool>("noEngineReferences"), $"{name}: noEngineReferences가 꺼져 있습니다 (dev-rules 9.1).");
                var projectRefs = XDocument.Load(csproj).Descendants("ProjectReference")
                    .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include").Value)).OrderBy(x => x).ToList();
                var asmRefs = asmdef["references"].Values<string>().OrderBy(x => x).ToList();
                Assert.True(projectRefs.SequenceEqual(asmRefs), $"{name}: asmdef 참조 [{string.Join(", ", asmRefs)}] ≠ csproj [{string.Join(", ", projectRefs)}]");
            }
        }

        [Fact(DisplayName = "Unity 프로젝트가 src 패키지와 Newtonsoft 패키지를 쓴다")]
        public void ManifestReferencesPackage()
        {
            var manifest = JObject.Parse(File.ReadAllText(Path.Combine(TestData.RepoRoot, "unity", "Packages", "manifest.json")));
            var pkg = JObject.Parse(File.ReadAllText(Path.Combine(Src, "package.json")));
            Assert.Equal("file:../../src", manifest["dependencies"].Value<string>(pkg.Value<string>("name")));
            Assert.NotNull(manifest["dependencies"]["com.unity.nuget.newtonsoft-json"]);
        }
    }
}
