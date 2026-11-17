using System;
using System.IO;
using Newtonsoft.Json;

namespace Game.Server
{
    /// <summary>
    /// store.json(폴더 저장소) → Firestore 한 번 옮기기 (docs/impl/db.md 6장, 3단계).
    /// 쓰는 법: dotnet Game.Server.dll migrate --from &lt;store.json이 있는 폴더&gt; --project &lt;GCP 프로젝트&gt; [--database &lt;id&gt;] [--dry]
    /// 같은 것을 두 번 돌려도 같은 결과 (문서를 덮어씀). --dry면 읽어서 수만 센다 (Firestore에 쓰지 않음).
    /// 끝나면 모음별 쓴 수와, 원본에서 센 수를 표로 찍는다 (맞춰 보기).
    /// </summary>
    public static class Migrate
    {
        public static int Run(string[] args)
        {
            string from = Arg(args, "--from"), project = Arg(args, "--project"), database = Arg(args, "--database");
            bool dry = Array.IndexOf(args, "--dry") >= 0;
            if (from == null || (!dry && project == null))
            {
                Console.Error.WriteLine("쓰는 법: migrate --from <store.json이 있는 폴더> --project <GCP 프로젝트> [--database <id>] [--dry]");
                return 2;
            }
            if (!File.Exists(Path.Combine(from, "store.json")))
            {
                Console.Error.WriteLine($"{from}/store.json이 없습니다.");
                return 2;
            }
            var source = new FolderStore(from).Export();
            Console.WriteLine("원본 (store.json):");
            Console.WriteLine($"  계정 {source.Memberships?.Count ?? 0} · 토큰 {source.Tokens?.Count ?? 0} · 외부 로그인 {source.External?.Count ?? 0} · 닉네임 {source.Nicknames?.Count ?? 0}");
            foreach (var g in source.Saves ?? new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, Game.Online.CloudSave>>())
                Console.WriteLine($"  게임 {g.Key}: 세이브 {g.Value.Count} · 영토 {Count(source.Territory, g.Key)} · 연합 {Count(source.Alliances, g.Key)} · 등록 {(source.Listings != null && source.Listings.TryGetValue(g.Key, out var l) ? l.Count : 0)}");
            if (dry) return 0;
            var counts = FirestoreStore.Create(project, database).Import(source);
            Console.WriteLine("Firestore에 쓴 문서:");
            Console.WriteLine(JsonConvert.SerializeObject(counts, Formatting.Indented));
            return 0;
        }

        private static int Count<V>(System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, V>> d, string g) =>
            d != null && d.TryGetValue(g, out var x) ? x.Count : 0;

        private static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
