using System.Collections.Generic;
using Game.Data.Json;
using Game.Foundation.Math;
using Newtonsoft.Json.Linq;

namespace Game.Data.Defs
{
    /// <summary>계급 하나 (army-v2.md 2장): 그 레벨부터 출진 군단 수·군단당 병종 칸 수·병종마다 최대 병력.</summary>
    public sealed class RankDef
    {
        public string Id { get; internal set; }
        public int MinLevel { get; internal set; }
        public int Corps { get; internal set; }
        public int Slots { get; internal set; }
        public long TroopMax { get; internal set; }
    }

    /// <summary>전투 밖 진행 수치 (campaign.json).</summary>
    public sealed class CampaignRules
    {
        public long RescueDeadlineSeconds { get; private set; }
        public long RecruitRetrySeconds { get; private set; }
        public Fix RecruitBaseChance { get; private set; }
        public Fix BrainwashBonus { get; private set; }
        public IReadOnlyDictionary<string, Fix> TemperamentRecruitBonus { get; private set; }
        /// <summary>해방 보상: 자원 id → 양.</summary>
        public IReadOnlyDictionary<string, long> ReleaseReward { get; private set; }
        public long LightInjurySeconds { get; private set; }
        public long HeavyInjurySeconds { get; private set; }
        /// <summary>즉시 치료 비용: 남은 시간 1시간당 돈.</summary>
        public long HealGoldPerHourLeft { get; private set; }
        public Fix CohesionWinGain { get; private set; }
        public Fix CohesionLossPenalty { get; private set; }
        public Fix CohesionMax { get; private set; }
        /// <summary>적 군량 포획률 (승리 시).</summary>
        public Fix SupplyCaptureRate { get; private set; }
        public int DefaultMaxCorps { get; private set; }
        /// <summary>상황별 편성을 둘 수 있는 상황 id (전장 유형, battle.md 3장). 이름은 문자열 "situation.&lt;id&gt;".</summary>
        public IReadOnlyList<string> FormationSituations { get; private set; }
        /// <summary>보유할 수 있는 부대 수 상한 (formation.md 2장).</summary>
        public int UnitCountMax { get; private set; }
        /// <summary>스킬북 가격 (돈): 스킬 단계 → 가격 (generals.md 4장). 단계가 없으면 상점에서 팔지 않는다.</summary>
        public IReadOnlyDictionary<string, long> SkillBookPriceByTier { get; private set; }
        /// <summary>계급(계정 레벨) 최대 (army-v2.md 2장).</summary>
        public int LordMaxLevel { get; private set; } = 1;
        /// <summary>계급 표 (minLevel 오름차순, 첫 계급은 1부터).</summary>
        public IReadOnlyList<RankDef> Ranks { get; private set; } = new List<RankDef>();
        public int RecruitBase { get; private set; }
        public int RecruitPerLevel { get; private set; }
        public int RecruitMax { get; private set; }

        /// <summary>그 레벨의 계급 (표 밖이면 가장 가까운 끝).</summary>
        public RankDef RankFor(int level)
        {
            RankDef r = Ranks.Count > 0 ? Ranks[0] : new RankDef { Id = "none", MinLevel = 1, Corps = DefaultMaxCorps, Slots = 6, TroopMax = long.MaxValue / 4 };
            foreach (var x in Ranks) if (x.MinLevel <= level) r = x;
            return r;
        }

        /// <summary>다음 계급 (없으면 null).</summary>
        public RankDef NextRank(int level)
        {
            foreach (var x in Ranks) if (x.MinLevel > level) return x;
            return null;
        }

        /// <summary>그 레벨에서 새 병사의 훈련도·결속 (%, 0~100): min(max, base + perLevel × (레벨 − 1)).</summary>
        public int RecruitQuality(int level) => System.Math.Min(RecruitMax, RecruitBase + RecruitPerLevel * (System.Math.Max(1, level) - 1));

        public static CampaignRules From(JObject o)
        {
            const string c = "campaign";
            var cap = JsonUtil.RequireObject(o, "captivity", c);
            var inj = JsonUtil.RequireObject(o, "injury", c);
            var coh = JsonUtil.RequireObject(o, "cohesion", c);
            var sup = JsonUtil.RequireObject(o, "supply", c);
            var temp = new Dictionary<string, Fix>();
            foreach (var p in JsonUtil.RequireObject(cap, "temperamentRecruitBonus", c).Properties()) temp[p.Name] = JsonUtil.ToFix(p.Value, c + ".temperamentRecruitBonus");
            var reward = new Dictionary<string, long>();
            foreach (var p in JsonUtil.RequireObject(cap, "releaseReward", c).Properties()) reward[p.Name] = p.Value.Value<long>();
            var books = new Dictionary<string, long>();
            if (o["skillBook"]?["priceGoldByTier"] is JObject pb)
                foreach (var p in pb.Properties()) books[p.Name] = p.Value.Value<long>();
            var ranks = new List<RankDef>();
            int lordMax = 1, rb = 0, rp = 0, rm = 0;
            if (o["lord"] is JObject lord)
            {
                lordMax = (int)JsonUtil.RequireLong(lord, "maxLevel", c + ".lord");
                var rec = JsonUtil.RequireObject(lord, "recruit", c + ".lord");
                rb = (int)JsonUtil.RequireLong(rec, "base", c + ".lord.recruit");
                rp = (int)JsonUtil.RequireLong(rec, "perLevel", c + ".lord.recruit");
                rm = (int)JsonUtil.RequireLong(rec, "max", c + ".lord.recruit");
                foreach (var t in (JArray)lord["ranks"] ?? new JArray())
                {
                    var ro = (JObject)t;
                    ranks.Add(new RankDef
                    {
                        Id = JsonUtil.RequireString(ro, "id", c + ".lord.ranks"),
                        MinLevel = (int)JsonUtil.RequireLong(ro, "minLevel", c + ".lord.ranks"),
                        Corps = (int)JsonUtil.RequireLong(ro, "corps", c + ".lord.ranks"),
                        Slots = (int)JsonUtil.RequireLong(ro, "slots", c + ".lord.ranks"),
                        TroopMax = JsonUtil.RequireLong(ro, "troopMax", c + ".lord.ranks"),
                    });
                }
                for (int i = 0; i < ranks.Count; i++)
                {
                    var r = ranks[i];
                    if (i == 0 ? r.MinLevel != 1 : r.MinLevel <= ranks[i - 1].MinLevel) throw new DataException(c + ".lord.ranks: minLevel은 1부터 오름차순이어야 합니다 (" + r.Id + ").");
                    if (r.Corps < 1 || r.Slots < 1 || r.Slots > 6 || r.TroopMax < 1) throw new DataException(c + ".lord.ranks: " + r.Id + "의 corps·slots(1~6)·troopMax가 범위를 벗어남.");
                }
                if (rb < 0 || rm > 100 || rb > rm || rp < 0) throw new DataException(c + ".lord.recruit: 0 ≤ base ≤ max ≤ 100, perLevel ≥ 0이어야 합니다.");
            }
            return new CampaignRules
            {
                LordMaxLevel = lordMax,
                Ranks = ranks,
                RecruitBase = rb,
                RecruitPerLevel = rp,
                RecruitMax = rm,
                SkillBookPriceByTier = books,
                FormationSituations = JsonUtil.StringList(o, "formationSituations"),
                UnitCountMax = (int)((o["units"] as JObject)?.Value<long?>("maxCount") ?? 20),
                RescueDeadlineSeconds = checked(JsonUtil.RequireLong(cap, "rescueDeadlineDays", c) * 86400),
                RecruitRetrySeconds = checked(JsonUtil.RequireLong(cap, "recruitRetryHours", c) * 3600),
                RecruitBaseChance = JsonUtil.RequireFix(cap, "recruitBaseChance", c),
                BrainwashBonus = JsonUtil.RequireFix(cap, "brainwashBonus", c),
                TemperamentRecruitBonus = temp,
                ReleaseReward = reward,
                LightInjurySeconds = checked(JsonUtil.RequireLong(inj, "lightHours", c) * 3600),
                HeavyInjurySeconds = checked(JsonUtil.RequireLong(inj, "heavyHours", c) * 3600),
                HealGoldPerHourLeft = JsonUtil.RequireLong(inj, "healGoldPerHourLeft", c),
                CohesionWinGain = JsonUtil.RequireFix(coh, "winGain", c),
                CohesionLossPenalty = JsonUtil.RequireFix(coh, "lossPenalty", c),
                CohesionMax = JsonUtil.RequireFix(coh, "max", c),
                SupplyCaptureRate = JsonUtil.RequireFix(sup, "captureRate", c),
                DefaultMaxCorps = (int)JsonUtil.RequireLong(o, "defaultMaxCorps", c),
            };
        }
    }

    /// <summary>장수 레벨 (levels.json).</summary>
    public sealed class LevelRules
    {
        public int MaxLevel { get; private set; }
        public long ExpBase { get; private set; }
        public long ExpPerLevel { get; private set; }
        public Fix BattleExpMultiplier { get; private set; }

        /// <summary>이 레벨에서 다음 레벨까지 필요한 경험치.</summary>
        public long ExpToNext(int level) => checked(ExpBase + ExpPerLevel * (level - 1));

        public static LevelRules From(JObject o) => new LevelRules
        {
            MaxLevel = (int)JsonUtil.RequireLong(o, "maxLevel", "levels"),
            ExpBase = JsonUtil.RequireLong(o, "expBase", "levels"),
            ExpPerLevel = JsonUtil.RequireLong(o, "expPerLevel", "levels"),
            BattleExpMultiplier = JsonUtil.RequireFix(o, "battleExpMultiplier", "levels"),
        };
    }

    /// <summary>숨은 보상 하나 (secrets.json, secrets.md 5장). 조건 종류는 흐름 모듈 등록표가 해석한다.</summary>
    public sealed class SecretDef
    {
        public string Id { get; internal set; }
        public bool Once { get; internal set; }
        /// <summary>특정 스텝에서만 (없으면 어느 스텝이든).</summary>
        public string Step { get; internal set; }
        /// <summary>"victory" 또는 "any".</summary>
        public string Result { get; internal set; }
        public IReadOnlyList<SkillPart> Conditions { get; internal set; }
        /// <summary>받는 장수: commander / duelWinner / casterOf.</summary>
        public string To { get; internal set; }
        /// <summary>casterOf일 때 그 스킬을 쓴 장수.</summary>
        public string CasterSkill { get; internal set; }
        public string RewardSkill { get; internal set; }
        public string RewardTrait { get; internal set; }
        public string HintKey { get; internal set; }

        internal static List<SecretDef> LoadAll(JObject doc)
        {
            var list = new List<SecretDef>();
            foreach (JObject s in (JArray)doc["secrets"])
            {
                string id = JsonUtil.RequireString(s, "id", "secrets");
                string c = "secrets." + id;
                var cond = JsonUtil.RequireObject(s, "condition", c);
                var reward = JsonUtil.RequireObject(s, "reward", c);
                var parts = new List<SkillPart>();
                if (cond["all"] is JArray all)
                    foreach (JObject p in all) parts.Add(new SkillPart(JsonUtil.RequireString(p, "type", c), p, c));
                list.Add(new SecretDef
                {
                    Id = id,
                    Once = s.Value<bool?>("once") ?? true,
                    Step = cond.Value<string>("step"),
                    Result = cond.Value<string>("result") ?? "victory",
                    Conditions = parts,
                    To = JsonUtil.RequireString(reward, "to", c),
                    CasterSkill = reward.Value<string>("casterSkill"),
                    RewardSkill = reward.Value<string>("skill"),
                    RewardTrait = reward.Value<string>("trait"),
                    HintKey = s.Value<string>("hintKey"),
                });
            }
            return list;
        }
    }

    /// <summary>성장 보상 규칙 하나 (growthTracks.json): 카운터가 조건을 만족하면 보상.</summary>
    public sealed class GrowthRuleDef
    {
        public string Counter { get; internal set; }
        /// <summary>몇마다 (0이면 안 씀).</summary>
        public long Every { get; internal set; }
        /// <summary>몇 이상 (null이면 안 씀).</summary>
        public long? AtLeast { get; internal set; }
        public bool Once { get; internal set; }
        public string RewardStat { get; internal set; }
        public Fix RewardAdd { get; internal set; }
        public string RewardTrait { get; internal set; }
    }

    public sealed class GrowthTrackDef
    {
        public string Track { get; internal set; }
        public string Flag { get; internal set; }
        public IReadOnlyList<GrowthRuleDef> Rules { get; internal set; }

        internal static List<GrowthTrackDef> LoadAll(JObject doc)
        {
            var list = new List<GrowthTrackDef>();
            foreach (JObject t in (JArray)doc["tracks"])
            {
                string c = "growthTracks." + t.Value<string>("track");
                var rules = new List<GrowthRuleDef>();
                foreach (JObject r in (JArray)t["rules"])
                {
                    var reward = JsonUtil.RequireObject(r, "reward", c);
                    rules.Add(new GrowthRuleDef
                    {
                        Counter = JsonUtil.RequireString(r, "counter", c),
                        Every = r.Value<long?>("every") ?? 0,
                        AtLeast = r.Value<long?>("atLeast"),
                        Once = r.Value<bool?>("once") ?? false,
                        RewardStat = reward.Value<string>("stat"),
                        RewardAdd = JsonUtil.FixOr(reward, "add", Fix.Zero, c),
                        RewardTrait = reward.Value<string>("trait"),
                    });
                }
                list.Add(new GrowthTrackDef { Track = JsonUtil.RequireString(t, "track", c), Flag = t.Value<string>("flag"), Rules = rules });
            }
            return list;
        }
    }

    /// <summary>스텝 순서와 구출 전투 연결 (Steps/steps.json).</summary>
    public sealed class StepIndexDef
    {
        public IReadOnlyList<string> Order { get; private set; }
        /// <summary>수용소 id → 구출 전투 스텝 id.</summary>
        public IReadOnlyDictionary<string, string> RescueSteps { get; private set; }
        /// <summary>훈련 전투 (R-85): 스텝 id → 열리는 데 필요한 병종 태그 (그 태그의 부대가 있으면). 스토리 순서 밖, 반복 가능.</summary>
        public IReadOnlyDictionary<string, string> Drills { get; private set; } = new Dictionary<string, string>();

        public static StepIndexDef From(JObject o)
        {
            var rescue = new Dictionary<string, string>();
            if (o["rescue"] is JObject r) foreach (var p in r.Properties()) rescue[p.Name] = p.Value.Value<string>();
            var drills = new Dictionary<string, string>();
            if (o["drills"] is JObject d) foreach (var p in d.Properties()) drills[p.Name] = p.Value.Value<string>();
            return new StepIndexDef { Order = JsonUtil.StringList(o, "order"), RescueSteps = rescue, Drills = drills };
        }
    }

    public sealed class StepUnitDef
    {
        public string Slot { get; internal set; }
        public string TroopType { get; internal set; }
        public long Troops { get; internal set; }
    }

    /// <summary>스텝 맵 오브젝트 (R-78): depot·watchtower·zone.</summary>
    public sealed class StepObjectDef
    {
        public string Id { get; internal set; }
        public string Type { get; internal set; }
        public Fix X { get; internal set; }
        public Fix Y { get; internal set; }
        public Fix Radius { get; internal set; }
        public Fix Value { get; internal set; }
        public string Team { get; internal set; }
        public Fix Seconds { get; internal set; }
        /// <summary>둑(dam): 터지면 잠기는 곳 (중심·반경, R-84).</summary>
        public Fix FloodX { get; internal set; }
        public Fix FloodY { get; internal set; }
        public Fix FloodRadius { get; internal set; }

        internal static List<StepObjectDef> List(JObject o, string c)
        {
            var list = new List<StepObjectDef>();
            if (!(o["objects"] is JArray arr)) return list;
            foreach (JObject x in arr)
                list.Add(new StepObjectDef
                {
                    Id = JsonUtil.RequireString(x, "id", c + ".objects"),
                    Type = JsonUtil.RequireString(x, "type", c + ".objects"),
                    X = JsonUtil.RequireFix(x, "x", c + ".objects"),
                    Y = JsonUtil.RequireFix(x, "y", c + ".objects"),
                    Radius = JsonUtil.FixOr(x, "radius", Fix.FromInt(3), c + ".objects"),
                    Value = JsonUtil.FixOr(x, "value", Fix.Zero, c + ".objects"),
                    Team = x.Value<string>("team"),
                    Seconds = JsonUtil.FixOr(x, "seconds", Fix.Zero, c + ".objects"),
                    FloodX = JsonUtil.FixOr(x, "floodX", Fix.Zero, c + ".objects"),
                    FloodY = JsonUtil.FixOr(x, "floodY", Fix.Zero, c + ".objects"),
                    FloodRadius = JsonUtil.FixOr(x, "floodRadius", Fix.Zero, c + ".objects"),
                });
            return list;
        }
    }

    public sealed class StepEnemyCorpsDef
    {
        public string Id { get; internal set; }
        public string General { get; internal set; }
        public string Role { get; internal set; }
        public string Formation { get; internal set; }
        public string Ai { get; internal set; }
        public string Difficulty { get; internal set; }
        /// <summary>군량 수량 (SupplyMinutes가 없을 때).</summary>
        public long Supply { get; internal set; }
        /// <summary>군량을 "분" 분량으로 (그 군단 병력 기준, R-6 식). 있으면 Supply보다 우선.</summary>
        public Fix? SupplyMinutes { get; internal set; }
        public IReadOnlyList<string> KnownFormations { get; internal set; }
        public IReadOnlyList<StepUnitDef> Units { get; internal set; }
        public Fix? X { get; internal set; }
        public Fix? Y { get; internal set; }
        public string Facing { get; internal set; }
        /// <summary>예비 군단 (증원 이벤트로 들어옴).</summary>
        public bool Reserve { get; internal set; }
    }

    /// <summary>스텝 연출 하나 (조건 → 행동들). 팀은 "player"/"enemy".</summary>
    public sealed class StepEventDef
    {
        public string Id { get; internal set; }
        public string TriggerType { get; internal set; }
        public long TriggerTick { get; internal set; }
        public string TriggerCorps { get; internal set; }
        public string TriggerTeam { get; internal set; }
        public Fix TriggerRatio { get; internal set; }
        public bool Once { get; internal set; }
        public IReadOnlyList<StepEventActionDef> Actions { get; internal set; }
    }

    public sealed class StepEventActionDef
    {
        public string Type { get; internal set; }
        public string Key { get; internal set; }
        public string Corps { get; internal set; }
        public string Stance { get; internal set; }
    }

    /// <summary>
    /// 스텝 하나 (Steps/&lt;id&gt;.json, battle.md 6장). 적군·맵·조건·보상만 정하고, 아군은 편성에서 가져온다.
    /// </summary>
    public sealed class StepDef
    {
        public string Id { get; private set; }
        public string NameKey { get; private set; }
        public string BattleType { get; private set; }
        public string Map { get; private set; }
        public int? MaxCorps { get; private set; }
        public Fix MaxMinutes { get; private set; }
        /// <summary>이 스텝에서 아군 장수가 잡히면 갇히는 수용소.</summary>
        public string Camp { get; private set; }
        /// <summary>구출 전투면 그 수용소 id (이기면 그 수용소의 아군 장수 구출).</summary>
        public string RescueCamp { get; private set; }
        public IReadOnlyList<string> DefeatConditions { get; private set; }
        /// <summary>맵 오브젝트·점령 구역 (R-78). 팀은 "player"/"enemy".</summary>
        public IReadOnlyList<StepObjectDef> Objects { get; private set; } = new List<StepObjectDef>();
        /// <summary>버티기 (R-78): 이 팀("player"/"enemy")이 HoldOutSeconds까지 남으면 이긴다. 없으면 null.</summary>
        public string HoldOutTeam { get; private set; }
        /// <summary>권장 장수 레벨 (출진 화면 안내, E22). 없으면 0 = 표시 안 함.</summary>
        public int RecommendedLevel { get; private set; }
        public Fix HoldOutSeconds { get; private set; }
        public string Weather { get; private set; }
        public string TimeOfDay { get; private set; }
        public string WindDirection { get; private set; }
        public Fix WindStrength { get; private set; }
        public bool DuelAllowed { get; private set; }
        public bool DebateAllowed { get; private set; }
        /// <summary>규칙 스위치·balance·combat 덮어쓰기 (스텝 단계).</summary>
        public JObject RuleOverrides { get; private set; }
        public JObject BalanceOverrides { get; private set; }
        public JObject CombatOverrides { get; private set; }
        public IReadOnlyList<StepEnemyCorpsDef> Enemy { get; private set; }
        public IReadOnlyList<string> RewardFormations { get; private set; }
        public IReadOnlyList<string> RewardSkills { get; private set; }
        /// <summary>첫 클리어에 합류하는 장수 (rewards.joinGenerals, generals.md 1장: 뽑기에서만 나오는 장수 없음). 이미 있으면 조각 1개.</summary>
        public IReadOnlyList<string> RewardGenerals { get; private set; }
        public IReadOnlyDictionary<string, long> RewardResources { get; private set; }
        /// <summary>승리 시 출전 장수 경험치 (전투 레벨업 배율 적용 전).</summary>
        public long RewardExp { get; private set; }
        public IReadOnlyList<StepEventDef> Events { get; private set; }
        /// <summary>밸런스 목표: 위임 출진 승률 범위 (없으면 null).</summary>
        public Fix? TargetWinRateMin { get; private set; }
        public Fix? TargetWinRateMax { get; private set; }
        /// <summary>스텝 종류: story(스토리, 기본) / territory(영토 전투: 해금·클리어·첫 보상·탐색 기억 없음).</summary>
        public string Kind { get; private set; }
        public const string StoryKind = "story";
        public const string TerritoryKind = "territory";
        public bool IsTerritory => Kind == TerritoryKind;

        public static StepDef Load(IDataSource source, string id) => From(JsonUtil.ParseObject(source.ReadText("Steps/" + id + ".json")));

        public static StepDef From(JObject o)
        {
            string id = JsonUtil.RequireString(o, "id", "step");
            string c = "step." + id;
            var enemy = new List<StepEnemyCorpsDef>();
            foreach (JObject e in (JArray)o["enemy"])
            {
                var units = new List<StepUnitDef>();
                foreach (JObject u in (JArray)e["units"])
                    units.Add(new StepUnitDef
                    {
                        Slot = JsonUtil.RequireString(u, "slot", c + ".units"),
                        TroopType = JsonUtil.RequireString(u, "troopType", c + ".units"),
                        Troops = JsonUtil.RequireLong(u, "troops", c + ".units"),
                    });
                enemy.Add(new StepEnemyCorpsDef
                {
                    Id = JsonUtil.RequireString(e, "id", c + ".enemy"),
                    General = JsonUtil.RequireString(e, "general", c + ".enemy"),
                    Role = e.Value<string>("role"),
                    Formation = JsonUtil.RequireString(e, "formation", c + ".enemy"),
                    Ai = e.Value<string>("ai") ?? "aggressive",
                    Difficulty = e.Value<string>("difficulty") ?? "normal",
                    Supply = e.Value<long?>("supply") ?? 0,
                    SupplyMinutes = e.TryGetValue("supplyMinutes", out var sm) ? JsonUtil.ToFix(sm, c + ".enemy.supplyMinutes") : (Fix?)null,
                    KnownFormations = JsonUtil.StringList(e, "knownFormations"),
                    Units = units,
                    X = e.TryGetValue("x", out var x) ? JsonUtil.ToFix(x, c) : (Fix?)null,
                    Y = e.TryGetValue("y", out var y) ? JsonUtil.ToFix(y, c) : (Fix?)null,
                    Facing = e.Value<string>("facing"),
                    Reserve = e.Value<bool?>("reserve") ?? false,
                });
            }
            var rewards = o["rewards"] as JObject ?? new JObject();
            var res = new Dictionary<string, long>();
            foreach (var p in rewards.Properties())
                if (p.Value.Type == JTokenType.Integer && p.Name != "exp") res[p.Name] = p.Value.Value<long>();
            var events = new List<StepEventDef>();
            if (o["events"] is JArray evs)
                foreach (JObject ev in evs)
                {
                    var t = JsonUtil.RequireObject(ev, "trigger", c + ".events");
                    var actions = new List<StepEventActionDef>();
                    foreach (JObject a in (JArray)ev["actions"])
                        actions.Add(new StepEventActionDef
                        {
                            Type = JsonUtil.RequireString(a, "type", c + ".events"),
                            Key = a.Value<string>("key"),
                            Corps = a.Value<string>("corps"),
                            Stance = a.Value<string>("stance"),
                        });
                    events.Add(new StepEventDef
                    {
                        Id = JsonUtil.RequireString(ev, "id", c + ".events"),
                        TriggerType = JsonUtil.RequireString(t, "type", c + ".events"),
                        TriggerTick = t.Value<long?>("tick") ?? 0,
                        TriggerCorps = t.Value<string>("corps"),
                        TriggerTeam = t.Value<string>("team"),
                        TriggerRatio = JsonUtil.FixOr(t, "ratio", Fix.Zero, c + ".events"),
                        Once = ev.Value<bool?>("once") ?? true,
                        Actions = actions,
                    });
                }
            var contests = o["contests"] as JObject ?? new JObject();
            var wind = o["wind"] as JObject;
            return new StepDef
            {
                Id = id,
                Kind = o.Value<string>("kind") ?? StoryKind,
                NameKey = JsonUtil.RequireString(o, "nameKey", c),
                BattleType = o.Value<string>("battleType") ?? "field",
                Map = JsonUtil.RequireString(o, "map", c),
                MaxCorps = o.Value<int?>("maxCorps"),
                MaxMinutes = JsonUtil.FixOr(o, "maxMinutes", Fix.FromInt(10), c),
                Camp = o.Value<string>("camp"),
                RescueCamp = o.Value<string>("rescueCamp"),
                DefeatConditions = o["defeatConditions"] != null ? JsonUtil.StringList(o, "defeatConditions") : new List<string> { "annihilation", "commander" },
                Objects = StepObjectDef.List(o, "steps"),
                HoldOutTeam = (o["holdOut"] as JObject)?.Value<string>("team"),
                RecommendedLevel = o.Value<int?>("recommendedLevel") ?? 0,
                HoldOutSeconds = o["holdOut"] is JObject ho ? JsonUtil.RequireFix(ho, "seconds", "steps.holdOut") : Fix.Zero,
                Weather = o.Value<string>("weather") ?? "clear",
                TimeOfDay = o.Value<string>("timeOfDay") ?? "day",
                WindDirection = wind?.Value<string>("direction"),
                WindStrength = wind != null ? JsonUtil.FixOr(wind, "strength", Fix.Zero, c) : Fix.Zero,
                DuelAllowed = contests.Value<bool?>("duel") ?? false,
                DebateAllowed = contests.Value<bool?>("debate") ?? false,
                RuleOverrides = o["rules"] as JObject,
                BalanceOverrides = o["balance"] as JObject,
                CombatOverrides = o["combat"] as JObject,
                Enemy = enemy,
                RewardFormations = JsonUtil.StringList(rewards, "unlockFormations"),
                RewardSkills = JsonUtil.StringList(rewards, "unlockSkills"),
                RewardGenerals = JsonUtil.StringList(rewards, "joinGenerals"),
                RewardResources = res,
                RewardExp = rewards.Value<long?>("exp") ?? 0,
                Events = events,
                TargetWinRateMin = o["balanceTarget"] is JObject bt1 ? JsonUtil.RequireFix(bt1, "winRateMin", c + ".balanceTarget") : (Fix?)null,
                TargetWinRateMax = o["balanceTarget"] is JObject bt2 ? JsonUtil.RequireFix(bt2, "winRateMax", c + ".balanceTarget") : (Fix?)null,
            };
        }
    }
}
