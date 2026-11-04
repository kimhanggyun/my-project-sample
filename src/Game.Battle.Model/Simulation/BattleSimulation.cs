using System;
using System.Collections.Generic;
using Game.Battle.Model.Ai;
using Game.Battle.Model.Combat;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Damage;
using Game.Battle.Model.Field;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Skills;
using Game.Battle.Model.Systems;
using Game.Battle.Model.Vision;
using Game.Battle.Model.Victory;
using Game.Data.Defs;
using Game.Foundation.Math;
using Game.Foundation.Random;

namespace Game.Battle.Model.Simulation
{
    /// <summary>등록표 묶음. 새 명령·AI 성향·패배 조건·피해 보정은 여기에 등록해 붙인다.</summary>
    public sealed class BattleExtensions
    {
        public CommandRegistry Commands { get; set; } = CommandRegistry.WithBuiltIns();
        public AiRegistry Ai { get; set; } = AiRegistry.WithBuiltIns();
        public VictoryRegistry Victory { get; set; } = VictoryRegistry.WithBuiltIns();
        public List<IDamageModifierSource> DamageSources { get; set; } = Combat.DamageSources.BuiltIns();
        public SkillRegistry Skills { get; set; } = SkillRegistry.WithBuiltIns();

        public static BattleExtensions Default() => new BattleExtensions();
    }

    /// <summary>
    /// 전투 시뮬레이션 (고정 틱, 결정적). 화면은 <see cref="Context"/>를 읽기만 하고, 입력은 <see cref="Issue"/>로만 넣는다.
    /// 틱 순서: 명령 → AI 판단 → 스킬(다가가기·전조) → 도발 강제 → 이동 → 시야 → 은신·복병 → 전투 → 군량 → 사기
    /// → 상태 시간·책략 점수 → 궤멸·퇴각 → 승패.
    /// </summary>
    public sealed class BattleSimulation
    {
        private readonly BattleExtensions _ext;
        private readonly DamageCalculator _calculator;
        private readonly CombatSystem _combat;
        private readonly List<BattleCommand> _pending = new List<BattleCommand>();
        private readonly List<BattleCommand> _log = new List<BattleCommand>();
        private readonly long _maxTicks;

        public BattleContext Context { get; }
        public long Tick => Context.Tick;
        public bool IsOver => Result != null;
        public BattleResult Result { get; private set; }
        public IReadOnlyList<BattleCommand> CommandLog => _log;

        public BattleSimulation(BattleSetup setup, BattleExtensions extensions = null)
        {
            _ext = extensions ?? BattleExtensions.Default();
            Validate(setup, _ext);
            Context = new BattleContext
            {
                Setup = setup,
                Field = new Battlefield(setup.Map, setup.Defs, setup.Flags.IsEnabled(RuleFlagIds.TerrainRules)),
                Random = new SeededRandom(setup.Seed),
                SkillRegistry = _ext.Skills,
            };
            _calculator = new DamageCalculator(setup.Balance, setup.Flags);
            _combat = new CombatSystem(_ext.DamageSources);
            _maxTicks = (setup.MaxMinutes * setup.Combat.TicksPerMinute).FloorToLong();
            Spawn();
            VisionSystem.Init(Context);
        }

        /// <summary>명령을 넣는다. 다음 <see cref="Step"/> 처음에 적용된다. 받을 수 없는 명령이면 false (기록하지 않음).</summary>
        public bool Issue(BattleCommand command)
        {
            if (IsOver || command == null) return false;
            var corps = Context.FindCorps(command.CorpsId);
            if (corps == null || corps.Setup.Controller != Controllers.Player || corps.Status != CorpsStatus.Active) return false;
            if (corps.HasStatus(StatusIds.Taunted)) return false; // 도발당한 동안은 명령을 받지 않는다
            if (!_ext.Commands.TryGet(command.Kind, out var handler) || !handler.IsValid(Context, corps, command)) return false;
            var stamped = command.Clone();
            stamped.Tick = Context.Tick;
            _pending.Add(stamped);
            _log.Add(stamped);
            return true;
        }

        public void Step()
        {
            if (IsOver) return;
            var ctx = Context;

            foreach (var cmd in _pending)
            {
                var corps = ctx.FindCorps(cmd.CorpsId);
                if (corps.Status != CorpsStatus.Active || corps.HasStatus(StatusIds.Taunted)) continue;
                if (_ext.Commands.TryGet(cmd.Kind, out var h) && h.IsValid(ctx, corps, cmd)) h.Apply(ctx, corps, cmd);
            }
            _pending.Clear();

            if (ctx.Tick % ctx.Combat.AiThinkEveryTicks == 0)
                foreach (var corps in ctx.Corps)
                    if (corps.Status == CorpsStatus.Active && corps.Setup.Controller == Controllers.Ai && !corps.HasStatus(StatusIds.Taunted))
                        _ext.Ai.Get(corps.Setup.AiStance).Think(ctx, corps);

            SkillSystem.Step(ctx);
            StatusSystem.Enforce(ctx);
            MovementSystem.Step(ctx);
            VisionSystem.Step(ctx);
            StealthSystem.Step(ctx);
            _combat.Step(ctx, _calculator);
            SupplySystem.Step(ctx);
            MoraleSystem.Step(ctx);
            StatusSystem.Tick(ctx);
            RetreatSystem.Step(ctx);
            ctx.Tick++;
            CheckEnd();
        }

        public BattleResult RunToEnd()
        {
            while (!IsOver) Step();
            return Result;
        }

        /// <summary>
        /// 명령 기록대로 돌린다 (리플레이·서버 검증). 각 명령은 기록된 틱에 넣는다.
        /// 같은 setup + 같은 명령 기록이면 같은 결과 (R-29).
        /// </summary>
        public static BattleResult Run(BattleSetup setup, IEnumerable<BattleCommand> commands = null, BattleExtensions extensions = null)
        {
            var sim = new BattleSimulation(setup, extensions);
            var ordered = StableByTick(commands);
            int next = 0;
            while (!sim.IsOver)
            {
                while (next < ordered.Count && ordered[next].Tick <= sim.Tick) sim.Issue(ordered[next++]);
                sim.Step();
            }
            return sim.Result;
        }

        /// <summary>틱 순서로, 같은 틱이면 원래 순서를 지킨다.</summary>
        private static List<BattleCommand> StableByTick(IEnumerable<BattleCommand> commands)
        {
            var list = new List<(BattleCommand c, int i)>();
            int i = 0;
            if (commands != null) foreach (var c in commands) list.Add((c, i++));
            list.Sort((a, b) => a.c.Tick != b.c.Tick ? a.c.Tick.CompareTo(b.c.Tick) : a.i.CompareTo(b.i));
            var result = new List<BattleCommand>(list.Count);
            foreach (var x in list) result.Add(x.c);
            return result;
        }

        private void Spawn()
        {
            var ctx = Context;
            var setup = ctx.Setup;
            var perTeam = new Dictionary<string, int>();
            for (int i = 0; i < setup.Corps.Count; i++)
            {
                var cs = setup.Corps[i];
                if (!ctx.Teams.Contains(cs.Team)) ctx.Teams.Add(cs.Team);
                perTeam.TryGetValue(cs.Team, out int k);
                perTeam[cs.Team] = k + 1;

                Vec facing;
                Vec anchor;
                setup.Map.Spawns.TryGetValue(cs.Team, out var spawn);
                facing = Facing.Parse(cs.Facing ?? spawn.Facing);
                if (cs.X.HasValue && cs.Y.HasValue) anchor = new Vec(cs.X.Value, cs.Y.Value);
                else
                {
                    var center = new Cell((int)spawn.X.FloorToLong(), (int)spawn.Y.FloorToLong()).Center;
                    long lateral = k % 2 == 1 ? -(k + 1) / 2 : k / 2;
                    anchor = center + facing.RightOf() * (setup.Combat.SpawnSpacingCells * lateral);
                }

                var leadershipBonus = (cs.General.Leadership - Fix.FromInt(50)) * setup.Combat.MoraleLeadershipFactor;
                var morale = Fix.Clamp(setup.Combat.MoraleStart + leadershipBonus, Fix.Zero, Fix.Hundred);
                var corps = new CorpsState
                {
                    Index = i,
                    Setup = cs,
                    Formation = setup.Defs.Formations[cs.Formation],
                    Temperament = setup.Defs.Temperaments[cs.General.Temperament],
                    Anchor = anchor,
                    Facing = facing,
                    Status = CorpsStatus.Active,
                    Morale = morale,
                    StartMorale = morale,
                    StartSupply = cs.Supply,
                    Supply = cs.Supply,
                };
                var known = new List<SkillDef>();
                var uses = new Dictionary<string, int>();
                foreach (var id in cs.General.Skills)
                {
                    var sk = setup.Defs.Skills[id];
                    if (known.Contains(sk)) continue;
                    known.Add(sk);
                    uses[id] = sk.UsesPerBattle;
                }
                corps.KnownSkills = known;
                corps.SkillPoints = new SkillPointPool(cs.General.Intellect, uses);
                foreach (var u in cs.Units)
                    corps.Units.Add(new SubUnitState
                    {
                        UnitId = u.UnitId,
                        Slot = u.Slot,
                        Troop = setup.Defs.TroopTypes[u.TroopType],
                        StartTroops = u.Troops,
                        Troops = u.Troops,
                    });
                ctx.Corps.Add(corps);
                MovementSystem.PlaceUnits(ctx, corps);
            }
        }

        private void CheckEnd()
        {
            var ctx = Context;
            var alive = new List<string>();
            foreach (var team in ctx.Teams)
            {
                bool defeated = false;
                foreach (var id in ctx.Setup.DefeatConditions)
                    if (_ext.Victory.Get(id).IsDefeated(ctx, team)) { defeated = true; break; }
                if (!defeated) alive.Add(team);
            }
            if (alive.Count < ctx.Teams.Count)
            {
                if (alive.Count <= 1) Finish(alive.Count == 1 ? alive[0] : null, BattleEndReasons.Defeat);
                return;
            }
            if (ctx.Tick >= _maxTicks) Finish(ByRemainingRatio(), BattleEndReasons.TimeLimit);
        }

        /// <summary>시간 제한: 남은 병력 ÷ 시작 병력이 가장 큰 팀. 같으면 무승부.</summary>
        private string ByRemainingRatio()
        {
            string best = null;
            long bestRemain = 0, bestStart = 1;
            bool tie = false;
            foreach (var team in Context.Teams)
            {
                long remain = 0, start = 0;
                foreach (var c in Context.Corps)
                    if (c.Team == team) { start += c.StartTroops; if (c.InBattle) remain += c.Troops; }
                if (start == 0) continue;
                if (best == null) { best = team; bestRemain = remain; bestStart = start; continue; }
                // remain/start 와 bestRemain/bestStart 비교 (곱해서, 넘침은 decimal로 피한다)
                int cmp = ((decimal)remain * bestStart).CompareTo((decimal)bestRemain * start);
                if (cmp > 0) { best = team; bestRemain = remain; bestStart = start; tie = false; }
                else if (cmp == 0) tie = true;
            }
            return tie ? null : best;
        }

        private void Finish(string winner, string reason)
        {
            var ctx = Context;
            var corps = new List<CorpsResult>();
            foreach (var c in ctx.Corps)
            {
                var units = new List<UnitResult>();
                foreach (var u in c.Units)
                    units.Add(new UnitResult { UnitId = u.UnitId, Slot = u.Slot, TroopType = u.Troop.Id, StartTroops = u.StartTroops, RemainingTroops = u.Troops });
                corps.Add(new CorpsResult
                {
                    Id = c.Id,
                    Team = c.Team,
                    Status = c.Status,
                    StartTroops = c.StartTroops,
                    RemainingTroops = c.Troops,
                    StartSupply = c.StartSupply,
                    RemainingSupply = c.Supply,
                    FinalMorale = c.Morale,
                    SkillPointsLeft = c.SkillPoints.Points,
                    Units = units,
                });
            }
            Result = new BattleResult
            {
                Winner = winner,
                EndReason = reason,
                Ticks = ctx.Tick,
                ElapsedSeconds = ctx.Combat.TickSeconds * ctx.Tick,
                Corps = corps,
                Commands = new List<BattleCommand>(_log),
                StateHash = StateHasher.Hash(ctx),
                Explored = ExploredCopy(ctx),
            };
        }

        private static Dictionary<string, bool[]> ExploredCopy(BattleContext ctx)
        {
            var d = new Dictionary<string, bool[]>();
            foreach (var kv in ctx.Vision) d[kv.Key] = (bool[])kv.Value.ExploredCells.Clone();
            return d;
        }

        private static void Validate(BattleSetup s, BattleExtensions ext)
        {
            void Fail(string m) => throw new ArgumentException("전투 설정: " + m);
            if (s.Defs == null || s.Map == null || s.Balance == null || s.Combat == null || s.Flags == null) Fail("Defs·Map·Balance·Combat·Flags가 필요합니다.");
            var ids = new HashSet<string>();
            var teams = new HashSet<string>();
            foreach (var id in s.DefeatConditions)
                if (!ext.Victory.Contains(id)) Fail($"없는 패배 조건 '{id}'.");
            foreach (var c in s.Corps)
            {
                if (c.Id == null || !ids.Add(c.Id)) Fail($"군단 id '{c.Id}'가 비었거나 중복.");
                teams.Add(c.Team);
                if (!s.Map.Spawns.ContainsKey(c.Team) && !(c.X.HasValue && c.Y.HasValue && c.Facing != null)) Fail($"군단 '{c.Id}': 팀 '{c.Team}'의 시작 위치가 맵에 없습니다.");
                if (!s.Map.RetreatEdges.ContainsKey(c.Team)) Fail($"군단 '{c.Id}': 팀 '{c.Team}'의 퇴각 방향이 맵에 없습니다.");
                if (c.General == null) Fail($"군단 '{c.Id}': 장수가 없습니다.");
                if (!s.Defs.Temperaments.ContainsKey(c.General.Temperament ?? "")) Fail($"군단 '{c.Id}': 없는 성향 '{c.General.Temperament}'.");
                if (c.Formation == null || !s.Defs.Formations.ContainsKey(c.Formation)) Fail($"군단 '{c.Id}': 없는 진형 '{c.Formation}'.");
                if (c.Controller != Controllers.Player && c.Controller != Controllers.Ai) Fail($"군단 '{c.Id}': 조종 '{c.Controller}'는 player 또는 ai.");
                if (c.Controller == Controllers.Ai && !ext.Ai.Contains(c.AiStance)) Fail($"군단 '{c.Id}': 없는 AI 성향 '{c.AiStance}'.");
                if (c.Supply < 0) Fail($"군단 '{c.Id}': 군량이 음수.");
                foreach (var sk in c.General.Skills ?? new List<string>())
                {
                    if (!s.Defs.Skills.TryGetValue(sk, out var def)) Fail($"군단 '{c.Id}': 없는 스킬 '{sk}'.");
                    foreach (var part in def.Effects)
                        if (!ext.Skills.HasEffect(part.Type)) Fail($"스킬 '{sk}': 등록되지 않은 효과 '{part.Type}'.");
                    foreach (var part in def.Conditions)
                        if (!ext.Skills.HasCondition(part.Type)) Fail($"스킬 '{sk}': 등록되지 않은 조건 '{part.Type}'.");
                }
                var slots = new HashSet<string>();
                foreach (var u in c.Units)
                {
                    if (!SlotIds.IsValid(u.Slot) || !slots.Add(u.Slot)) Fail($"군단 '{c.Id}': 칸 '{u.Slot}'가 없거나 중복.");
                    if (!s.Defs.TroopTypes.ContainsKey(u.TroopType ?? "")) Fail($"군단 '{c.Id}': 없는 병종 '{u.TroopType}'.");
                    if (u.Troops < 0) Fail($"군단 '{c.Id}': 병력이 음수.");
                }
                if (c.Units.Count == 0) Fail($"군단 '{c.Id}': 부대가 없습니다.");
            }
            if (teams.Count < 2) Fail("팀이 둘 이상 필요합니다.");
        }
    }

    public static class BattleEndReasons
    {
        public const string Defeat = "defeat";
        public const string TimeLimit = "timeLimit";
    }

    /// <summary>상태 지문 (FNV-1a 64). 리플레이·서버 재현이 같은지 한 값으로 비교한다.</summary>
    public static class StateHasher
    {
        public static ulong Hash(BattleContext ctx)
        {
            ulong h = 14695981039346656037UL;
            void Add(long v)
            {
                unchecked
                {
                    for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (i * 8)); h *= 1099511628211UL; }
                }
            }
            void AddString(string s) { foreach (char ch in s ?? "") Add(ch); Add(-1); }

            Add(ctx.Tick);
            Add((long)ctx.Random.State);
            foreach (var c in ctx.Corps)
            {
                AddString(c.Id);
                Add((long)c.Status);
                AddString(c.Formation.Id);
                Add(c.Morale.Raw);
                Add(c.Supply);
                Add(c.SupplyAccumulator);
                Add(c.Anchor.X.Raw); Add(c.Anchor.Y.Raw);
                Add(c.Facing.X.Raw); Add(c.Facing.Y.Raw);
                Add(c.Hidden ? 1 : 0);
                Add(c.SkillPoints.Points.Raw);
                foreach (var st in c.Statuses) { AddString(st.Id); Add(st.TicksLeft); Add(st.AttackPp.Raw); }
                foreach (var u in c.Units)
                {
                    Add(u.Troops);
                    Add(u.PendingLoss.Raw);
                    Add(u.Position.X.Raw); Add(u.Position.Y.Raw);
                    Add(u.ConfusedTicks);
                }
            }
            foreach (var z in ctx.Reveals) { AddString(z.Team); Add(z.Center.X.Raw); Add(z.Center.Y.Raw); Add(z.TicksLeft); }
            foreach (var team in ctx.Teams)
                foreach (var id in ctx.Vision[team].VisibleCorps.OrderedIds(ctx)) AddString(id);
            return h;
        }
    }
}
