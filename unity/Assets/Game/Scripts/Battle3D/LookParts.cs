using System.Collections.Generic;
using Game.Data.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Unity.Battle3D
{
    /// <summary>부품의 도형 하나 (lookParts.json pieces[]). pos = 붙는 자리(at)에서 도형 가운데까지, size = 도형 전체 크기 (가로 · 높이 · 깊이), rot = 도형 돌림 (도).</summary>
    public sealed class LookPiece
    {
        public string Shape, At, Color;
        public Vector3 Pos, Size, Rot;
        /// <summary>좌우 대칭으로 하나 더 (x를 뒤집음).</summary>
        public bool Mirror;
    }

    /// <summary>겉모습 부품 하나 (R-150): 번호 (H01 …) · 이름 · 등급 · 도형들.</summary>
    public sealed class LookPart
    {
        public string Id, Name, Grade, Note, Slot;
        public readonly List<LookPiece> Pieces = new List<LookPiece>();
    }

    /// <summary>무기 묶음 (L01 …): 오른손 · 왼손 · 등 (2026-11-24 사용자 "보병은 칼과 방패가 세트").</summary>
    public sealed class LookLoadout
    {
        public string Id, Name, Right, Left, Back;
    }

    /// <summary>
    /// 겉모습 부품 목록 (R-150, data/Ui/lookParts.json — 2026-11-24 사용자 "부품마다 번호, 마음에 안 들면 번호로 지움").
    /// 번호 첫 글자 = 부위: H 투구 · A 갑옷 · C 등(망토 · 화살통 · 깃발) · S 칼 · 도끼 (오른손) · P 창 (오른손) · B 활 · 쇠뇌 (왼손) · D 방패 (왼손) · M 말 갖춤.
    /// 등급: basic 기본 · coin 엽전 · gem 금괴 · cash 결제 · general 장수 전용. 병종 · 장수 기본 차림은 troops · generals.
    /// 겉모습만 — 전투 계산과 무관.
    /// </summary>
    public sealed class LookPartLibrary
    {
        public const string File = "Ui/lookParts.json";
        public const string SlotHat = "hat", SlotArmor = "armor", SlotBack = "back", SlotRight = "right", SlotLeft = "left", SlotTack = "tack";
        /// <summary>부위 차례 (꾸미기 탭 · 모양 열쇠).</summary>
        public static readonly string[] Slots = { SlotHat, SlotArmor, SlotBack, SlotRight, SlotLeft, SlotTack };
        public static readonly string[] Grades = { "basic", "coin", "gem", "cash", "general" };

        public readonly Dictionary<string, LookPart> Parts = new Dictionary<string, LookPart>();
        /// <summary>파일 차례 그대로.</summary>
        public readonly List<LookPart> Ordered = new List<LookPart>();
        public readonly Dictionary<string, LookLoadout> Loadouts = new Dictionary<string, LookLoadout>();
        public readonly List<LookLoadout> OrderedLoadouts = new List<LookLoadout>();
        /// <summary>병종 id → 기본 차림 (hat · armor · back · right · left · tack · loadout).</summary>
        public readonly Dictionary<string, Dictionary<string, string>> Troops = new Dictionary<string, Dictionary<string, string>>();
        /// <summary>장수 성향 (warrior · standard · strategist) → 기본 차림.</summary>
        public readonly Dictionary<string, Dictionary<string, string>> Generals = new Dictionary<string, Dictionary<string, string>>();
        /// <summary>총대장이면 더 붙는 부품 (군기).</summary>
        public string CommanderExtra;
        /// <summary>옛 모양 이름 → 부품 (세트 look의 "helmet:crimson" 같은 값).</summary>
        public readonly Dictionary<string, string> LegacyHat = new Dictionary<string, string>(), LegacyShield = new Dictionary<string, string>();

        private static LookPartLibrary _shared;
        private static IDataSource _sharedSource;

        /// <summary>데이터 묶음의 부품 목록 (한 번 읽어 보관). 파일이 없거나 깨지면 빈 목록 (예전 모양으로 그림).</summary>
        public static LookPartLibrary Of(IDataSource data)
        {
            if (_shared != null && _sharedSource == data) return _shared;
            var lib = new LookPartLibrary();
            try { if (data != null && data.Exists(File)) lib.Parse(JObject.Parse(data.ReadText(File))); }
            catch (System.Exception e) { Debug.LogWarning("lookParts.json을 읽지 못했습니다: " + e.Message); }
            _shared = lib;
            _sharedSource = data;
            return lib;
        }

        public bool Empty => Parts.Count == 0;

        /// <summary>번호 첫 글자 → 부위 (모르면 null).</summary>
        public static string SlotOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            switch (id[0])
            {
                case 'H': return SlotHat;
                case 'A': return SlotArmor;
                case 'C': return SlotBack;
                case 'S': case 'P': return SlotRight;
                case 'B': case 'D': return SlotLeft;
                case 'M': return SlotTack;
                default: return null;
            }
        }

        /// <summary>등급 차례 (기본 0 … 장수 4, 모르면 0).</summary>
        public static int GradeRank(string grade)
        {
            int i = System.Array.IndexOf(Grades, grade);
            return i < 0 ? 0 : i;
        }

        public int RankOf(string partId) => partId != null && Parts.TryGetValue(partId, out var p) ? GradeRank(p.Grade) : -1;

        public LookPart Find(string id) => id != null && Parts.TryGetValue(id, out var p) ? p : null;

        private void Parse(JObject doc)
        {
            if (doc["parts"] is JArray parts)
                foreach (var t in parts)
                {
                    if (!(t is JObject o)) continue;
                    var p = new LookPart { Id = (string)o["id"], Name = (string)o["name"], Grade = (string)o["grade"] ?? "basic", Note = (string)o["note"] };
                    p.Slot = SlotOf(p.Id);
                    if (p.Id == null || p.Slot == null || Parts.ContainsKey(p.Id)) continue;
                    if (o["pieces"] is JArray pieces)
                        foreach (var q in pieces)
                            if (q is JObject po)
                                p.Pieces.Add(new LookPiece
                                {
                                    Shape = (string)po["shape"] ?? "box",
                                    At = (string)po["at"],
                                    Color = (string)po["color"] ?? "main",
                                    Pos = Vec(po["pos"]),
                                    Size = Vec(po["size"], 0.01f),
                                    Rot = Vec(po["rot"]),
                                    Mirror = po["mirror"]?.Type == JTokenType.Boolean && (bool)po["mirror"],
                                });
                    Parts[p.Id] = p;
                    Ordered.Add(p);
                }
            if (doc["loadouts"] is JArray loads)
                foreach (var t in loads)
                {
                    if (!(t is JObject o) || (string)o["id"] == null) continue;
                    var l = new LookLoadout { Id = (string)o["id"], Name = (string)o["name"], Right = (string)o["right"], Left = (string)o["left"], Back = (string)o["back"] };
                    if (Loadouts.ContainsKey(l.Id)) continue;
                    Loadouts[l.Id] = l;
                    OrderedLoadouts.Add(l);
                }
            ReadOutfits(doc["troops"] as JObject, Troops);
            ReadOutfits(doc["generals"] as JObject, Generals);
            CommanderExtra = (string)doc["commanderExtra"];
            if (doc["legacy"]?["hat"] is JObject lh) foreach (var p in lh.Properties()) LegacyHat[p.Name] = (string)p.Value;
            if (doc["legacy"]?["shield"] is JObject ls) foreach (var p in ls.Properties()) LegacyShield[p.Name] = (string)p.Value;
        }

        private static void ReadOutfits(JObject o, Dictionary<string, Dictionary<string, string>> into)
        {
            if (o == null) return;
            foreach (var p in o.Properties())
            {
                if (!(p.Value is JObject w)) continue;
                var d = new Dictionary<string, string>();
                foreach (var kv in w.Properties()) if (kv.Value.Type == JTokenType.String) d[kv.Name] = (string)kv.Value;
                into[p.Name] = d;
            }
        }

        private static Vector3 Vec(JToken t, float fallback = 0f)
        {
            if (!(t is JArray a) || a.Count < 3) return new Vector3(fallback, fallback, fallback);
            return new Vector3(F(a[0]), F(a[1]), F(a[2]));
        }

        private static float F(JToken t) => t.Type == JTokenType.Float || t.Type == JTokenType.Integer ? (float)t : 0f;
    }
}
