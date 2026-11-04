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
    }

    public enum AttackSide { Front, Flank, Rear }

    public static class MoveModes
    {
        /// <summary>이동: 길에서 적과 부딪히면 멈춰 싸운 뒤 다시 이동.</summary>
        public const string Normal = "normal";
        /// <summary>강행: 사거리 안의 적은 공격하되 멈추지 않는다.</summary>
        public const string Forced = "forced";
        /// <summary>공격 이동: 가는 길에 감지 거리 안의 적을 찾아 공격.</summary>
        public const string AttackMove = "attackMove";
    }

    /// <summary>군단의 현재 명령. 명령 처리기·AI만 바꾼다.</summary>
    public sealed class CorpsOrder
    {
        public string Kind { get; set; } = OrderKinds.Hold;
        public Vec Point { get; set; }
        public string TargetCorpsId { get; set; }
        public string Mode { get; set; } = MoveModes.Normal;
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
        public Vec Position { get; internal set; }
        public bool Alive => Troops > 0;
    }

    public sealed class CorpsState
    {
        public int Index { get; internal set; }
        public string Id => Setup.Id;
        public string Team => Setup.Team;
        public CorpsSetup Setup { get; internal set; }
        public FormationDef Formation { get; internal set; }
        public TemperamentDef Temperament { get; internal set; }
        public Vec Anchor { get; internal set; }
        public Vec Facing { get; internal set; }
        public CorpsStatus Status { get; internal set; }
        public CorpsOrder Order { get; private set; } = new CorpsOrder();
        public Fix Morale { get; internal set; }
        public Fix StartMorale { get; internal set; }
        public long StartSupply { get; internal set; }
        public long Supply { get; internal set; }
        /// <summary>틱마다 Σ(병력 × 하루 소비량 × 하루 환산)을 더한 값. 소비 = 이 값 × 틱 길이 ÷ 60 (내림, R-6과 같은 식).</summary>
        public long SupplyAccumulator { get; internal set; }
        public List<SubUnitState> Units { get; } = new List<SubUnitState>();

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
            ClearPath();
        }

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
        public long Tick { get; internal set; }
        public List<CorpsState> Corps { get; } = new List<CorpsState>();
        public List<string> Teams { get; } = new List<string>();

        public CorpsState FindCorps(string id)
        {
            foreach (var c in Corps) if (c.Id == id) return c;
            return null;
        }

        /// <summary>가장 가까운 전투 중인 적 군단 (같은 거리면 앞 순서).</summary>
        public CorpsState NearestEnemy(CorpsState self)
        {
            CorpsState best = null;
            Fix bestD = Fix.Zero;
            foreach (var c in Corps)
            {
                if (c.Team == self.Team || !c.InBattle) continue;
                Fix d = Vec.DistanceSquared(c.Anchor, self.Anchor);
                if (best == null || d < bestD) { best = c; bestD = d; }
            }
            return best;
        }

        public bool IsEnabled(string flag) => Flags.IsEnabled(flag);
    }
}
