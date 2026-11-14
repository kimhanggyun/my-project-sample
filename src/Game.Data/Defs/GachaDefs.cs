using System.Collections.Generic;
using Game.Data.Json;
using Game.Foundation.Math;
using Newtonsoft.Json.Linq;

namespace Game.Data.Defs
{
    /// <summary>장수 등급 하나와 그 등급의 모집 확률.</summary>
    public sealed class GachaGradeDef
    {
        public string Id { get; internal set; }
        public Fix Rate { get; internal set; }
        /// <summary>등급 순서 (0 = 가장 낮음).</summary>
        public int Rank { get; internal set; }
    }

    /// <summary>모집(뽑기) 하나 (generals.md 5장): 재화·가격, 10회 보장 등급, 천장, 장수 목록.</summary>
    public sealed class GachaBannerDef
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        public string Currency { get; internal set; }
        public long CostOne { get; internal set; }
        public long CostTen { get; internal set; }
        public string TenGuarantee { get; internal set; }
        public string PityGrade { get; internal set; }
        public int PityCount { get; internal set; }
        public IReadOnlyList<string> Pool { get; internal set; }
        /// <summary>하루 무료 1회 모집 횟수 (R-100, 서버 시각 unix 일). 0 = 없음. 광고는 화면이 (꺼져 있으면 광고 없이).</summary>
        public int FreeDaily { get; internal set; }
    }

    /// <summary>
    /// 모집 데이터 (Rules/gacha.json, verification R-51). 읽을 때 검사: 등급 확률 합 = 1, 목록의 장수가 있고 등급이 데이터에 있음,
    /// 확률이 있는 등급마다 목록에 장수가 하나 이상, 보장·천장 등급이 있음. 파일이 없으면 모집 없음.
    /// </summary>
    public sealed class GachaRules
    {
        public const string File = "gacha.json";
        public IReadOnlyList<GachaGradeDef> Grades { get; private set; } = new List<GachaGradeDef>();
        public IReadOnlyDictionary<string, GachaBannerDef> Banners { get; private set; } = new Dictionary<string, GachaBannerDef>();

        /// <summary>한계 돌파 단계마다 드는 조각 수 (단계 수 = 길이, R-57). 비면 한계 돌파 없음.</summary>
        public IReadOnlyList<long> LimitBreakShards { get; private set; } = new List<long>();
        /// <summary>한계 돌파 한 단계마다 오르는 레벨 상한.</summary>
        public int LevelCapPerStage { get; private set; }
        public int MaxLimitBreak => LimitBreakShards.Count;

        public static readonly GachaRules None = new GachaRules();

        public GachaGradeDef Grade(string id)
        {
            foreach (var g in Grades) if (g.Id == id) return g;
            return null;
        }

        public static GachaRules From(JObject o, IReadOnlyDictionary<string, GeneralDef> generals)
        {
            var grades = new List<GachaGradeDef>();
            Fix sum = Fix.Zero;
            foreach (JObject g in (JArray)o["grades"] ?? new JArray())
            {
                var def = new GachaGradeDef { Id = JsonUtil.RequireString(g, "id", File), Rate = JsonUtil.RequireFix(g, "rate", File), Rank = grades.Count };
                if (def.Rate < Fix.Zero) throw new DataException($"{File}: 등급 '{def.Id}' 확률은 0 이상.");
                sum += def.Rate;
                grades.Add(def);
            }
            if (grades.Count > 0 && sum != Fix.OneValue) throw new DataException($"{File}: 등급 확률 합이 1이 아닙니다 ({sum}).");
            var r = new GachaRules { Grades = grades };
            var banners = new Dictionary<string, GachaBannerDef>();
            foreach (JObject b in (JArray)o["banners"] ?? new JArray())
            {
                string id = JsonUtil.RequireString(b, "id", File);
                string c = File + ".banners." + id;
                var pity = b["pity"] as JObject;
                var def = new GachaBannerDef
                {
                    Id = id,
                    NameKey = JsonUtil.RequireString(b, "nameKey", c),
                    Currency = JsonUtil.RequireString(b, "currency", c),
                    CostOne = JsonUtil.RequireLong(b, "costOne", c),
                    CostTen = JsonUtil.RequireLong(b, "costTen", c),
                    TenGuarantee = b.Value<string>("tenGuarantee"),
                    PityGrade = pity?.Value<string>("grade"),
                    PityCount = pity?.Value<int?>("count") ?? 0,
                    Pool = JsonUtil.StringList(b, "pool"),
                    FreeDaily = b.Value<int?>("freeDaily") ?? 0,
                };
                if (def.FreeDaily < 0) throw new DataException($"{c}: freeDaily는 0 이상.");
                if (def.CostOne < 0 || def.CostTen < 0) throw new DataException($"{c}: 가격은 0 이상.");
                if (def.TenGuarantee != null && r.Grade(def.TenGuarantee) == null) throw new DataException($"{c}: 없는 보장 등급 '{def.TenGuarantee}'.");
                if (def.PityGrade != null && (r.Grade(def.PityGrade) == null || def.PityCount < 1)) throw new DataException($"{c}: 천장 등급·횟수가 잘못되었습니다.");
                foreach (var gid in def.Pool)
                {
                    if (!generals.TryGetValue(gid, out var gd)) throw new DataException($"{c}: 없는 장수 '{gid}'.");
                    if (r.Grade(gd.Grade) == null) throw new DataException($"{c}: 장수 '{gid}'의 등급 '{gd.Grade}'이 grades에 없습니다.");
                }
                foreach (var g in grades)
                {
                    if (g.Rate <= Fix.Zero) continue;
                    bool any = false;
                    foreach (var gid in def.Pool) if (generals[gid].Grade == g.Id) any = true;
                    if (!any) throw new DataException($"{c}: 확률이 있는 등급 '{g.Id}'의 장수가 목록에 없습니다.");
                }
                banners[id] = def;
            }
            r.Banners = banners;
            if (o["limitBreak"] is JObject lb)
            {
                var shards = new List<long>();
                foreach (var t in (JArray)lb["shards"] ?? new JArray())
                {
                    long n = t.Value<long>();
                    if (n < 1) throw new DataException($"{File}.limitBreak: 단계마다 조각은 1개 이상.");
                    shards.Add(n);
                }
                r.LimitBreakShards = shards;
                r.LevelCapPerStage = (int)JsonUtil.RequireLong(lb, "levelCapPerStage", File + ".limitBreak");
                if (r.LevelCapPerStage < 0) throw new DataException($"{File}.limitBreak: levelCapPerStage는 0 이상.");
            }
            return r;
        }
    }
}
