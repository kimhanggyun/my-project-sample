using System.Collections.Generic;
using Game.Data.Json;
using Game.Foundation.Math;
using Newtonsoft.Json.Linq;

namespace Game.Data.Defs
{
    /// <summary>
    /// 대결 정의 (contests.json: 일기토·논쟁, battle.md 2.7.7). 행동마다 기본 행동(as)이 있고,
    /// 결과표 outcomes[나의 기본 행동][상대의 기본 행동] = 나에게 일어나는 일 목록.
    /// </summary>
    public sealed class ContestDef
    {
        public const string Special = "special";

        public string Id { get; internal set; }
        public string Flag { get; internal set; }
        public string Stat { get; internal set; }
        public string ResourceKey { get; internal set; }
        public string ChargeKey { get; internal set; }
        public Fix Start { get; internal set; }
        public int MaxRounds { get; internal set; }
        public Fix BaseDamage { get; internal set; }
        public Fix SpecialDamage { get; internal set; }
        public Fix SpecialCharge { get; internal set; }
        public int VariancePct { get; internal set; }
        public Fix CritChance { get; internal set; }
        public Fix CritMultiplier { get; internal set; }
        public Fix StatInfluencePctPerPoint { get; internal set; }
        public Fix StatInfluenceMaxPct { get; internal set; }
        /// <summary>끝났을 때 체력이 이 값 이하면 경상 (없으면 경상 없음).</summary>
        public Fix? LightInjuryAtOrBelow { get; internal set; }
        public Fix TriggerMaxStatDifference { get; internal set; }
        public Fix TriggerChance { get; internal set; }
        public IReadOnlyList<string> RefuseTemperaments { get; internal set; }
        /// <summary>행동 id (화면 순서).</summary>
        public IReadOnlyList<ContestActionDef> Actions { get; internal set; }
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<SkillPart>>> Outcomes { get; internal set; }
        /// <summary>결과 종류(win, fall, retreat, draw) → 전투 영향 값.</summary>
        public IReadOnlyDictionary<string, JObject> BattleEffects { get; internal set; }

        /// <summary>기운(charge)이 이만큼일 때 고를 수 있는 행동 id (필살은 기운이 가득 찼을 때만).</summary>
        public List<string> AvailableFor(Fix charge)
        {
            var list = new List<string>();
            foreach (var a in Actions)
                if (!a.RequiresFullCharge || charge >= SpecialCharge) list.Add(a.Id);
            return list;
        }

        public ContestActionDef Action(string id)
        {
            foreach (var a in Actions) if (a.Id == id) return a;
            return null;
        }

        public ContestActionDef ByBase(string baseId)
        {
            foreach (var a in Actions) if (a.Base == baseId) return a;
            return null;
        }

        internal static List<ContestDef> LoadAll(JObject doc)
        {
            var raw = new Dictionary<string, JObject>();
            var order = new List<string>();
            foreach (JObject c in (JArray)doc["contests"])
            {
                string id = JsonUtil.RequireString(c, "id", "contests");
                if (raw.ContainsKey(id)) throw new DataException($"contests.json: id '{id}'가 중복됩니다.");
                raw[id] = c;
                order.Add(id);
            }
            var list = new List<ContestDef>();
            foreach (var id in order) list.Add(From(raw[id], raw));
            return list;
        }

        private static ContestDef From(JObject o, Dictionary<string, JObject> all)
        {
            string id = JsonUtil.RequireString(o, "id", "contests");
            string c = "contests." + id;
            var actions = new List<ContestActionDef>();
            foreach (JObject a in (JArray)o["actions"])
                actions.Add(new ContestActionDef
                {
                    Id = JsonUtil.RequireString(a, "id", c + ".actions"),
                    NameKey = JsonUtil.RequireString(a, "nameKey", c + ".actions"),
                    Base = a.Value<string>("as") ?? a.Value<string>("id"),
                    RequiresFullCharge = a.Value<bool?>("requiresFullCharge") ?? false,
                });
            JObject outcomesObj = o["outcomes"] as JObject;
            if (outcomesObj == null)
            {
                string from = JsonUtil.RequireString(o, "outcomesFrom", c);
                if (!all.TryGetValue(from, out var src) || !(src["outcomes"] is JObject so))
                    throw new DataException($"{c}.outcomesFrom: 결과표가 있는 대결 '{from}'이 없습니다.");
                outcomesObj = so;
            }
            var outcomes = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<SkillPart>>>();
            foreach (var mine in outcomesObj.Properties())
            {
                var row = new Dictionary<string, IReadOnlyList<SkillPart>>();
                foreach (var theirs in ((JObject)mine.Value).Properties())
                {
                    var parts = new List<SkillPart>();
                    foreach (JObject e in (JArray)theirs.Value)
                        parts.Add(new SkillPart(JsonUtil.RequireString(e, "type", c + ".outcomes"), e, c + ".outcomes." + mine.Name + "." + theirs.Name));
                    row[theirs.Name] = parts;
                }
                outcomes[mine.Name] = row;
            }
            var effects = new Dictionary<string, JObject>();
            foreach (var p in JsonUtil.RequireObject(o, "battleEffects", c).Properties()) effects[p.Name] = (JObject)p.Value.DeepClone();
            var trig = JsonUtil.RequireObject(o, "trigger", c);
            return new ContestDef
            {
                Id = id,
                Flag = JsonUtil.RequireString(o, "flag", c),
                Stat = JsonUtil.RequireString(o, "stat", c),
                ResourceKey = JsonUtil.RequireString(o, "resourceKey", c),
                ChargeKey = JsonUtil.RequireString(o, "chargeKey", c),
                Start = JsonUtil.RequireFix(o, "start", c),
                MaxRounds = (int)JsonUtil.RequireLong(o, "maxRounds", c),
                BaseDamage = JsonUtil.RequireFix(o, "baseDamage", c),
                SpecialDamage = JsonUtil.RequireFix(o, "specialDamage", c),
                SpecialCharge = JsonUtil.RequireFix(o, "specialEnergy", c),
                VariancePct = (int)JsonUtil.RequireLong(o, "variancePct", c),
                CritChance = JsonUtil.RequireFix(o, "critChance", c),
                CritMultiplier = JsonUtil.RequireFix(o, "critMultiplier", c),
                StatInfluencePctPerPoint = JsonUtil.RequireFix(o, "statInfluencePctPerPoint", c),
                StatInfluenceMaxPct = JsonUtil.RequireFix(o, "statInfluenceMaxPct", c),
                LightInjuryAtOrBelow = o.TryGetValue("lightInjuryAtOrBelow", out var li) ? JsonUtil.ToFix(li, c) : (Fix?)null,
                TriggerMaxStatDifference = JsonUtil.RequireFix(trig, "maxStatDifference", c + ".trigger"),
                TriggerChance = JsonUtil.RequireFix(trig, "chance", c + ".trigger"),
                RefuseTemperaments = JsonUtil.StringList(trig, "refuseTemperaments"),
                Actions = actions,
                Outcomes = outcomes,
                BattleEffects = effects,
            };
        }
    }

    public sealed class ContestActionDef
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        /// <summary>결과표에서 쓰는 기본 행동 (attack, evade, charge, bow, retreat, special).</summary>
        public string Base { get; internal set; }
        /// <summary>기운(통찰)이 가득 찼을 때만 고를 수 있다 (필살·세뇌).</summary>
        public bool RequiresFullCharge { get; internal set; }
    }

    /// <summary>AI 난이도 하나 (ai.json). 판단의 질만 바꾼다.</summary>
    public sealed class AiDifficultyDef
    {
        public string Id { get; internal set; }
        public int ThinkIntervalMultiplier { get; internal set; }
        public Fix SkillUseChance { get; internal set; }
        public Fix CounterFormationChance { get; internal set; }
        /// <summary>안 보이는 숨는 지형을 간파로 살피는가.</summary>
        public bool Scouting { get; internal set; }
        /// <summary>사령관 협공 (R-75): 앞선 군단이 뒤처진 같은 편을 기다렸다 함께 치고, 붙으면 같은 편이 싸우는 적을 함께 친다.</summary>
        public bool Coordinate { get; internal set; }
        /// <summary>장수가 일기토를 거는가 (R-79). 판을 크게 흔드는 수라 어려움에서만.</summary>
        public bool Challenges { get; internal set; }
    }

    /// <summary>AI 수치 (ai.json).</summary>
    public sealed class AiRules
    {
        public IReadOnlyDictionary<string, AiDifficultyDef> Difficulties { get; internal set; }
        /// <summary>난이도 차례 (쉬움 → 어려움). 전체 난이도 설정이 스텝 난이도를 이 차례에서 옮긴다 (R-75).</summary>
        public IReadOnlyList<string> DifficultyOrder { get; internal set; } = new List<string>();
        /// <summary>전체 난이도 설정 id → 옮길 칸 수.</summary>
        public IReadOnlyDictionary<string, int> SettingShift { get; internal set; } = new Dictionary<string, int>();
        /// <summary>협공: 같은 편보다 이만큼(칸) 넘게 앞서면 기다린다.</summary>
        public Fix CoordinateGapCells { get; internal set; }

        /// <summary>스텝 난이도에 전체 난이도 설정을 더한다 (차례 안에서, 끝을 넘지 않게). 설정이 없거나 모르면 그대로.</summary>
        public string Shift(string stepDifficulty, string setting)
        {
            if (setting == null || !SettingShift.TryGetValue(setting, out int by) || by == 0) return stepDifficulty;
            int i = -1;
            for (int k = 0; k < DifficultyOrder.Count; k++) if (DifficultyOrder[k] == stepDifficulty) { i = k; break; }
            if (i < 0) return stepDifficulty;
            return DifficultyOrder[System.Math.Max(0, System.Math.Min(DifficultyOrder.Count - 1, i + by))];
        }
        public Fix RallyMoraleDrop { get; internal set; }
        public Fix RevealSearchCells { get; internal set; }
        /// <summary>AI가 일기토를 거는 최소 능력치 차이 (R-79).</summary>
        public Fix ChallengeEdge { get; internal set; }
        public IReadOnlyDictionary<string, int> ContestBaseWeights { get; internal set; }
        public int ContestFullChargeSpecialWeight { get; internal set; }
        public Fix ContestLowResourceAtOrBelow { get; internal set; }
        public int ContestLowResourceRetreatBonus { get; internal set; }
        public int ContestLowResourceEvadeBonus { get; internal set; }
        public int ContestOpponentFullChargeBowBonus { get; internal set; }
        public int ContestOpponentFullChargeRetreatBonus { get; internal set; }
        /// <summary>성향 → 기본 행동 → 가중치 가감.</summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> ContestTemperamentBonus { get; internal set; }
        /// <summary>위임 판단 수준을 정하는 장수 능력치 (평균).</summary>
        public IReadOnlyList<string> DelegationStats { get; internal set; }
        /// <summary>(평균 ≥ 기준, 난이도) — 높은 기준부터.</summary>
        public IReadOnlyList<(Fix atLeast, string difficulty)> DelegationLevels { get; internal set; }
        public string DelegationStance { get; internal set; }

        /// <summary>장수 능력치 평균으로 위임 판단 수준(난이도 id).</summary>
        public string DelegationDifficulty(System.Func<string, Fix> stat)
        {
            Fix sum = Fix.Zero;
            foreach (var s in DelegationStats) sum += stat(s);
            Fix avg = DelegationStats.Count == 0 ? Fix.Zero : sum / DelegationStats.Count;
            foreach (var (atLeast, difficulty) in DelegationLevels) if (avg >= atLeast) return difficulty;
            return DelegationLevels[DelegationLevels.Count - 1].difficulty;
        }

        private static Dictionary<string, int> ReadShift(JObject o)
        {
            var map = new Dictionary<string, int>();
            if (o["settingShift"] is JObject sh) foreach (var p in sh.Properties()) if (!p.Name.StartsWith("_")) map[p.Name] = p.Value.Value<int>();
            return map;
        }

        internal static AiRules From(JObject o)
        {
            const string c = "ai";
            var diffs = new Dictionary<string, AiDifficultyDef>();
            foreach (var p in JsonUtil.RequireObject(o, "difficulties", c).Properties())
            {
                var d = (JObject)p.Value;
                string ctx = c + ".difficulties." + p.Name;
                diffs[p.Name] = new AiDifficultyDef
                {
                    Id = p.Name,
                    ThinkIntervalMultiplier = (int)JsonUtil.RequireLong(d, "thinkIntervalMultiplier", ctx),
                    SkillUseChance = JsonUtil.RequireFix(d, "skillUseChance", ctx),
                    CounterFormationChance = JsonUtil.RequireFix(d, "counterFormationChance", ctx),
                    Scouting = d.Value<bool?>("scouting") ?? false,
                    Coordinate = d.Value<bool?>("coordinate") ?? false,
                    Challenges = d.Value<bool?>("challenges") ?? false,
                };
            }
            var sk = JsonUtil.RequireObject(o, "skills", c);
            var ct = JsonUtil.RequireObject(o, "contest", c);
            var weights = new Dictionary<string, int>();
            foreach (var p in JsonUtil.RequireObject(ct, "baseWeights", c + ".contest").Properties()) weights[p.Name] = p.Value.Value<int>();
            var temp = new Dictionary<string, IReadOnlyDictionary<string, int>>();
            foreach (var p in JsonUtil.RequireObject(ct, "temperamentBonus", c + ".contest").Properties())
            {
                var row = new Dictionary<string, int>();
                foreach (var q in ((JObject)p.Value).Properties()) row[q.Name] = q.Value.Value<int>();
                temp[p.Name] = row;
            }
            var del = JsonUtil.RequireObject(o, "delegation", c);
            var levels = new List<(Fix, string)>();
            foreach (JObject l in (JArray)del["levels"])
                levels.Add((JsonUtil.RequireFix(l, "atLeast", c + ".delegation"), JsonUtil.RequireString(l, "difficulty", c + ".delegation")));
            levels.Sort((a, b) => b.Item1.CompareTo(a.Item1));
            return new AiRules
            {
                DifficultyOrder = JsonUtil.StringList(o, "difficultyOrder"),
                SettingShift = ReadShift(o),
                CoordinateGapCells = JsonUtil.FixOr(o, "coordinateGapCells", Fix.FromInt(4), c),
                DelegationStats = JsonUtil.StringList(del, "stats"),
                DelegationLevels = levels,
                DelegationStance = JsonUtil.RequireString(del, "stance", c + ".delegation"),
                Difficulties = diffs,
                RallyMoraleDrop = JsonUtil.RequireFix(sk, "rallyMoraleDrop", c + ".skills"),
                RevealSearchCells = JsonUtil.RequireFix(sk, "revealSearchCells", c + ".skills"),
                ChallengeEdge = JsonUtil.FixOr(sk, "challengeEdge", Fix.FromInt(5), c + ".skills"),
                ContestBaseWeights = weights,
                ContestFullChargeSpecialWeight = (int)JsonUtil.RequireLong(ct, "fullChargeSpecialWeight", c),
                ContestLowResourceAtOrBelow = JsonUtil.RequireFix(ct, "lowResourceAtOrBelow", c),
                ContestLowResourceRetreatBonus = (int)JsonUtil.RequireLong(ct, "lowResourceRetreatBonus", c),
                ContestLowResourceEvadeBonus = (int)JsonUtil.RequireLong(ct, "lowResourceEvadeBonus", c),
                ContestOpponentFullChargeBowBonus = (int)JsonUtil.RequireLong(ct, "opponentFullChargeBowBonus", c),
                ContestOpponentFullChargeRetreatBonus = (int)JsonUtil.RequireLong(ct, "opponentFullChargeRetreatBonus", c),
                ContestTemperamentBonus = temp,
            };
        }
    }
}
