using System.Collections.Generic;
using Game.Data.Json;
using Game.Data.Rules;
using Game.Foundation.Math;
using Newtonsoft.Json.Linq;

namespace Game.Data.Defs
{
    /// <summary>
    /// 한 테마의 정의 데이터 전체. 공통 규칙(Rules/) + 테마(Themes/&lt;id&gt;/)를 읽는다.
    /// </summary>
    public sealed class DefinitionSet
    {
        public const string RulesDir = "Rules/";

        public string ThemeId { get; private set; }
        public IReadOnlyDictionary<string, StatDefinition> Stats { get; private set; }
        public IReadOnlyDictionary<string, TroopTypeDef> TroopTypes { get; private set; }
        public IReadOnlyDictionary<string, FormationDef> Formations { get; private set; }
        /// <summary>공격 진형 id → 유리한 상대 진형 id 집합.</summary>
        public IReadOnlyDictionary<string, HashSet<string>> FormationWins { get; private set; }
        /// <summary>지형 id → 병종 태그 → 전투 보정(%p).</summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, Fix>> TerrainCombat { get; private set; }
        public IReadOnlyDictionary<string, GeneralDef> Generals { get; private set; }
        public IReadOnlyDictionary<string, ProductDef> Products { get; private set; }
        public StartingKitDef StartingKit { get; private set; }
        public IReadOnlyDictionary<string, string> Strings { get; private set; }
        public JObject RuleFlagDefinitions { get; private set; }
        public JObject BalanceCommon { get; private set; }

        /// <summary>전투 수치 (combat.json).</summary>
        public CombatRules Combat { get; private set; }
        public JObject CombatCommon { get; private set; }
        /// <summary>지형 id → 이동 규칙.</summary>
        public IReadOnlyDictionary<string, TerrainRuleDef> TerrainRules { get; private set; }
        public IReadOnlyList<TroopCounterRule> TroopCounters { get; private set; }
        public IReadOnlyDictionary<string, TemperamentDef> Temperaments { get; private set; }
        public IReadOnlyDictionary<string, SkillDef> Skills { get; private set; }
        public IReadOnlyDictionary<string, ContestDef> Contests { get; private set; }
        public AiRules Ai { get; private set; }
        public IReadOnlyDictionary<string, ConditionDef> Weathers { get; private set; }
        public IReadOnlyDictionary<string, ConditionDef> TimesOfDay { get; private set; }
        public CampaignRules Campaign { get; private set; }
        public IReadOnlyList<GrowthTrackDef> GrowthTracks { get; private set; }
        public StepIndexDef StepIndex { get; private set; }
        public SpeechRules Speech { get; private set; }
        public LevelRules Levels { get; private set; }
        public IReadOnlyList<SecretDef> Secrets { get; private set; }
        /// <summary>장수 모집 (Rules/gacha.json, 없으면 모집 없음).</summary>
        public GachaRules Gacha { get; private set; } = GachaRules.None;
        /// <summary>연습 대결 (Rules/practice.json, 없으면 없음, R-59).</summary>
        public PracticeRules Practice { get; private set; } = PracticeRules.None;
        /// <summary>처음 안내 (Rules/guides.json, R-62). 파일이 없으면 안내 없음.</summary>
        public GuideRules Guides { get; private set; } = GuideRules.None;
        /// <summary>부대 키우기 (Rules/troopGrowth.json, 없으면 훈련·승급 없음).</summary>
        public TroopGrowthRules TroopGrowth { get; private set; } = TroopGrowthRules.None;

        /// <summary>스텝·규칙 세트 덮어쓰기를 적용한 전투 수치 (balance와 같은 순서).</summary>
        public CombatRules ResolveCombat(JObject stepOverride = null, JObject ruleSetOverride = null) =>
            CombatRules.From(BalanceResolver.Resolve(CombatCommon, stepOverride, ruleSetOverride));

        /// <summary>공통 balance만 적용한 값. 스텝·규칙 세트 덮어쓰기는 <see cref="ResolveBalance"/>.</summary>
        public BalanceData Balance { get; private set; }

        public BalanceData ResolveBalance(JObject stepOverride = null, JObject ruleSetOverride = null) =>
            BalanceData.From(BalanceResolver.Resolve(BalanceCommon, stepOverride, ruleSetOverride));

        public RuleFlags ResolveFlags(JObject stepOverride = null, JObject ruleSetOverride = null) =>
            RuleFlags.Build(RuleFlagDefinitions, stepOverride, ruleSetOverride);

        public static DefinitionSet Load(IDataSource source, string themeId, string language = "ko")
        {
            string theme = "Themes/" + themeId + "/";
            var set = new DefinitionSet { ThemeId = themeId };

            JObject Read(string path) => JsonUtil.ParseObject(source.ReadText(path));

            var stats = new Dictionary<string, StatDefinition>();
            foreach (JObject s in RequireArray(Read(RulesDir + "stats.json"), "stats", "stats.json"))
            {
                var def = new StatDefinition
                {
                    Id = JsonUtil.RequireString(s, "id", "stats"),
                    NameKey = JsonUtil.RequireString(s, "nameKey", "stats"),
                    Min = JsonUtil.RequireFix(s, "min", "stats"),
                    Max = JsonUtil.RequireFix(s, "max", "stats"),
                    Default = JsonUtil.RequireFix(s, "default", "stats"),
                };
                AddUnique(stats, def.Id, def, "stats.json");
            }
            set.Stats = stats;

            var troops = new Dictionary<string, TroopTypeDef>();
            foreach (JObject t in RequireArray(Read(RulesDir + "troopTypes.json"), "troopTypes", "troopTypes.json"))
            {
                string id = JsonUtil.RequireString(t, "id", "troopTypes");
                var def = new TroopTypeDef
                {
                    Id = id,
                    NameKey = JsonUtil.RequireString(t, "nameKey", "troopTypes." + id),
                    Tier = (int)JsonUtil.RequireLong(t, "tier", "troopTypes." + id),
                    Parent = t.Value<string>("parent"),
                    Tags = JsonUtil.StringList(t, "tags"),
                    SupplyPerDay = JsonUtil.RequireLong(t, "supplyPerDay", "troopTypes." + id),
                    AttackRange = JsonUtil.RequireFix(t, "attackRange", "troopTypes." + id),
                    Stats = ReadStatBlock(t["stats"] as JObject, stats, "troopTypes." + id),
                };
                AddUnique(troops, id, def, "troopTypes.json");
            }
            set.TroopTypes = troops;
            if (source.Exists(RulesDir + TroopGrowthRules.File)) set.TroopGrowth = TroopGrowthRules.From(Read(RulesDir + TroopGrowthRules.File), troops);

            var formationsDoc = Read(RulesDir + "formations.json");
            var formations = new Dictionary<string, FormationDef>();
            foreach (JObject f in RequireArray(formationsDoc, "formations", "formations.json"))
            {
                var def = new FormationDef
                {
                    Id = JsonUtil.RequireString(f, "id", "formations"),
                    NameKey = JsonUtil.RequireString(f, "nameKey", "formations"),
                    Group = JsonUtil.RequireString(f, "group", "formations"),
                    Tier = JsonUtil.RequireString(f, "tier", "formations"),
                    Slots = ReadSlots(f),
                };
                AddUnique(formations, def.Id, def, "formations.json");
            }
            set.Formations = formations;
            var wins = new Dictionary<string, HashSet<string>>();
            var matchup = JsonUtil.RequireObject(formationsDoc, "wins", "formations.json");
            foreach (var p in matchup.Properties())
                wins[p.Name] = new HashSet<string>(JsonUtil.StringList(matchup, p.Name));
            set.FormationWins = wins;

            var terrain = new Dictionary<string, IReadOnlyDictionary<string, Fix>>();
            var terrainDoc = JsonUtil.RequireObject(Read(RulesDir + "terrainCombat.json"), "terrain", "terrainCombat.json");
            foreach (var p in terrainDoc.Properties())
            {
                var row = new Dictionary<string, Fix>();
                foreach (var cell in ((JObject)p.Value).Properties())
                    row[cell.Name] = JsonUtil.ToFix(cell.Value, "terrainCombat." + p.Name + "." + cell.Name);
                terrain[p.Name] = row;
            }
            set.TerrainCombat = terrain;

            var products = new Dictionary<string, ProductDef>();
            foreach (JObject pr in RequireArray(Read(RulesDir + "products.json"), "products", "products.json"))
            {
                var def = new ProductDef
                {
                    Id = JsonUtil.RequireString(pr, "id", "products"),
                    Kind = JsonUtil.RequireString(pr, "kind", "products"),
                    Convenience = pr.Value<bool?>("convenience") ?? false,
                    PlayPaths = JsonUtil.StringList(pr, "playPaths"),
                };
                AddUnique(products, def.Id, def, "products.json");
            }
            set.Products = products;

            set.RuleFlagDefinitions = JsonUtil.RequireObject(Read(RulesDir + "ruleFlags.json"), "flags", "ruleFlags.json");
            set.BalanceCommon = Read(RulesDir + "balance.json");
            set.Balance = BalanceData.From(set.BalanceCommon);

            set.CombatCommon = Read(RulesDir + "combat.json");
            set.Combat = CombatRules.From(set.CombatCommon);

            var terrainRules = new Dictionary<string, TerrainRuleDef>();
            foreach (var p in JsonUtil.RequireObject(Read(RulesDir + "terrainRules.json"), "terrain", "terrainRules.json").Properties())
            {
                var t = (JObject)p.Value;
                string ctx = "terrainRules." + p.Name;
                var byTag = new Dictionary<string, Fix>();
                if (t["costByTag"] is JObject bt)
                    foreach (var q in bt.Properties()) byTag[q.Name] = JsonUtil.ToFix(q.Value, ctx + ".costByTag." + q.Name);
                terrainRules[p.Name] = new TerrainRuleDef
                {
                    Id = p.Name,
                    Cost = JsonUtil.RequireFix(t, "cost", ctx),
                    CostByTag = byTag,
                    Impassable = JsonUtil.StringList(t, "impassable"),
                    Height = (int)(t.Value<long?>("height") ?? 0),
                    Conceals = t.Value<bool?>("conceals") ?? false,
                    Flammable = t.Value<bool?>("flammable") ?? false,
                    BurnsTo = t.Value<string>("burnsTo"),
                    BurnSec = JsonUtil.FixOr(t, "burnSec", Fix.Zero, ctx),
                };
            }
            set.TerrainRules = terrainRules;

            var counters = new List<TroopCounterRule>();
            foreach (JObject r in RequireArray(Read(RulesDir + "troopCounters.json"), "rules", "troopCounters.json"))
                counters.Add(new TroopCounterRule
                {
                    AttackerTag = JsonUtil.RequireString(r, "attacker", "troopCounters"),
                    DefenderTag = JsonUtil.RequireString(r, "defender", "troopCounters"),
                    Pp = JsonUtil.RequireFix(r, "pp", "troopCounters"),
                });
            set.TroopCounters = counters;

            var temperaments = new Dictionary<string, TemperamentDef>();
            foreach (JObject t in RequireArray(Read(RulesDir + "temperaments.json"), "temperaments", "temperaments.json"))
            {
                var def = new TemperamentDef
                {
                    Id = JsonUtil.RequireString(t, "id", "temperaments"),
                    RetreatMorale = JsonUtil.RequireFix(t, "retreatMorale", "temperaments"),
                };
                AddUnique(temperaments, def.Id, def, "temperaments.json");
            }
            set.Temperaments = temperaments;

            var skills = new Dictionary<string, SkillDef>();
            foreach (JObject sk in RequireArray(Read(RulesDir + "skills.json"), "skills", "skills.json"))
            {
                var def = SkillDef.From(sk);
                AddUnique(skills, def.Id, def, "skills.json");
            }
            set.Skills = skills;

            var contests = new Dictionary<string, ContestDef>();
            foreach (var def in ContestDef.LoadAll(Read(RulesDir + "contests.json"))) contests[def.Id] = def;
            set.Contests = contests;
            set.Ai = AiRules.From(Read(RulesDir + "ai.json"));

            var cond = Read(RulesDir + "conditions.json");
            Dictionary<string, ConditionDef> Conditions(string key)
            {
                var d = new Dictionary<string, ConditionDef>();
                foreach (var p in JsonUtil.RequireObject(cond, key, "conditions.json").Properties())
                    d[p.Name] = ConditionDef.From(p.Name, (JObject)p.Value, "conditions." + key + "." + p.Name);
                return d;
            }
            set.Weathers = Conditions("weather");
            set.TimesOfDay = Conditions("timeOfDay");

            set.Campaign = CampaignRules.From(Read(RulesDir + "campaign.json"));
            set.GrowthTracks = GrowthTrackDef.LoadAll(Read(RulesDir + "growthTracks.json"));
            set.StepIndex = StepIndexDef.From(Read("Steps/steps.json"));
            set.Speech = SpeechRules.From(Read(RulesDir + "speech.json"));
            set.Levels = LevelRules.From(Read(RulesDir + "levels.json"));
            set.Secrets = SecretDef.LoadAll(Read(RulesDir + "secrets.json"));

            var generals = new Dictionary<string, GeneralDef>();
            foreach (JObject g in RequireArray(Read(theme + "generals.json"), "generals", "generals.json"))
            {
                string id = JsonUtil.RequireString(g, "id", "generals");
                var def = new GeneralDef
                {
                    Id = id,
                    NameKey = JsonUtil.RequireString(g, "nameKey", "generals." + id),
                    Grade = JsonUtil.RequireString(g, "grade", "generals." + id),
                    Temperament = JsonUtil.RequireString(g, "temperament", "generals." + id),
                    Traits = JsonUtil.StringList(g, "traits"),
                    InnateSkills = JsonUtil.StringList(g, "innateSkills"),
                    Recruitable = g.Value<bool?>("recruitable") ?? true,
                    Stats = ReadStatBlock(g["stats"] as JObject, stats, "generals." + id),
                    GrowthRates = ReadRates(g["growthRates"] as JObject, stats, "generals." + id),
                };
                AddUnique(generals, id, def, "generals.json");
            }
            set.Generals = generals;
            if (source.Exists(RulesDir + GachaRules.File)) set.Gacha = GachaRules.From(Read(RulesDir + GachaRules.File), set.Generals);
            if (source.Exists(RulesDir + PracticeRules.File)) set.Practice = PracticeRules.From(Read(RulesDir + PracticeRules.File), set.Temperaments);
            if (source.Exists(RulesDir + GuideRules.File)) set.Guides = GuideRules.From(Read(RulesDir + GuideRules.File));

            set.StartingKit = ReadStartingKit(Read(theme + "startingKit.json"));

            var strings = new Dictionary<string, string>();
            foreach (var p in Read(theme + "strings." + language + ".json").Properties())
                strings[p.Name] = p.Value.Value<string>();
            set.Strings = strings;

            return set;
        }

        private static IReadOnlyDictionary<string, Fix> ReadRates(JObject obj, IReadOnlyDictionary<string, StatDefinition> defs, string context)
        {
            var d = new Dictionary<string, Fix>();
            if (obj != null)
                foreach (var p in obj.Properties())
                {
                    if (!defs.ContainsKey(p.Name)) throw new DataException($"{context}.growthRates: 알 수 없는 능력치 '{p.Name}'.");
                    d[p.Name] = JsonUtil.ToFix(p.Value, context + ".growthRates." + p.Name);
                }
            return d;
        }

        private static IReadOnlyDictionary<string, SlotOffset> ReadSlots(JObject formation)
        {
            var slots = new Dictionary<string, SlotOffset>();
            if (formation["slots"] is JObject obj)
                foreach (var p in obj.Properties())
                {
                    var o = (JObject)p.Value;
                    string ctx = "formations." + formation.Value<string>("id") + ".slots." + p.Name;
                    slots[p.Name] = new SlotOffset(JsonUtil.RequireFix(o, "x", ctx), JsonUtil.RequireFix(o, "y", ctx));
                }
            return slots;
        }

        private static StartingKitDef ReadStartingKit(JObject doc)
        {
            const string c = "startingKit";
            var units = new List<StartingUnitDef>();
            foreach (JObject u in RequireArray(doc, "units", c))
                units.Add(new StartingUnitDef
                {
                    Id = JsonUtil.RequireString(u, "id", c + ".units"),
                    TroopType = JsonUtil.RequireString(u, "troopType", c + ".units"),
                    Troops = JsonUtil.RequireLong(u, "troops", c + ".units"),
                });
            var comps = new List<StartingCompositionDef>();
            foreach (JObject cp in RequireArray(doc, "compositions", c))
            {
                var slots = new Dictionary<string, string>();
                foreach (var s in JsonUtil.RequireObject(cp, "slots", c + ".compositions").Properties())
                    slots[s.Name] = s.Value.Value<string>();
                comps.Add(new StartingCompositionDef
                {
                    General = JsonUtil.RequireString(cp, "general", c + ".compositions"),
                    Formation = JsonUtil.RequireString(cp, "formation", c + ".compositions"),
                    Role = JsonUtil.RequireString(cp, "role", c + ".compositions"),
                    Slots = slots,
                });
            }
            var wallet = new Dictionary<string, long>();
            foreach (var p in JsonUtil.RequireObject(doc, "wallet", c).Properties())
                wallet[p.Name] = p.Value.Value<long>();
            return new StartingKitDef
            {
                Generals = JsonUtil.StringList(doc, "generals"),
                Units = units,
                Compositions = comps,
                Wallet = wallet,
                Formations = JsonUtil.StringList(doc, "formations"),
            };
        }

        private static StatBlock ReadStatBlock(JObject obj, IReadOnlyDictionary<string, StatDefinition> defs, string context)
        {
            var values = new Dictionary<string, Fix>();
            if (obj != null)
                foreach (var p in obj.Properties())
                {
                    if (!defs.ContainsKey(p.Name)) throw new DataException($"{context}: 알 수 없는 능력치 '{p.Name}'.");
                    values[p.Name] = JsonUtil.ToFix(p.Value, context + ".stats." + p.Name);
                }
            return new StatBlock(values, defs);
        }

        private static JArray RequireArray(JObject doc, string key, string context)
        {
            if (doc[key] is JArray a) return a;
            throw new DataException($"{context}: '{key}' 배열이 필요합니다.");
        }

        private static void AddUnique<T>(Dictionary<string, T> dict, string id, T value, string context)
        {
            if (dict.ContainsKey(id)) throw new DataException($"{context}: id '{id}'가 중복됩니다.");
            dict[id] = value;
        }
    }
}
