using System.Collections.Generic;
using Game.Data.Json;
using Game.Foundation.Math;
using Newtonsoft.Json.Linq;

namespace Game.Data.Defs
{
    // 전투 시뮬레이션용 정의 데이터 (Rules/combat.json, terrainRules.json, troopCounters.json, temperaments.json, Maps/*.json).
    // 모두 읽기 전용.

    /// <summary>진형 칸 위치 (장수 기준, 칸 단위. x = 오른쪽, y = 앞쪽).</summary>
    public readonly struct SlotOffset
    {
        public readonly Fix X;
        public readonly Fix Y;
        public SlotOffset(Fix x, Fix y) { X = x; Y = y; }
    }

    /// <summary>전투 수치 (combat.json).</summary>
    public sealed class CombatRules
    {
        public Fix TickSeconds { get; private set; }
        public Fix BaseDamagePerTroopPerSecond { get; private set; }
        public Fix StatPpPerPoint { get; private set; }
        public Fix DamageVariancePp { get; private set; }
        public Fix MoveCellsPerSecondAtSpeed50 { get; private set; }
        public Fix ContactRadius { get; private set; }
        public Fix PerceptionCells { get; private set; }
        public Fix FlankPp { get; private set; }
        public Fix RearPp { get; private set; }
        public Fix FrontDot { get; private set; }
        public Fix RearDot { get; private set; }
        public Fix HeightPpPerLevel { get; private set; }
        public Fix FormationCorePp { get; private set; }
        public Fix FormationSituationalPp { get; private set; }
        public Fix MoraleStart { get; private set; }
        public Fix MoraleLeadershipFactor { get; private set; }
        public Fix MoraleLossPerPercentTroops { get; private set; }
        public Fix MoraleFlankHitPerSecond { get; private set; }
        public Fix MoraleRecoverPerSecond { get; private set; }
        public Fix MoraleSupplyExhaustedPerSecond { get; private set; }
        public Fix RetreatTroopRatio { get; private set; }
        public Fix RetreatEdgeMargin { get; private set; }
        public int AiThinkEveryTicks { get; private set; }
        public Fix GeneralMightToAttack { get; private set; }
        public Fix GeneralLeadershipToDefense { get; private set; }
        /// <summary>같은 팀 군단끼리 시작 위치 옆 간격 (칸).</summary>
        public Fix SpawnSpacingCells { get; private set; }

        /// <summary>1분에 몇 틱인지. 60 ÷ 틱 길이가 정수여야 한다.</summary>
        public long TicksPerMinute => (Fix.FromInt(60) / TickSeconds).FloorToLong();

        public static CombatRules From(JObject o)
        {
            const string c = "combat";
            var dir = JsonUtil.RequireObject(o, "direction", c);
            var height = JsonUtil.RequireObject(o, "height", c);
            var fc = JsonUtil.RequireObject(o, "formationCounter", c);
            var morale = JsonUtil.RequireObject(o, "morale", c);
            var retreat = JsonUtil.RequireObject(o, "retreat", c);
            var general = JsonUtil.RequireObject(o, "general", c);
            var r = new CombatRules
            {
                TickSeconds = JsonUtil.RequireFix(o, "tickSeconds", c),
                BaseDamagePerTroopPerSecond = JsonUtil.RequireFix(o, "baseDamagePerTroopPerSecond", c),
                StatPpPerPoint = JsonUtil.RequireFix(o, "statPpPerPoint", c),
                DamageVariancePp = JsonUtil.RequireFix(o, "damageVariancePp", c),
                MoveCellsPerSecondAtSpeed50 = JsonUtil.RequireFix(o, "moveCellsPerSecondAtSpeed50", c),
                ContactRadius = JsonUtil.RequireFix(o, "contactRadius", c),
                PerceptionCells = JsonUtil.RequireFix(o, "perceptionCells", c),
                FlankPp = JsonUtil.RequireFix(dir, "flankPp", c + ".direction"),
                RearPp = JsonUtil.RequireFix(dir, "rearPp", c + ".direction"),
                FrontDot = JsonUtil.RequireFix(dir, "frontDot", c + ".direction"),
                RearDot = JsonUtil.RequireFix(dir, "rearDot", c + ".direction"),
                HeightPpPerLevel = JsonUtil.RequireFix(height, "ppPerLevel", c + ".height"),
                FormationCorePp = JsonUtil.RequireFix(fc, "corePp", c + ".formationCounter"),
                FormationSituationalPp = JsonUtil.RequireFix(fc, "situationalPp", c + ".formationCounter"),
                MoraleStart = JsonUtil.RequireFix(morale, "start", c + ".morale"),
                MoraleLeadershipFactor = JsonUtil.RequireFix(morale, "leadershipFactor", c + ".morale"),
                MoraleLossPerPercentTroops = JsonUtil.RequireFix(morale, "lossPerPercentTroops", c + ".morale"),
                MoraleFlankHitPerSecond = JsonUtil.RequireFix(morale, "flankHitPerSecond", c + ".morale"),
                MoraleRecoverPerSecond = JsonUtil.RequireFix(morale, "recoverPerSecond", c + ".morale"),
                MoraleSupplyExhaustedPerSecond = JsonUtil.RequireFix(morale, "supplyExhaustedPerSecond", c + ".morale"),
                RetreatTroopRatio = JsonUtil.RequireFix(retreat, "troopRatio", c + ".retreat"),
                RetreatEdgeMargin = JsonUtil.RequireFix(o, "retreatEdgeMargin", c),
                AiThinkEveryTicks = (int)JsonUtil.RequireLong(o, "aiThinkEveryTicks", c),
                GeneralMightToAttack = JsonUtil.RequireFix(general, "mightToAttack", c + ".general"),
                GeneralLeadershipToDefense = JsonUtil.RequireFix(general, "leadershipToDefense", c + ".general"),
                SpawnSpacingCells = JsonUtil.RequireFix(o, "spawnSpacingCells", c),
            };
            if (r.TickSeconds <= Fix.Zero || (Fix.FromInt(60) / r.TickSeconds) * r.TickSeconds != Fix.FromInt(60))
                throw new DataException("combat.tickSeconds: 60초를 나누어떨어지게 하는 양수여야 합니다.");
            if (r.AiThinkEveryTicks < 1) throw new DataException("combat.aiThinkEveryTicks: 1 이상이어야 합니다.");
            return r;
        }
    }

    /// <summary>지형 이동 규칙 하나 (terrainRules.json).</summary>
    public sealed class TerrainRuleDef
    {
        public const string AllTags = "*";

        public string Id { get; internal set; }
        public Fix Cost { get; internal set; }
        /// <summary>태그별 이동 비용. 여러 태그가 맞으면 가장 큰 값.</summary>
        public IReadOnlyDictionary<string, Fix> CostByTag { get; internal set; }
        /// <summary>진입 불가 태그. "*"는 모든 병종.</summary>
        public IReadOnlyList<string> Impassable { get; internal set; }
        public int Height { get; internal set; }

        public bool BlocksEveryone
        {
            get
            {
                foreach (var t in Impassable) if (t == AllTags) return true;
                return false;
            }
        }

        public bool Blocks(TroopTypeDef troop)
        {
            foreach (var t in Impassable)
                if (t == AllTags || troop.HasTag(t)) return true;
            return false;
        }

        public Fix CostFor(TroopTypeDef troop)
        {
            bool found = false;
            Fix best = Fix.Zero;
            foreach (var kv in CostByTag)
                if (troop.HasTag(kv.Key) && (!found || kv.Value > best)) { best = kv.Value; found = true; }
            return found ? best : Cost;
        }
    }

    /// <summary>병종 상성 하나 (troopCounters.json): 공격 태그 → 방어 태그 %p.</summary>
    public sealed class TroopCounterRule
    {
        public string AttackerTag { get; internal set; }
        public string DefenderTag { get; internal set; }
        public Fix Pp { get; internal set; }
    }

    /// <summary>성향 (temperaments.json).</summary>
    public sealed class TemperamentDef
    {
        public string Id { get; internal set; }
        /// <summary>사기가 이 값 이하가 되면 퇴각한다.</summary>
        public Fix RetreatMorale { get; internal set; }
    }

    public sealed class SpawnDef
    {
        public Fix X { get; internal set; }
        public Fix Y { get; internal set; }
        /// <summary>"N", "S", "E", "W", "NE" …</summary>
        public string Facing { get; internal set; }
    }

    /// <summary>전장 맵 (Maps/&lt;id&gt;.json). 칸 (x, y): x는 오른쪽, y는 아래쪽(행 번호).</summary>
    public sealed class MapDef
    {
        public static readonly IReadOnlyList<string> Edges = new[] { "north", "south", "east", "west" };

        public string Id { get; internal set; }
        public string BattleType { get; internal set; }
        public int Width { get; internal set; }
        public int Height { get; internal set; }
        private string[] _cells;
        public IReadOnlyDictionary<string, SpawnDef> Spawns { get; internal set; }
        public IReadOnlyDictionary<string, string> RetreatEdges { get; internal set; }

        public string TerrainAt(int x, int y) => _cells[y * Width + x];
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public IEnumerable<string> TerrainIds
        {
            get
            {
                var seen = new HashSet<string>();
                foreach (var c in _cells) if (seen.Add(c)) yield return c;
            }
        }

        public static MapDef Load(IDataSource source, string mapId) =>
            From(JsonUtil.ParseObject(source.ReadText("Maps/" + mapId + ".json")));

        public static MapDef From(JObject doc)
        {
            string id = JsonUtil.RequireString(doc, "id", "map");
            string c = "map." + id;
            var legend = new Dictionary<char, string>();
            foreach (var p in JsonUtil.RequireObject(doc, "legend", c).Properties())
            {
                if (p.Name.Length != 1) throw new DataException($"{c}.legend: 기호는 한 글자여야 합니다 ('{p.Name}').");
                legend[p.Name[0]] = p.Value.Value<string>();
            }
            var rows = JsonUtil.StringList(doc, "rows");
            if (rows.Count == 0) throw new DataException($"{c}.rows: 행이 없습니다.");
            int w = rows[0].Length;
            var cells = new string[w * rows.Count];
            for (int y = 0; y < rows.Count; y++)
            {
                if (rows[y].Length != w) throw new DataException($"{c}.rows[{y}]: 폭이 {w}가 아닙니다.");
                for (int x = 0; x < w; x++)
                {
                    if (!legend.TryGetValue(rows[y][x], out var terrain))
                        throw new DataException($"{c}.rows[{y}][{x}]: 범례에 없는 기호 '{rows[y][x]}'.");
                    cells[y * w + x] = terrain;
                }
            }
            var spawns = new Dictionary<string, SpawnDef>();
            foreach (var p in JsonUtil.RequireObject(doc, "spawns", c).Properties())
            {
                var s = (JObject)p.Value;
                spawns[p.Name] = new SpawnDef
                {
                    X = JsonUtil.RequireFix(s, "x", c + ".spawns." + p.Name),
                    Y = JsonUtil.RequireFix(s, "y", c + ".spawns." + p.Name),
                    Facing = JsonUtil.RequireString(s, "facing", c + ".spawns." + p.Name),
                };
            }
            var edges = new Dictionary<string, string>();
            foreach (var p in JsonUtil.RequireObject(doc, "retreatEdges", c).Properties())
                edges[p.Name] = p.Value.Value<string>();
            return new MapDef
            {
                Id = id,
                BattleType = doc.Value<string>("battleType") ?? "field",
                Width = w,
                Height = rows.Count,
                _cells = cells,
                Spawns = spawns,
                RetreatEdges = edges,
            };
        }
    }
}
