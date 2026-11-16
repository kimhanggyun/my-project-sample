using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

        [Fact(DisplayName = "unity/Assets의 모든 파일·폴더에 .meta가 있고, 짝 없는 .meta는 없다 (빌드 생성물 GameDataBuild 제외)")]
        public void AssetMetaFiles()
        {
            string assets = Path.Combine(TestData.RepoRoot, "unity", "Assets");
            var all = new List<string>();
            foreach (var path in Directory.EnumerateFileSystemEntries(assets, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(assets, path).Replace('\\', '/');
                if (rel.Split('/').Any(p => p.StartsWith(".") || p.StartsWith("GameDataBuild"))) continue;
                all.Add(rel);
            }
            var set = new HashSet<string>(all);
            var missing = all.Where(e => !e.EndsWith(".meta") && !set.Contains(e + ".meta")).ToList();
            var orphan = all.Where(e => e.EndsWith(".meta") && !set.Contains(e.Substring(0, e.Length - 5))).ToList();
            Assert.True(missing.Count == 0, ".meta 없음: " + string.Join(", ", missing));
            Assert.True(orphan.Count == 0, "짝 없는 .meta: " + string.Join(", ", orphan));
        }

        [Fact(DisplayName = "화면 코드(unity/Assets/Game)가 쓰는 ui.* 문구 키가 테마 문자열에 있다")]
        public void UiStringKeysExist()
        {
            var strings = JObject.Parse(File.ReadAllText(Path.Combine(TestData.RepoRoot, "data", "Themes", "ancientwar", "strings.ko.json")));
            var missing = new SortedSet<string>();
            foreach (var file in Directory.GetFiles(Path.Combine(TestData.RepoRoot, "unity", "Assets", "Game"), "*.cs", SearchOption.AllDirectories))
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(ui\\.[A-Za-z0-9_.]*[A-Za-z0-9_])\""))
                    if (strings[m.Groups[1].Value] == null) missing.Add(m.Groups[1].Value);
            Assert.True(missing.Count == 0, "없는 문구 키: " + string.Join(", ", missing));
        }

        [Fact(DisplayName = "화면 코드의 T(\"ui.*\", 인자…)가 문구의 {n} 개수만큼 인자를 넘긴다 (모자라면 FormatException)")]
        public void UiStringFormatArgsSuffice()
        {
            var strings = JObject.Parse(File.ReadAllText(Path.Combine(TestData.RepoRoot, "data", "Themes", "ancientwar", "strings.ko.json")));
            var bad = new List<string>();
            foreach (var file in Directory.GetFiles(Path.Combine(TestData.RepoRoot, "unity", "Assets", "Game"), "*.cs", SearchOption.AllDirectories))
            {
                string src = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(src, "\\bT\\(\\s*\"(ui\\.[A-Za-z0-9_.]+)\""))
                {
                    string text = strings.Value<string>(m.Groups[1].Value);
                    if (text == null) continue;
                    int need = 0;
                    foreach (Match p in Regex.Matches(text, "\\{(\\d+)")) need = System.Math.Max(need, int.Parse(p.Groups[1].Value) + 1);
                    if (need == 0) continue;
                    int args = CountArgs(src, m.Index + m.Length);
                    if (args < need) bad.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value} {{n}} {need}개, 인자 {args}개");
                }
            }
            Assert.True(bad.Count == 0, "인자 모자람: " + string.Join("; ", bad));
        }

        /// <summary>키 문자열 바로 뒤부터 닫는 괄호까지 맨 바깥 쉼표 수 = 넘긴 인자 수.</summary>
        private static int CountArgs(string src, int i)
        {
            int depth = 0, args = 0;
            for (; i < src.Length; i++)
            {
                char c = src[i];
                if (c == '"') { i++; while (i < src.Length && src[i] != '"') { if (src[i] == '\\') i++; i++; } continue; }
                if (c == '\'') { i++; while (i < src.Length && src[i] != '\'') { if (src[i] == '\\') i++; i++; } continue; }
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') { if (depth == 0) break; depth--; }
                else if (c == ',' && depth == 0) args++;
            }
            return args;
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
