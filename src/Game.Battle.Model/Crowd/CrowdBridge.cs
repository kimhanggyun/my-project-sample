using System.Collections.Generic;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Battle.Model.Crowd
{
    /// <summary>수비판 자리 하나 (R-167): 수비 군단(장수 id) → 3 × 3 칸 · 자세 · 매복.</summary>
    public sealed class CrowdBoardPlace
    {
        public string GeneralId { get; set; }
        public int Row { get; set; }
        public int Col { get; set; }
        /// <summary><see cref="CrowdStances"/>.</summary>
        public string Stance { get; set; } = CrowdStances.Guard;
        public bool Ambush { get; set; }
    }

    /// <summary>수비 자세 (R-167): guard 깃발 둘레를 지킴 (AI, 기본) · hold 제자리 (안 움직이고 닿는 적만) · advance 나가 싸움 (AI, 멀리까지).</summary>
    public static class CrowdStances
    {
        public const string Guard = "guard", Hold = "hold", Advance = "advance";
        public static readonly string[] All = { Guard, Hold, Advance };
        public static bool IsKnown(string s) => s == Guard || s == Hold || s == Advance;
    }

    /// <summary>
    /// 영지 깃발전 다리 (R-163 · R-166 · R-167): 출진 준비가 만든 예전 전투 설정(군단 · 장수 · 부대)을 병사 단위 깃발전 설정으로 바꾸고,
    /// 끝난 깃발전을 예전 결과 모양(BattleResult)으로 돌려 결과 반영(손실 · 경험)을 그대로 쓴다.
    /// 기기 · 서버가 같은 코드로 같은 설정을 만든다 (서버는 명령 기록으로 다시 돌려 확인).
    /// </summary>
    public static class CrowdBridge
    {
        /// <summary>
        /// 예전 설정 → 깃발전 설정. playerTeam 군단 = 우리 편 (공격), 나머지 = 적 (수비, 깃발).
        /// 군단 하나 = 부대 하나 (R-159: 장수 하나 + 병종 하나 — 여러 부대면 첫 부대 병종, 병력은 합).
        /// 자리 = 포진판 칸 (SlotRow · SlotCol, 겹치면 빈 칸), 수비는 board가 있으면 그 칸 · 자세 · 매복.
        /// </summary>
        public static CrowdSetup FromOld(BattleSetup old, CrowdRules rules, string playerTeam, CrowdTerrain terrain,
            IReadOnlyList<CrowdBoardPlace> board = null, IReadOnlyList<CrowdTrapSpec> traps = null)
        {
            var setup = new CrowdSetup
            {
                AllyUnits = new List<CrowdUnitSpec>(), EnemyUnits = new List<CrowdUnitSpec>(),
                AllyAdvance = false, EnemyAdvance = false, Seed = old.Seed == 0 ? 1UL : old.Seed, Terrain = terrain,
                FlagBattle = true, FlagDefender = CrowdBattle.Enemy,
            };
            var used = new[] { new bool[9], new bool[9] };
            foreach (var c in old.Corps)
            {
                if (c.Reserve || c.Units == null || c.Units.Count == 0) continue;
                bool ally = c.Team == playerTeam;
                long troops = 0;
                foreach (var u in c.Units) troops += u.Troops;
                if (troops <= 0) continue;
                int count = SoldiersFor(rules, troops);
                var place = ally ? null : FindPlace(board, c.General?.Id);
                int row = place?.Row ?? c.SlotRow ?? 0, col = place?.Col ?? c.SlotCol ?? 1;
                (row, col) = FreeCell(used[ally ? 0 : 1], row, col);
                string form = "line";
                if (c.Formation != null && old.Defs?.Formations != null && old.Defs.Formations.TryGetValue(c.Formation, out var fd) && fd.Crowd != null) form = fd.Crowd;
                var spec = new CrowdUnitSpec
                {
                    Row = row, Col = col, Troop = c.Units[0].TroopType ?? "infantry", Formation = form, Count = count,
                    Commander = c.Role == "commander", Key = c.Id,
                    General = PresetOf(c.General), Might = StatOf(c.General?.Might), Intellect = StatOf(c.General?.Intellect),
                };
                if (ally) spec.Behavior = c.Delegated ? CrowdUnitSpec.Behaviors.Delegate : CrowdUnitSpec.Behaviors.Hold;
                else
                {
                    string stance = place != null && CrowdStances.IsKnown(place.Stance) ? place.Stance : CrowdStances.Guard;
                    spec.Behavior = stance == CrowdStances.Hold ? CrowdUnitSpec.Behaviors.Hold : CrowdUnitSpec.Behaviors.Delegate;
                    spec.Ambush = place != null && place.Ambush;
                }
                (ally ? setup.AllyUnits : setup.EnemyUnits).Add(spec);
            }
            // 총대장이 꼭 하나
            foreach (var list in new[] { setup.AllyUnits, setup.EnemyUnits })
            {
                bool any = false;
                foreach (var u in list) { if (u.Commander && any) u.Commander = false; any |= u.Commander; }
                if (!any && list.Count > 0) list[0].Commander = true;
            }
            if (traps != null) setup.Traps = new List<CrowdTrapSpec>(traps);
            return setup;
        }

        /// <summary>병력 → 병사 수 (올림, 1 ~ gridMaxCount).</summary>
        public static int SoldiersFor(CrowdRules rules, long troops)
        {
            long per = System.Math.Max(1, rules.TroopsPerSoldier);
            long n = (troops + per - 1) / per;
            return (int)System.Math.Max(1, System.Math.Min(rules.GridMaxCount, n));
        }

        private static CrowdBoardPlace FindPlace(IReadOnlyList<CrowdBoardPlace> board, string generalId)
        {
            if (board == null || generalId == null) return null;
            foreach (var p in board) if (p.GeneralId == generalId) return p;
            return null;
        }

        private static (int, int) FreeCell(bool[] used, int row, int col)
        {
            row = System.Math.Max(0, System.Math.Min(2, row)); col = System.Math.Max(0, System.Math.Min(2, col));
            if (!used[row * 3 + col]) { used[row * 3 + col] = true; return (row, col); }
            for (int i = 0; i < 9; i++) if (!used[i]) { used[i] = true; return (i / 3, i % 3); }
            return (row, col);
        }

        private static int? StatOf(Fix? v) => v.HasValue ? (int?)System.Math.Max(1, (int)v.Value.RoundToLong()) : null;

        /// <summary>장수 자리 성향: 무력이 지력보다 15 넘게 높으면 맹장 (선봉), 지력이 15 넘게 높으면 책사 (뒤), 아니면 장수.</summary>
        public static string PresetOf(GeneralSnapshot g)
        {
            if (g == null) return "standard";
            var d = g.Might - g.Intellect;
            if (d > Fix.FromInt(15)) return "warrior";
            if (d < Fix.FromInt(-15)) return "strategist";
            return "standard";
        }

        /// <summary>
        /// 끝난 깃발전 → 예전 결과 (결과 반영용): 이긴 편, 군단마다 남은 병력 = 처음 병력 × 남은 병사 ÷ 처음 병사 (부대별로 나눔),
        /// 무너짐 = 퇴각, 전멸 = 궤멸, 장수가 쓰러지면 중상 · 빠져나가면 경상.
        /// </summary>
        public static BattleResult ToResult(CrowdBattle b, BattleSetup old, string playerTeam, string enemyTeam, IReadOnlyList<BattleCommand> commands = null)
        {
            var corps = new List<CorpsResult>();
            var injuries = new Dictionary<string, string>();
            foreach (var c in old.Corps)
            {
                CrowdArmy army = null;
                foreach (var a in b.Armies) if (a.Key == c.Id) { army = a; break; }
                long start = 0;
                foreach (var u in c.Units) start += u.Troops;
                long left = start;
                var status = c.Reserve ? CorpsStatus.Reserve : CorpsStatus.Active;
                if (army != null)
                {
                    int alive = 0;
                    foreach (var s in army.Soldiers) if (!s.IsGeneral && s.Alive) alive++;
                    left = army.StartCount <= 0 ? start : start * alive / army.StartCount;
                    if (alive == 0) status = CorpsStatus.Destroyed;
                    else if (army.Routed) status = CorpsStatus.Retreated;
                    if (c.General != null)
                    {
                        if (army.General.DiedTick >= 0) injuries[c.General.Id] = "heavy";
                        else if (army.General.FledTick >= 0) injuries[c.General.Id] = "light";
                    }
                }
                var units = new List<UnitResult>();
                long given = 0;
                for (int i = 0; i < c.Units.Count; i++)
                {
                    var u = c.Units[i];
                    long rem = i == c.Units.Count - 1 ? left - given : (start == 0 ? 0 : left * u.Troops / start);
                    rem = System.Math.Max(0, System.Math.Min(u.Troops, rem));
                    given += rem;
                    units.Add(new UnitResult { UnitId = u.UnitId, Slot = u.Slot, TroopType = u.TroopType, StartTroops = u.Troops, RemainingTroops = rem });
                }
                long remaining = 0;
                foreach (var ur in units) remaining += ur.RemainingTroops;
                corps.Add(new CorpsResult
                {
                    Id = c.Id, Team = c.Team, Status = status, StartTroops = start, RemainingTroops = remaining,
                    StartSupply = c.Supply, RemainingSupply = c.Supply, FinalMorale = army?.Morale ?? Fix.Zero, SkillPointsLeft = Fix.Zero, Units = units,
                });
            }
            string winner = b.Winner == CrowdBattle.Ally ? playerTeam : b.Winner == CrowdBattle.Enemy ? enemyTeam : null;
            return new BattleResult
            {
                Winner = winner,
                EndReason = b.EndReason,
                Ticks = b.Tick,
                ElapsedSeconds = b.Rules.TickSeconds * b.Tick,
                Corps = corps,
                Commands = commands ?? new List<BattleCommand>(),
                StateHash = StateHash(b),
                Explored = new Dictionary<string, bool[]>(),
                Contests = new List<Contests.ContestResult>(),
                GeneralInjuries = injuries,
                Captures = new List<CaptureRecord>(),
                Events = new List<Events.BattleEvent>(),
            };
        }

        /// <summary>깃발전 상태 지문 (FNV-1a 64): 틱 · 이긴 편 · 깃발 · 병사 자리 · 체력 · 부대 사기 (기기 · 서버 비교, R-166).</summary>
        public static ulong StateHash(CrowdBattle b)
        {
            ulong h = 1469598103934665603UL;
            void Mix(long v) { unchecked { for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (i * 8)); h *= 1099511628211UL; } } }
            Mix(b.Tick); Mix(b.Winner); Mix(b.FlagProgress.Raw);
            foreach (var s in b.Soldiers) { Mix(s.Pos.X.Raw); Mix(s.Pos.Y.Raw); Mix(s.Hp); Mix(s.DiedTick); Mix(s.FledTick); }
            foreach (var a in b.Armies) { Mix(a.Morale.Raw); Mix(a.Routed ? 1 : 0); }
            return h;
        }
    }
}
