using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Foundation.Math;

namespace Game.Battle.Model.Crowd
{
    /// <summary>
    /// 명령 기록 하나 (R-166): 바깥(사람 · 화면)에서 내린 명령을 그 틱과 함께 적는다. 같은 설정 + 같은 기록이면 같은 결과 (서버 재현).
    /// 전투 안 AI 명령 · 설정 시작 명령은 적지 않는다 (재현 때 다시 생기므로).
    /// </summary>
    public sealed class CrowdCommand
    {
        public int Tick;
        public string Kind;
        public int Army;
        public int Target = -1;
        public bool Flag, Flag2;
        public string Text;
        public Fix X, Y;
        public List<Vec> Points;
    }

    /// <summary>함정 하나 (R-165): 수비가 전투 전에 깐다. pit 구덩이 = 처음 밟은 적 둘레에 피해 · 혼란 한 번, slow 마름쇠 = 그 안 적은 느려짐. 적에게는 밟기 전까지 안 보임.</summary>
    public sealed class CrowdTrap
    {
        public int Team;
        public string Kind = "pit";
        public Vec Pos;
        public bool Used;
        /// <summary>적이 한 번 밟아 드러남 (화면이 적에게도 그림).</summary>
        public bool Revealed;
    }

    /// <summary>전투 전 함정 자리 (CrowdSetup.Traps): 편 · 종류 · 전장 좌표.</summary>
    public sealed class CrowdTrapSpec
    {
        public int Team = CrowdBattle.Enemy;
        public string Kind = "pit";
        public Fix X, Y;
    }

    public static class CrowdTrapKinds
    {
        public const string Pit = "pit", Slow = "slow";
    }

    /// <summary>
    /// 깃발전 · 지형 위험 · 명령 기록 (2026-11-28 사용자 결정, battle-crowd.md 4장 "깃발전"):
    /// - R-161 공포 확산: 흔들려 이탈하는 병사 · 퇴각하는 부대가 같은 편 사기를 깎는다 — 자기 부대뿐 아니라 진형이 겹친(가까운) 아군 부대도. 장수가 살아 있으면 덜, 사기 충만이면 없음.
    /// - R-162 총대장: 쓰러지거나 빠져나가면 그 편 모든 부대 사기 commanderFallMorale (한 번). 승패 조건은 아님 (commanderDefeats false).
    /// - R-163 깃발전: 수비 총대장 시작 자리의 진지 깃발. 원 안 공격 병사가 flagMinAttackers 이상이고 수비보다 많으면 게이지가 차고 (차이가 클수록 빨리),
    ///   공격 병사가 없으면 줄어듦. 100이면 공격 승리. 시간이 다 되면 수비 승리.
    /// - R-164 화계 · 낙석: 화계 = 숲 · 풀 칸에 불 → 이웃 숲 · 풀 칸으로 번짐 (시드 난수), 그 칸 병사 피해 · 사기 · 혼란. 낙석 = 높은 곳에서 낮은 곳으로, 그 둘레 병사 피해 · 사기.
    /// - R-165 매복 · 함정: 숲 · 풀에 숨은 부대는 적이 가까이 오거나 먼저 칠 때까지 보이지 않음 (먼저 치면 둘레 적 사기 · 혼란). 함정은 위.
    /// - R-166 명령 기록 · 재현.
    /// </summary>
    public sealed partial class CrowdBattle
    {
        public const string FireAttack = "fireAttack", Rockfall = "rockfall";

        // ---------------- 명령 기록 (R-166) ----------------

        /// <summary>바깥에서 내린 명령 (틱 순서).</summary>
        public readonly List<CrowdCommand> Commands = new List<CrowdCommand>();
        private bool _recording;
        private bool _stepping;
        private int _cmdDepth;

        /// <summary>기록할 때 (바깥 호출 · 첫 겹 · 전투 진행 중이 아님) 적는다.</summary>
        private void Rec(string kind, int army, int target = -1, bool flag = false, bool flag2 = false, string text = null, Vec? at = null, IReadOnlyList<Vec> points = null)
        {
            if (!_recording || _stepping || _cmdDepth > 0) return;
            var c = new CrowdCommand { Tick = Tick, Kind = kind, Army = army, Target = target, Flag = flag, Flag2 = flag2, Text = text };
            if (at.HasValue) { c.X = at.Value.X; c.Y = at.Value.Y; }
            if (points != null) c.Points = new List<Vec>(points);
            Commands.Add(c);
        }

        /// <summary>
        /// 재현 (R-166): 같은 설정으로 새 전투를 만들고, 기록의 틱마다 같은 명령을 내리며 끝까지 (또는 maxTicks) 돌린다.
        /// skills가 있으면 스킬 데이터도 같이.
        /// </summary>
        public static CrowdBattle Replay(CrowdRules rules, CrowdSetup setup, IReadOnlyDictionary<string, Game.Data.Defs.TroopTypeDef> troops, IReadOnlyList<Game.Data.Defs.TroopCounterRule> counters,
            IReadOnlyDictionary<string, Game.Data.Defs.SkillDef> skills, IReadOnlyList<CrowdCommand> commands, int maxTicks = int.MaxValue)
        {
            var b = new CrowdBattle(rules, setup, troops, counters);
            if (skills != null) b.UseSkillData(skills);
            int k = 0;
            var list = commands ?? new List<CrowdCommand>();
            while (!b.IsOver && b.Tick < maxTicks)
            {
                while (k < list.Count && list[k].Tick <= b.Tick) b.Apply(list[k++]);
                b.Step();
            }
            return b;
        }

        /// <summary>기록 하나를 그대로 내린다 (재현 · 녹화 보기).</summary>
        public void Apply(CrowdCommand c)
        {
            var at = new Vec(c.X, c.Y);
            switch (c.Kind)
            {
                case "hold": OrderHold(c.Army); break;
                case "move": OrderMove(c.Army, at, c.Flag, c.Flag2); break;
                case "path": OrderPath(c.Army, c.Points ?? new List<Vec>(), c.Target); break;
                case "advance": OrderAdvance(c.Army); break;
                case "retreat": OrderRetreat(c.Army); break;
                case "attack": OrderAttack(c.Army, c.Target); break;
                case "attackNearest": OrderAttackNearest(c.Army); break;
                case "delegate": SetDelegated(c.Army, c.Flag); break;
                case "autoMarch": SetAutoMarch(c.Army, c.Flag); break;
                case "formation": SetFormation(c.Army, c.Text); break;
                case "siege": OrderSiege(c.Army, c.Text); break;
                case "skill": UseSkill(c.Army, c.Text, c.Flag ? at : (Vec?)null); break;
                case "clearWaypoints": ClearWaypoints(c.Army); break;
            }
        }

        /// <summary>예약한 경유 지점을 지운다 (지금 가는 곳은 그대로, 예약 이동 [취소]).</summary>
        public void ClearWaypoints(int army)
        {
            if (army < 0 || army >= Armies.Count) return;
            Rec("clearWaypoints", army);
            Armies[army].Waypoints.Clear();
        }

        // 바깥 명령 = 기록 + 실제 처리 (안에서 다른 명령을 부르면 그건 기록하지 않음)
        private T Cmd<T>(System.Func<T> f) { _cmdDepth++; try { return f(); } finally { _cmdDepth--; } }
        private void Cmd(System.Action f) { _cmdDepth++; try { f(); } finally { _cmdDepth--; } }

        public void SetAutoMarch(int army, bool on) { Rec("autoMarch", army, flag: on); Cmd(() => SetAutoMarchCore(army, on)); }
        public void SetFormation(int army, string formation) { Rec("formation", army, text: formation); Cmd(() => SetFormationCore(army, formation)); }
        public void OrderHold(int army) { Rec("hold", army); Cmd(() => OrderHoldCore(army)); }
        public void OrderMove(int army, Vec point, bool append = false, bool march = false) { Rec("move", army, flag: append, flag2: march, at: point); Cmd(() => OrderMoveCore(army, point, append, march)); }
        public void OrderPath(int army, IReadOnlyList<Vec> points, int thenAttack = -1) { Rec("path", army, target: thenAttack, points: points); Cmd(() => OrderPathCore(army, points, thenAttack)); }
        public void OrderAdvance(int army) { Rec("advance", army); Cmd(() => OrderAdvanceCore(army)); }
        public void OrderRetreat(int army) { Rec("retreat", army); Cmd(() => OrderRetreatCore(army)); }
        public bool OrderAttackNearest(int army) { Rec("attackNearest", army); return Cmd(() => OrderAttackNearestCore(army)); }
        public void SetDelegated(int army, bool on) { Rec("delegate", army, flag: on); Cmd(() => SetDelegatedCore(army, on)); }
        public void OrderAttack(int army, int target) { Rec("attack", army, target: target); Cmd(() => OrderAttackCore(army, target)); }
        public void OrderSiege(int army, string task) { Rec("siege", army, text: task); Cmd(() => OrderSiegeCore(army, task)); }
        public bool UseSkill(int army, string skill, Vec? point = null) { Rec("skill", army, flag: point.HasValue, text: skill, at: point); return Cmd(() => UseSkillCore(army, skill, point)); }

        // ---------------- 깃발전 (R-163) ----------------

        /// <summary>깃발전이면 진지 깃발 자리 (수비 총대장 시작 자리), 아니면 null.</summary>
        public Vec? FlagPos { get; private set; }
        /// <summary>깃발을 지키는 편 (깃발전이 아니면 -1).</summary>
        public int FlagDefender { get; private set; } = -1;
        /// <summary>점령 게이지 0 ~ 100.</summary>
        public Fix FlagProgress { get; private set; }
        /// <summary>이번 틱 원 안 병사 수 (화면).</summary>
        public int FlagAttackersIn { get; private set; }
        public int FlagDefendersIn { get; private set; }

        private void SetupField(CrowdSetup setup)
        {
            if (setup.FlagBattle)
            {
                FlagDefender = setup.FlagDefender;
                var cmd = Commander(FlagDefender);
                FlagPos = cmd.General != null ? cmd.General.Pos : cmd.Center;
            }
            if (setup.Traps != null)
                foreach (var t in setup.Traps) Traps.Add(new CrowdTrap { Team = t.Team, Kind = t.Kind, Pos = new Vec(t.X, t.Y) });
            // 매복: 숨는 땅(숲 · 풀)에 놓인 매복 부대만 숨는다
            foreach (var a in Armies) a.Hidden = a.Ambush && InCover(a);
            _home = new Vec[Armies.Count];
            foreach (var a in Armies) _home[a.Id] = a.Center;
            _recording = true;
        }

        /// <summary>부대 시작 자리 (깃발전 수비 AI가 돌아올 곳).</summary>
        private Vec[] _home;

        /// <summary>
        /// 깃발전 AI (R-163): 공격 부대는 flagAiEngageRange 안 적만 치고 아니면 깃발로 간다.
        /// 수비 부대는 깃발에서 flagGuardRange 안에 들어온 적이나 바로 곁 적만 치고, 멀리 쫓아갔으면 제자리로 돌아온다 (깃발을 비우고 쫓지 않게).
        /// 깃발전이 아니면 false (예전 AI 그대로).
        /// </summary>
        private bool AiFlag(CrowdArmy a)
        {
            if (FlagDefender < 0 || !FlagPos.HasValue) return false;
            var r = Rules;
            var flag = FlagPos.Value;
            var me = a.Centroid();
            int best = NearestEnemyArmy(a.Id);
            Fix dBest = best >= 0 ? (Armies[best].Centroid() - me).Length : Fix.FromInt(9999);
            bool attackingBest = a.Order == CrowdOrders.Attack && a.OrderTarget == best;
            if (a.Team == FlagDefender)
            {
                bool threat = best >= 0 && (dBest <= r.FlagAiEngageRange || (Armies[best].Centroid() - flag).Length <= r.FlagGuardRange);
                if (threat) { if (!attackingBest) OrderAttack(a.Id, best); return true; }
                var home = _home != null && a.Id < _home.Length ? _home[a.Id] : a.Center;
                if (a.Order == CrowdOrders.Attack || (a.Order == CrowdOrders.Move && (a.OrderPoint - home).Length > Fix.OneValue))
                    if ((a.Center - home).Length > Fix.FromInt(2)) OrderMove(a.Id, home);
                return true;
            }
            // 공격: 가까운 적은 치고, 이미 치고 있는 대상이 가까우면 그대로
            if (a.Order == CrowdOrders.Attack && a.OrderTarget >= 0 && Standing(Armies[a.OrderTarget])
                && (Armies[a.OrderTarget].Centroid() - me).Length <= r.FlagAiEngageRange * Fix.FromRatio(3, 2)) return true;
            if (best >= 0 && dBest <= r.FlagAiEngageRange) { OrderAttack(a.Id, best); return true; }
            if ((me - flag).Length > r.FlagRadius / 2 && !(a.Order == CrowdOrders.Move && (a.OrderPoint - flag).Length <= Fix.OneValue))
                OrderMove(a.Id, flag);
            return true;
        }

        private int MaxTicksNow => FlagDefender >= 0 ? (int)(Rules.FlagMaxSeconds / Rules.TickSeconds).FloorToLong() : Rules.MaxTicks;

        private void UpdateFlag()
        {
            if (FlagDefender < 0 || !FlagPos.HasValue) return;
            var r = Rules;
            Fix r2 = r.FlagRadius * r.FlagRadius;
            int atk = 0, def = 0;
            foreach (var s in Soldiers)
            {
                if (!s.Alive || s.Deserting || Armies[s.Army].Routed) continue;
                if (Vec.DistanceSquared(s.Pos, FlagPos.Value) > r2) continue;
                if (s.Team == FlagDefender) def++; else atk++;
            }
            FlagAttackersIn = atk; FlagDefendersIn = def;
            Fix dt = r.TickSeconds;
            if (atk >= r.FlagMinAttackers && atk > def)
            {
                // 차이 10명마다 flagFillPerTen씩 빨라짐 (최대 flagFillMax)
                Fix rate = Fix.Min(r.FlagFillMax, r.FlagFillPerSec + r.FlagFillPerTen * (atk - def) / 10);
                FlagProgress = Fix.Min(Fix.FromInt(100), FlagProgress + rate * dt);
            }
            else if (atk == 0) FlagProgress = Fix.Max(Fix.Zero, FlagProgress - r.FlagDecayPerSec * dt);
        }

        // ---------------- 공포 확산 · 총대장 (R-161 · R-162) ----------------

        private readonly bool[] _commanderFell = new bool[2];
        /// <summary>그 편 총대장이 쓰러졌나 (사기 꺾임을 이미 받음, R-162).</summary>
        public bool CommanderFell(int team) => _commanderFell[team];

        /// <summary>그 부대의 겁먹은 정도 0 ~ 1: 퇴각이면 1, 아니면 이탈자 ÷ 남은 병사.</summary>
        private Fix PanicOf(CrowdArmy b)
        {
            if (b.Routed) return Fix.OneValue;
            int alive = 0, desert = 0;
            foreach (var s in b.Soldiers) { if (!s.Alive || s.IsGeneral) continue; alive++; if (s.Deserting) desert++; }
            return alive == 0 ? Fix.Zero : Fix.FromInt(desert) / alive;
        }

        private void SpreadPanic()
        {
            var r = Rules;
            if (r.PanicSpread <= Fix.Zero) return;
            Fix dt = r.TickSeconds;
            var panic = new Fix[Armies.Count];
            var size = new Fix[Armies.Count];
            foreach (var b in Armies)
            {
                panic[b.Id] = Standing(b) ? PanicOf(b) : Fix.Zero;
                size[b.Id] = Fix.Max(b.HalfWidth, b.HalfDepth);
            }
            foreach (var a in Armies)
            {
                if (a.Routed || !Standing(a) || Tick < a.InspiredUntil) continue; // 사기 충만은 안 번짐
                Fix sum = Fix.Zero;
                foreach (var b in Armies)
                {
                    if (b.Team != a.Team || panic[b.Id] <= Fix.Zero) continue;
                    if (b == a) { sum += panic[b.Id]; continue; }
                    // 진형이 겹칠수록 (가운데 사이가 가까울수록) 많이, panicRange 밖이면 없음
                    Fix reach = size[a.Id] + size[b.Id] + r.PanicRange;
                    Fix d = (a.TickCentroid - b.TickCentroid).Length;
                    if (d >= reach) continue;
                    sum += panic[b.Id] * (reach - d) / reach;
                }
                if (sum <= Fix.Zero) continue;
                Fix loss = r.PanicSpread * sum * dt;
                if (a.General.Alive) loss = loss * (100 - r.PanicCommandDampPct) / 100; // 장수가 붙잡아 덜 흔들림
                LoseMorale(a, loss);
                if (a.Morale < Fix.Zero) a.Morale = Fix.Zero;
                a.Wavering = a.Morale <= r.WaverAt;
            }
        }

        /// <summary>총대장이 쓰러지거나 빠져나가면 그 편 모든 부대 사기를 한 번 꺾는다 (R-162).</summary>
        private void CheckCommanderFall()
        {
            for (int t = 0; t < 2; t++)
            {
                if (_commanderFell[t]) continue;
                var g = Commander(t).General;
                if (g.Alive) continue;
                _commanderFell[t] = true;
                foreach (var a in Armies)
                    if (a.Team == t && !a.Routed && Standing(a))
                    {
                        LoseMorale(a, Rules.CommanderFallMorale);
                        if (a.Morale < Fix.Zero) a.Morale = Fix.Zero;
                        a.Wavering = a.Morale <= Rules.WaverAt;
                    }
            }
        }

        // ---------------- 매복 · 함정 (R-165) ----------------

        public readonly List<CrowdTrap> Traps = new List<CrowdTrap>();

        private bool CoverAt(Vec p) => Terrain != null && Rules.CoverTerrains.Contains(Terrain.TerrainAt(p));

        private bool InCover(CrowdArmy a) => CoverAt(a.Center) || CoverAt(a.Centroid());

        /// <summary>적 편에게 그 부대가 안 보이나 (매복 중).</summary>
        public bool HiddenFrom(CrowdArmy a, int viewerTeam) => a.Hidden && a.Team != viewerTeam;

        private void UpdateAmbush()
        {
            var r = Rules;
            Fix reveal2 = r.AmbushRevealRange * r.AmbushRevealRange;
            foreach (var a in Armies)
            {
                if (!a.Hidden) continue;
                bool struck = false, seen = false, sprung = false; // sprung = 적이 매복 자리로 걸어 들어옴
                foreach (var s in a.Soldiers)
                {
                    if (!s.Alive) continue;
                    if (s.LastAttackTick == Tick) struck = true;
                    if (s.Hp < s.MaxHp && s.DiedTick < 0 && a.LastHitTick == Tick) seen = true;
                }
                if (!struck && !seen && Tick % 5 == 0)
                {
                    var c = a.Centroid();
                    if (!InCover(a)) seen = true; // 숨는 땅을 벗어나면 드러남
                    else
                        foreach (var s in Soldiers)
                            if (s.Alive && s.Team != a.Team && Vec.DistanceSquared(s.Pos, c) <= reveal2 + a.HalfWidth * a.HalfWidth) { seen = sprung = true; break; }
                }
                if (!struck && !seen) continue;
                a.Hidden = false;
                if (struck || sprung) // 먼저 치거나 적이 걸어 들어오면 기습 (맞아서 · 숨는 땅을 벗어나 드러나면 아님)
                {
                    // 기습: 둘레 적 부대 사기 · 혼란
                    var c = a.Centroid();
                    Fix shock2 = (r.AmbushRevealRange + a.HalfWidth * 2) * (r.AmbushRevealRange + a.HalfWidth * 2);
                    foreach (var o in Armies)
                        if (o.Team != a.Team && Standing(o) && Vec.DistanceSquared(o.Centroid(), c) <= shock2)
                        {
                            LoseMorale(o, r.AmbushMorale);
                            o.ConfusedUntil = Tick + System.Math.Max(1, (int)(Rules.ConfuseSeconds / Rules.TickSeconds).RoundToLong());
                            AmbushHits++;
                        }
                }
            }
        }

        /// <summary>기습으로 흔들린 적 부대 수 (시험 · 화면).</summary>
        public int AmbushHits { get; private set; }

        /// <summary>함정: 구덩이는 처음 밟은 적 둘레에 한 번, 마름쇠는 그 안 적이 느려짐 (걸음 배수는 TrapSlow).</summary>
        private void StepTraps()
        {
            if (Traps.Count == 0) return;
            var r = Rules;
            Fix r2 = r.TrapRadius * r.TrapRadius;
            foreach (var t in Traps)
            {
                if (t.Used) continue;
                bool stepped = false;
                foreach (var s in Soldiers)
                    if (s.Alive && s.Team != t.Team && Vec.DistanceSquared(s.Pos, t.Pos) <= r2) { stepped = true; break; }
                if (!stepped) continue;
                t.Revealed = true;
                if (t.Kind != CrowdTrapKinds.Pit) continue;
                t.Used = true;
                var hit = new HashSet<int>();
                foreach (var s in Soldiers)
                {
                    if (!s.Alive || s.Team == t.Team || Vec.DistanceSquared(s.Pos, t.Pos) > r2 * 4) continue;
                    _damage[s.Id] += r.TrapPitDamage;
                    hit.Add(s.Army);
                }
                foreach (int ai in hit)
                {
                    var a = Armies[ai];
                    LoseMorale(a, r.TrapPitMorale);
                    a.ConfusedUntil = Tick + System.Math.Max(1, (int)(Rules.ConfuseSeconds / Rules.TickSeconds).RoundToLong());
                }
            }
        }

        /// <summary>마름쇠 안 적 병사의 걸음 배수 (없으면 1).</summary>
        private Fix TrapSlow(CrowdSoldier s)
        {
            if (Traps.Count == 0) return Fix.OneValue;
            Fix r2 = Rules.TrapRadius * Rules.TrapRadius * 4;
            foreach (var t in Traps)
                if (t.Kind == CrowdTrapKinds.Slow && t.Team != s.Team && Vec.DistanceSquared(s.Pos, t.Pos) <= r2) return Rules.TrapSlowPct / 100;
            return Fix.OneValue;
        }

        // ---------------- 화계 · 낙석 (R-164) ----------------

        /// <summary>타는 칸 → 꺼지는 틱. 다 탄 칸은 다시 타지 않음.</summary>
        public readonly Dictionary<int, int> Burning = new Dictionary<int, int>();
        private readonly HashSet<int> _burnt = new HashSet<int>();
        /// <summary>타는 칸 → 불이 처음 붙은 곳 (fireSpreadRange 안에서만 번짐).</summary>
        private readonly Dictionary<int, Vec> _fireFrom = new Dictionary<int, Vec>();
        /// <summary>다 탄 칸 (화면).</summary>
        public IReadOnlyCollection<int> BurntCells => _burnt;
        private readonly List<(int army, Vec point, int landTick)> _pendingRocks = new List<(int, Vec, int)>();
        /// <summary>떨어진 돌 (화면 · 시험): 틱 · 자리.</summary>
        public readonly List<(int tick, Vec at)> RockEvents = new List<(int, Vec)>();

        private bool Flammable(int col, int row) => Terrain != null && Rules.FireTerrains.Contains(Terrain.Map.TerrainAt(col, row));

        private bool CanFire(Vec p)
        {
            if (Terrain == null) return false;
            Terrain.CellOf(p, out int c, out int r);
            return Flammable(c, r) && !_burnt.Contains(r * Terrain.Map.Width + c);
        }

        private bool CanRock(CrowdArmy a, Vec p)
        {
            if (Terrain == null || !Terrain.Map.HasHeights) return false;
            return Terrain.LevelAt(a.Centroid()) - Terrain.LevelAt(p) >= Rules.RockHeightMin;
        }

        private void Ignite(Vec p)
        {
            if (Terrain == null) return;
            Terrain.CellOf(p, out int c0, out int r0);
            int rad = System.Math.Max(0, (int)(Rules.FireStartRadius / Terrain.CellSize).RoundToLong());
            int burnTicks = System.Math.Max(1, (int)(Rules.FireBurnSeconds / Rules.TickSeconds).RoundToLong());
            for (int dc = -rad; dc <= rad; dc++)
                for (int dr = -rad; dr <= rad; dr++)
                {
                    int c = c0 + dc, r = r0 + dr;
                    if (c < 0 || r < 0 || c >= Terrain.Map.Width || r >= Terrain.Map.Height || dc * dc + dr * dr > rad * rad) continue;
                    int cell = r * Terrain.Map.Width + c;
                    if (Flammable(c, r) && !_burnt.Contains(cell) && !Burning.ContainsKey(cell)) { Burning[cell] = Tick + burnTicks; _fireFrom[cell] = p; }
                }
        }

        private void StepHazards()
        {
            var r = Rules;
            // 떨어지는 돌
            for (int k = 0; k < _pendingRocks.Count; k++)
            {
                var (army, point, land) = _pendingRocks[k];
                if (land > Tick) continue;
                _pendingRocks.RemoveAt(k--);
                RockEvents.Add((Tick, point));
                Fix r2 = r.RockRadius * r.RockRadius;
                var hit = new HashSet<int>();
                foreach (var s in Soldiers)
                {
                    if (!s.Alive || Vec.DistanceSquared(s.Pos, point) > r2) continue;
                    _damage[s.Id] += r.RockDamage;
                    hit.Add(s.Army);
                }
                foreach (int ai in hit) LoseMorale(Armies[ai], r.RockMorale);
            }
            if (RockEvents.Count > 0 && RockEvents[0].tick < Tick - 50) RockEvents.RemoveAt(0);
            if (Burning.Count == 0 || Terrain == null) return;
            int w = Terrain.Map.Width, h = Terrain.Map.Height;
            int burnTicks = System.Math.Max(1, (int)(r.FireBurnSeconds / r.TickSeconds).RoundToLong());
            // 번짐: fireSpreadSeconds마다 타는 칸의 이웃 숲 · 풀 칸이 fireSpreadChance로 붙음 (칸 번호 순서 — 같은 결과), 처음 붙은 곳에서 fireSpreadRange 안만
            int spreadTicks = System.Math.Max(1, (int)(r.FireSpreadSeconds / r.TickSeconds).RoundToLong());
            if (Tick % spreadTicks == 0)
            {
                var cells = new List<int>(Burning.Keys);
                cells.Sort();
                var add = new List<int>();
                var addFrom = new List<Vec>();
                Fix range2 = r.FireSpreadRange * r.FireSpreadRange;
                foreach (int cell in cells)
                {
                    int c = cell % w, row = cell / w;
                    var from = _fireFrom[cell];
                    foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nc = c + dc, nr = row + dr;
                        if (nc < 0 || nr < 0 || nc >= w || nr >= h) continue;
                        int n = nr * w + nc;
                        if (Burning.ContainsKey(n) || _burnt.Contains(n) || add.Contains(n) || !Flammable(nc, nr)) continue;
                        if (Vec.DistanceSquared(Terrain.CellCenter(nc, nr), from) > range2) continue;
                        if (_rng.Chance(r.FireSpreadChance)) { add.Add(n); addFrom.Add(from); }
                    }
                }
                for (int k = 0; k < add.Count; k++) { Burning[add[k]] = Tick + burnTicks; _fireFrom[add[k]] = addFrom[k]; }
            }
            // 꺼짐
            var done = new List<int>();
            foreach (var kv in Burning) if (kv.Value <= Tick) done.Add(kv.Key);
            foreach (int cell in done) { Burning.Remove(cell); _fireFrom.Remove(cell); _burnt.Add(cell); }
            // 타는 칸 병사: 초마다 피해, 부대 사기 · 혼란
            int sec = System.Math.Max(1, (int)(Fix.OneValue / r.TickSeconds).RoundToLong());
            if (Tick % sec != 0) return;
            var hurt = new HashSet<int>();
            foreach (var s in Soldiers)
            {
                if (!s.Alive) continue;
                if (!Burning.ContainsKey(Terrain.CellIndex(s.Pos))) continue;
                _damage[s.Id] += r.FireDamage;
                hurt.Add(s.Army);
            }
            foreach (int ai in hurt)
            {
                var a = Armies[ai];
                LoseMorale(a, r.FireMorale);
                a.ConfusedUntil = Tick + System.Math.Max(1, (int)(r.ConfuseSeconds / r.TickSeconds).RoundToLong());
            }
        }

        /// <summary>그 칸이 지금 타는지 (화면).</summary>
        public bool IsBurning(int col, int row) => Terrain != null && Burning.ContainsKey(row * Terrain.Map.Width + col);
        /// <summary>그 칸이 다 탔는지 (화면).</summary>
        public bool IsBurnt(int col, int row) => Terrain != null && _burnt.Contains(row * Terrain.Map.Width + col);
    }
}
