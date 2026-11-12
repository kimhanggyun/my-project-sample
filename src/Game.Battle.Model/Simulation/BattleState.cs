using System.Collections.Generic;
using Game.Battle.Model.Field;
using Game.Battle.Model.Geometry;
using Game.Data.Defs;
using Game.Data.Rules;
using Game.Foundation.Math;
using Game.Foundation.Random;

namespace Game.Battle.Model.Simulation
{
    public enum CorpsStatus
    {
        /// <summary>싸우는 중.</summary>
        Active,
        /// <summary>퇴각 방향 끝으로 가는 중 (명령을 받지 않는다. 사거리 안의 적과는 싸운다, R-20).</summary>
        Retreating,
        /// <summary>전장을 빠져나감.</summary>
        Retreated,
        /// <summary>병력 0.</summary>
        Destroyed,
        /// <summary>장수가 포로가 됨 → 군단 궤멸 (남은 병력은 흩어져 전투에서 빠짐).</summary>
        Captured,
        /// <summary>예비 군단: 증원 이벤트가 올 때까지 전장 밖에서 기다림.</summary>
        Reserve,
    }

    public enum AttackSide { Front, Flank, Rear }

    /// <summary>대기 방식 (battle.md 4.3.4). 대기 명령의 Mode.</summary>
    public static class HoldModes
    {
        public const string Normal = "normal";
        /// <summary>휴식: 사기·책략 점수 회복이 빠르다. 싸우게 되면 깨진다.</summary>
        public const string Rest = "rest";
        /// <summary>협공 대기: 함께 협공 대기하는 군단이 모두 자리를 잡으면 함께 공격.</summary>
        public const string Coordinate = "coordinate";

        public static bool IsValid(string mode) => mode == null || mode == Normal || mode == Rest || mode == Coordinate;
    }

    public static class MoveModes
    {
        /// <summary>이동: 길에서 적과 부딪히면 멈춰 싸운 뒤 다시 이동.</summary>
        public const string Normal = "normal";
        /// <summary>강행: 사거리 안의 적은 공격하되 멈추지 않는다.</summary>
        public const string Forced = "forced";
        /// <summary>공격 이동: 가는 길에 감지 거리 안의 적을 찾아 공격.</summary>
        public const string AttackMove = "attackMove";
        /// <summary>지원 이동 (후위의 진군, R-63): 곧게 가다가 적이 advanceRoles.rearStandoffCells 안에 들어오면 그 자리에 멈춰 싸운다 (쫓지 않는다).</summary>
        public const string Support = "support";
    }

    /// <summary>군단의 현재 명령. 명령 처리기·AI만 바꾼다.</summary>
    public sealed class CorpsOrder
    {
        public string Kind { get; set; } = OrderKinds.Hold;
        public Vec Point { get; set; }
        public string TargetCorpsId { get; set; }
        public string Mode { get; set; } = MoveModes.Normal;
        /// <summary>진군을 군단 역할(<see cref="CorpsSetup.AdvanceRole"/>)대로 (플레이어의 진군·전군 진군 명령). AI의 진군은 역할을 쓰지 않는다.</summary>
        public bool ByRole { get; set; }
        /// <summary>AI 진군 (R-65): 가장 가까운 적으로 모이지 않고 자기 줄(지금 가로 위치)로 앞으로 곧게.</summary>
        public bool Lane { get; set; }
    }

    public static class OrderKinds
    {
        public const string Hold = "hold";
        public const string MoveTo = "moveTo";
        public const string Attack = "attack";
        /// <summary>진군: 그때그때 가장 가까운 적으로 (대상이 빠지면 다음 적).</summary>
        public const string Advance = "advance";
        public const string Retreat = "retreat";
    }

    public sealed class SubUnitState
    {
        public string UnitId { get; internal set; }
        public string Slot { get; internal set; }
        public TroopTypeDef Troop { get; internal set; }
        public long StartTroops { get; internal set; }
        public long Troops { get; internal set; }
        /// <summary>아직 정수 병력으로 떨어지지 않은 누적 피해.</summary>
        public Fix PendingLoss { get; internal set; }
        /// <summary>앞줄에서 싸울 준비가 된 병력 (R-73 전열). 음수면 아직 정하지 않음 (첫 틱에 한도까지).</summary>
        public Fix FrontTroops { get; internal set; } = Fix.FromInt(-1);
        public Vec Position { get; internal set; }
        /// <summary>혼란 남은 틱. 혼란 중에는 공격하지 않는다.</summary>
        public long ConfusedTicks { get; internal set; }
        /// <summary>부대 결속 (캠페인에서 받음): 혼란 저항·회복.</summary>
        public Fix Cohesion { get; internal set; }
        /// <summary>부대 훈련도 (0~1, 전투 중 바뀌지 않는다).</summary>
        public Fix Training { get; internal set; }
        /// <summary>이번 틱에 이 소부대가 공격한 적 소부대 (없으면 null). 화면 연출용이며 규칙 계산·상태 지문에 쓰지 않는다.</summary>
        public SubUnitState AttackTarget { get; internal set; }
        public bool Confused => ConfusedTicks > 0;
        public bool Alive => Troops > 0;
    }

    /// <summary>군단에 걸린 상태 하나 (버프·디버프·도발·복병 준비 …). 틱이 0이 되면 사라진다.</summary>
    public sealed class StatusEffect
    {
        public string Id { get; internal set; }
        public long TicksLeft { get; internal set; }
        /// <summary>이 상태가 공격에 더하는 %p (없으면 0).</summary>
        public Fix AttackPp { get; internal set; }
        /// <summary>건 군단 (도발 등). 집중 사격이면 노리는 적 군단.</summary>
        public string SourceCorpsId { get; internal set; }
        /// <summary>이 상태인 군단이 맞을 때 덜 맞는 %p (철벽 등, R-76).</summary>
        public Fix DefensePp { get; internal set; }
        /// <summary>SourceCorpsId 군단을 칠 때만 더하는 공격 %p (집중 사격, R-76).</summary>
        public Fix TargetCorpsPp { get; internal set; }
        /// <summary>손실 사기 %p 보정 (철벽: 음수 = 덜 흔들림, R-76).</summary>
        public Fix MoraleLossPct { get; internal set; }
        /// <summary>움직이지 않는다 (철벽, R-76).</summary>
        public bool Immobile { get; internal set; }
        /// <summary>이동 속도 % (강행군, R-82).</summary>
        public Fix MoveSpeedPct { get; internal set; }
        /// <summary>끝날 때 사기 가감 (강행군 피로, R-82).</summary>
        public Fix EndMorale { get; internal set; }
        /// <summary>장수 포위 압력 % (선두 돌파, R-82).</summary>
        public Fix CaptureRiskPct { get; internal set; }
        /// <summary>반간에 걸린 상대에게서 깎는 책략 점수 (R-82).</summary>
        public Fix DrainPoints { get; internal set; }
    }

    /// <summary>포로 기록 (전투 뒤 등용·구출 처리는 캠페인, captivity.md).</summary>
    public sealed class CaptureRecord
    {
        public string GeneralId { get; internal set; }
        public string CorpsId { get; internal set; }
        public string CapturedByTeam { get; internal set; }
        /// <summary>"encircled"(포위) 또는 "duel"(일기토 패배).</summary>
        public string Cause { get; internal set; }
        /// <summary>그 전투의 논쟁에서 세뇌로 이긴 상대 → 등용 성공률 보너스.</summary>
        public bool BrainwashBonus { get; internal set; }
    }

    public static class StatusIds
    {
        public const string Taunted = "taunted";
        public const string AmbushSetup = "ambushSetup";
        public const string Surprise = "surprise";
        /// <summary>일기토 패배: 혼란이 끝나면 퇴각.</summary>
        public const string Routing = "routing";
        /// <summary>다음 진형 변경이 바로 (진형 급변, R-76).</summary>
        public const string QuickFormation = "quickFormation";
        /// <summary>원거리 소부대가 한 적 군단을 노림 (집중 사격, R-76).</summary>
        public const string FocusFire = "focusFire";
        /// <summary>거짓 퇴각 중 (R-80). 끝나면 돌아서서 반격.</summary>
        public const string FeignedRetreat = "feignedRetreat";
        /// <summary>은밀 행군 중 (R-82): 움직여도 숨어 있다.</summary>
        public const string StealthMarch = "stealthMarch";
        /// <summary>이간당함 (R-82): 지휘 범위·협공·시야 공유에서 빠짐.</summary>
        public const string Discord = "discord";
        /// <summary>반간을 걸어 둠 (R-82).</summary>
        public const string CounterIntrigue = "counterIntrigue";
    }

    public sealed class CorpsState
    {
        public int Index { get; internal set; }
        /// <summary>전투 중 일기토 상대 군단 (R-71). 대결 중이면 서로 싸우지 않고 명령을 받지 않는다. 없으면 null.</summary>
        public string DuelWith { get; internal set; }
        /// <summary>진행 중인 거짓 퇴각 (R-80).</summary>
        internal Skills.FeignPlan Feign { get; set; }
        public string Id => Setup.Id;
        public string Team => Setup.Team;
        public CorpsSetup Setup { get; internal set; }
        public FormationDef Formation { get; internal set; }
        /// <summary>바꾸는 중인 진형 (R-56). 다 바뀔 때까지 지금 진형(Formation)을 쓰고, 받는 피해가 늘어난다. 없으면 null.</summary>
        public FormationDef PendingFormation { get; internal set; }
        /// <summary>진형 전환이 끝나기까지 남은 틱.</summary>
        public long ReformTicksLeft { get; internal set; }
        public bool Reforming => PendingFormation != null;
        public TemperamentDef Temperament { get; internal set; }
        public Vec Anchor { get; internal set; }
        public Vec Facing { get; internal set; }
        public CorpsStatus Status { get; internal set; }
        public CorpsOrder Order { get; private set; } = new CorpsOrder();
        public Fix Morale { get; internal set; }
        public Fix StartMorale { get; internal set; }
        public long StartSupply { get; internal set; }
        public long Supply { get; internal set; }
        /// <summary>틱마다 Σ(병력 × 하루 소비량 × 하루 환산 × (100 + 날씨 %p))를 더한 값. 소비 = 이 값 × 틱 길이 ÷ 6000 (내림, R-6과 같은 식).</summary>
        public long SupplyAccumulator { get; internal set; }
        public List<SubUnitState> Units { get; } = new List<SubUnitState>();

        // 스킬·상태
        public Skills.SkillPointPool SkillPoints { get; internal set; }
        public IReadOnlyList<SkillDef> KnownSkills { get; internal set; }
        public List<StatusEffect> Statuses { get; } = new List<StatusEffect>();
        /// <summary>은신 (복병 완성). 적에게 보이지 않고, 들키거나 공격하기 전에는 공격하지 않는다 (R-20 예외).</summary>
        public bool Hidden { get; internal set; }
        /// <summary>복병 효과 매개변수 (준비·은신 중일 때).</summary>
        public SkillPart AmbushEffect { get; internal set; }
        /// <summary>사거리 밖이라 다가가는 중인 스킬 (점수·횟수는 이미 냈다, R-12).</summary>
        public Commands.BattleCommand PendingSkill { get; internal set; }
        public bool MovedThisTick { get; internal set; }
        /// <summary>포위 상태에서 쌓인 포로 압력 (battle.md 2.5.1).</summary>
        public Fix CapturePressure { get; internal set; }
        /// <summary>AI 성향 (설정값에서 시작, 스텝 연출로 바뀔 수 있음).</summary>
        public string AiStance { get; internal set; }
        /// <summary>이미 남긴 경고 종류 (군단마다 한 번).</summary>
        public HashSet<string> Warned { get; } = new HashSet<string>();

        public StatusEffect FindStatus(string id)
        {
            foreach (var st in Statuses) if (st.Id == id) return st;
            return null;
        }

        public bool HasStatus(string id) => FindStatus(id) != null;

        /// <summary>같은 id면 시간을 새로 잡는다.</summary>
        public void AddStatus(StatusEffect status)
        {
            var old = FindStatus(status.Id);
            if (old != null) Statuses.Remove(old);
            Statuses.Add(status);
        }

        public void RemoveStatus(string id)
        {
            var old = FindStatus(id);
            if (old != null) Statuses.Remove(old);
        }

        public bool KnowsSkill(string id)
        {
            foreach (var s in KnownSkills) if (s.Id == id) return true;
            return false;
        }

        // 길
        public List<Cell> Path { get; internal set; } = new List<Cell>();
        public int PathIndex { get; internal set; }
        public Cell? PathGoal { get; internal set; }
        public long PathPlannedTick { get; internal set; } = -1;

        // 이번 틱 전투 기록 (다음 틱 이동·사기 계산에 쓴다)
        public bool Engaged { get; internal set; }
        /// <summary>근접 소부대가 적 군단과 맞닿음 (접전).</summary>
        public bool MeleeContact { get; internal set; }
        public bool HitFromSide { get; internal set; }
        public bool BeingHit { get; internal set; }
        public long LostThisTick { get; internal set; }

        public bool InBattle => Status == CorpsStatus.Active || Status == CorpsStatus.Retreating;

        /// <summary>예약된 명령 (지금 명령이 끝나면 차례로, battle.md 4.3.5). 플레이어가 새 명령(예약 아님)을 내리면 비운다.</summary>
        public List<Commands.BattleCommand> Queue { get; } = new List<Commands.BattleCommand>();
        /// <summary>지금 협공 대기를 시작한 틱, 이미 한 번 물었는지.</summary>
        public long CoordinateSinceTick { get; internal set; }
        public bool CoordinateAsked { get; internal set; }

        public bool Resting => Order.Kind == OrderKinds.Hold && Order.Mode == HoldModes.Rest;
        public bool Coordinating => Order.Kind == OrderKinds.Hold && Order.Mode == HoldModes.Coordinate;

        public long StartTroops
        {
            get { long s = 0; foreach (var u in Units) s += u.StartTroops; return s; }
        }

        public long Troops
        {
            get { long s = 0; foreach (var u in Units) s += u.Troops; return s; }
        }

        public List<TroopTypeDef> AliveTroopTypes()
        {
            var list = new List<TroopTypeDef>();
            foreach (var u in Units) if (u.Alive && !list.Contains(u.Troop)) list.Add(u.Troop);
            return list;
        }

        public bool HasAliveMelee(string meleeTag)
        {
            foreach (var u in Units) if (u.Alive && u.Troop.HasTag(meleeTag)) return true;
            return false;
        }

        /// <summary>명령을 바꾼다 (명령 처리기·AI가 쓴다). 길은 다시 찾는다.</summary>
        public void AssignOrder(CorpsOrder order)
        {
            Order = order ?? new CorpsOrder();
            OrderByPlayer = false;
            ClearPath();
        }

        /// <summary>위임 중 (아군 군단을 AI가 지휘, ai.md 9장).</summary>
        public bool Delegated { get; internal set; }
        /// <summary>지금 명령을 플레이어가 내렸다 (위임 중에도 이 명령이 끝날 때까지 AI가 덮어쓰지 않는다).</summary>
        public bool OrderByPlayer { get; internal set; }
        /// <summary>AI 판단 수준 (AI 군단은 설정값, 아군 군단은 장수 능력치로 정한 위임 수준).</summary>
        public string AiDifficulty { get; internal set; }

        /// <summary>AI가 지휘하는가: AI 군단이거나, 위임 중이고 플레이어 명령이 진행 중이 아닐 때.</summary>
        public bool AiInCharge => Setup.Controller == Controllers.Ai || (Delegated && !OrderByPlayer);

        public void ClearPath()
        {
            Path = new List<Cell>();
            PathIndex = 0;
            PathGoal = null;
        }
    }

    /// <summary>전투 한 판의 전체 상태. 시스템들이 읽고 쓴다 (전투 모듈 안에서만).</summary>
    public sealed class BattleContext
    {
        public BattleSetup Setup { get; internal set; }
        public DefinitionSet Defs => Setup.Defs;
        public CombatRules Combat => Setup.Combat;
        public BalanceData Balance => Setup.Balance;
        public RuleFlags Flags => Setup.Flags;
        public Battlefield Field { get; internal set; }
        public IRandom Random { get; internal set; }
        public Skills.SkillRegistry SkillRegistry { get; internal set; }
        public long Tick { get; internal set; }
        public List<CorpsState> Corps { get; } = new List<CorpsState>();
        public List<string> Teams { get; } = new List<string>();
        /// <summary>팀 → 시야 (전장 안개).</summary>
        public Dictionary<string, Vision.TeamVision> Vision { get; } = new Dictionary<string, Vision.TeamVision>();
        public List<Vision.RevealZone> Reveals { get; } = new List<Vision.RevealZone>();
        /// <summary>맵 오브젝트·점령 구역의 지금 상태 (R-78).</summary>
        public List<Victory.MapObjectState> Objects { get; } = new List<Victory.MapObjectState>();
        /// <summary>전조 중인 스킬 효과 (정해진 틱에 나타난다).</summary>
        public List<Skills.ScheduledEffect> Scheduled { get; } = new List<Skills.ScheduledEffect>();
        /// <summary>바람·날씨·시간대·불.</summary>
        public Environment.BattleEnvironment Environment { get; internal set; }
        /// <summary>전투 중 포로가 된 장수.</summary>
        public List<CaptureRecord> Captures { get; } = new List<CaptureRecord>();
        /// <summary>장수 id → "light"/"heavy".</summary>
        public Dictionary<string, string> Injuries { get; } = new Dictionary<string, string>();
        /// <summary>전투 중 일기토 (R-71). 끝난 것도 남긴다 (화면이 결과를 읽는다).</summary>
        public List<Contests.DuelState> Duels { get; } = new List<Contests.DuelState>();
        /// <summary>플레이어 군단이 받고 아직 답하지 않은 일기토 신청 (R-79).</summary>
        public List<Contests.DuelOffer> DuelOffers { get; } = new List<Contests.DuelOffer>();
        /// <summary>숨겨 둔 함정 (R-80). 놓은 팀만 본다.</summary>
        public List<Skills.FieldTrap> Traps { get; } = new List<Skills.FieldTrap>();
        /// <summary>위장 깃발 (R-80).</summary>
        public List<Skills.Decoy> Decoys { get; } = new List<Skills.Decoy>();
        /// <summary>이 전투의 논쟁에서 세뇌로 진 장수 (포로가 되면 등용 보너스).</summary>
        public HashSet<string> Brainwashed { get; } = new HashSet<string>();
        /// <summary>전투 이벤트 기록 (말풍선·연출·리플레이·숨은 보상).</summary>
        public List<Events.BattleEvent> Log { get; } = new List<Events.BattleEvent>();
        /// <summary>명령 처리기 (예약 명령을 꺼내 쓸 때).</summary>
        internal Commands.CommandRegistry Commands { get; set; }
        /// <summary>이미 일어난 스텝 연출 (Setup.Events 번호).</summary>
        public HashSet<int> FiredEvents { get; } = new HashSet<int>();

        /// <summary>그 팀이 지금 이 군단을 보는가 (안개·은신 반영). 같은 팀은 항상 보인다.</summary>
        /// <summary>그 팀이 지금 이 지점을 보는가 (안개).</summary>
        public bool IsPointVisibleTo(string team, Vec p)
        {
            if (!Vision.TryGetValue(team, out var v) || v.VisibleCells == null) return false;
            int x = (int)p.X.FloorToLong(), y = (int)p.Y.FloorToLong();
            if (!Field.Map.InBounds(x, y)) return false;
            return v.VisibleCells[y * Field.Width + x];
        }

        public bool IsVisibleTo(string team, CorpsState corps) =>
            corps.Team == team || (Vision.TryGetValue(team, out var v) && v.VisibleCorps.Contains(corps.Id));

        public CorpsState FindCorps(string id)
        {
            foreach (var c in Corps) if (c.Id == id) return c;
            return null;
        }

        /// <summary>가장 가까운, 지금 보이는 전투 중인 적 군단 (같은 거리면 앞 순서).</summary>
        public CorpsState NearestEnemy(CorpsState self)
        {
            CorpsState best = null;
            Fix bestD = Fix.Zero;
            foreach (var c in Corps)
            {
                if (c.Team == self.Team || !c.InBattle || !IsVisibleTo(self.Team, c)) continue;
                Fix d = Vec.DistanceSquared(c.Anchor, self.Anchor);
                if (best == null || d < bestD) { best = c; bestD = d; }
            }
            return best;
        }

        public bool IsEnabled(string flag) => Flags.IsEnabled(flag);

        /// <summary>우리 진영의 앞 방향 (군단 설정의 방향, 없으면 팀 시작 위치의 방향). 군단이 몸을 돌려도 바뀌지 않는다.</summary>
        public Vec Forward(CorpsState c)
        {
            string f = c.Setup.Facing;
            if (f == null && Setup.Map.Spawns.TryGetValue(c.Team, out var spawn)) f = spawn.Facing;
            return Geometry.Facing.Parse(f ?? "N");
        }

        /// <summary>전장 안으로 자른 점.</summary>
        public Vec ClampToField(Vec p)
        {
            var half = Fix.FromRatio(1, 2);
            return new Vec(Fix.Clamp(p.X, half, Fix.FromInt(Field.Width) - half), Fix.Clamp(p.Y, half, Fix.FromInt(Field.Height) - half));
        }

        /// <summary>자기 줄로 앞으로 곧게 간 끝 (전장 끝, R-60·R-65).</summary>
        public Vec LaneEnd(CorpsState c) => ClampToField(c.Anchor + Forward(c) * Fix.FromInt(Field.Width + Field.Height));

        /// <summary>
        /// 보이는 적이 없을 때 진군할 곳: 마지막으로 본 가장 가까운 적 위치, 그것도 없으면 적 팀 시작 위치.
        /// </summary>
        public Vec? AdvanceFallback(CorpsState self)
        {
            Vec? best = null;
            Fix bestD = Fix.Zero;
            if (Vision.TryGetValue(self.Team, out var v))
                foreach (var c in Corps)
                {
                    if (c.Team == self.Team || !c.InBattle || !v.LastSeen.TryGetValue(c.Id, out var seen)) continue;
                    Fix d = Vec.DistanceSquared(seen, self.Anchor);
                    if (best == null || d < bestD) { best = seen; bestD = d; }
                }
            if (best != null) return best;
            foreach (var team in Teams)
                if (team != self.Team && Setup.Map.Spawns.TryGetValue(team, out var spawn))
                    return new Cell((int)spawn.X.FloorToLong(), (int)spawn.Y.FloorToLong()).Center;
            return null;
        }
    }
}
