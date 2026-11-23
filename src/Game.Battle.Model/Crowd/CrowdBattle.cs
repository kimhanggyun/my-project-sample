using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Data.Defs;
using Game.Foundation.Math;
using Game.Foundation.Random;

namespace Game.Battle.Model.Crowd
{
    /// <summary>샘플전투2 병사 하나 (장수도 병사 하나로, 맞으면 체력이 준다).</summary>
    public sealed class CrowdSoldier
    {
        public int Id;
        public int Team;
        /// <summary>속한 부대 (CrowdBattle.Armies 번호).</summary>
        public int Army;
        public bool IsGeneral;
        public Vec Pos;
        /// <summary>대열 안 자리 (부대 기준, 앞 = +y).</summary>
        public Vec Slot;
        /// <summary>지난 틱에 움직인 양 (화면이 걷기 · 달리기 · 방향에 씀).</summary>
        public Vec Moved;
        /// <summary>차지하는 반지름 (말 탄 병사는 크다).</summary>
        public Fix Radius;
        public int Hp;
        public int MaxHp;
        public int Cooldown;
        /// <summary>마지막으로 친(쏜) 틱 · 대상 (화면 공격 동작). 친 적 없으면 -1.</summary>
        public int LastAttackTick = -1;
        public int LastTargetId = -1;
        /// <summary>돌격해 부딪친 틱 (친 쪽 · 맞은 쪽 둘 다, 화면 흙먼지 · 흔들림). 없으면 -1.</summary>
        public int ImpactTick = -1;
        /// <summary>쓰러진 틱 (살아 있으면 -1).</summary>
        public int DiedTick = -1;
        /// <summary>무너져 도망치다 전장 밖으로 나간 틱 (R-128, 아니면 -1). 쓰러진 것은 아니지만 더는 싸우지 않는다.</summary>
        public int FledTick = -1;
        /// <summary>제자리 부대에서 자리에 와 멈춘 병사 (조금 밀려나도 다시 당기지 않음 — 잔떨림 없앰). 부대가 움직이면 풀림.</summary>
        public bool Settled;
        /// <summary>멈춘 자리 · 자리까지 가장 가까웠던 거리 · 가까워지지 못한 틱 수 (못 가면 그 자리에서 멈춤).</summary>
        public Vec SettleAt;
        /// <summary>멈출 때의 목표 자리 (R-142): 목표가 UnsettleDistance 넘게 옮겨지면 (진형 바뀜 · 장수 자리 바뀜 · 부대 가운데 움직임) 멈춤을 푼다.</summary>
        public Vec SettleGoal;
        public Fix BestDist = Fix.FromInt(9999);
        public int StuckTicks;
        /// <summary>성문 · 성벽 같은 구조물을 친 한 방 (R-140, 화면이 그쪽을 보고 휘두름): 친 자리.</summary>
        public bool HitStructure;
        public Vec StrikeAt;
        /// <summary>따로 갈 자리 (R-145 성벽 위 · 사다리로 올라 성 안): 있으면 대열 자리 대신 맵 칸 길을 따라 간다.</summary>
        public Vec? Spot;
        public List<Vec> SpotPath;
        public int SpotIdx;
        /// <summary>사다리 오르기 (R-145): 시작 · 끝 틱 (오르지 않으면 -1), 사다리 번호, 이번 틱 꼭대기에 닿음.</summary>
        public int ClimbStart = -1, ClimbEnd = -1, ClimbLadder = -1;
        public bool ClimbArrived;
        /// <summary>전장에서 싸울 수 있음 (쓰러지지도 나가지도 않음).</summary>
        public bool Alive => DiedTick < 0 && FledTick < 0;
    }

    /// <summary>
    /// 부대 장수 (R-138): 무력 · 지력 · 자리(front 선봉 · back 대열 뒤 · rear 멀리 뒤) · 병종 적성(뿌리 병종 → S · A · B · C, 없으면 B).
    /// 지금은 crowd.json generalPresets (맹장 · 장수 · 책사)에서, 나중에 캠페인 장수(능력치 · 적성)에서 만든다.
    /// </summary>
    public sealed class CrowdGeneral
    {
        public string Preset = "standard";
        public int Might = 70, Intellect = 65;
        public string Role = "back";
        public readonly Dictionary<string, string> Aptitude = new Dictionary<string, string>();
        public string AptitudeOf(string baseTroop) => Aptitude.TryGetValue(baseTroop, out var g) ? g : Aptitude.TryGetValue("*", out var all) ? all : "B";
    }

    /// <summary>
    /// 성문 (R-140): 맵의 gate 칸 덩어리 하나. 체력이 남아 있으면 닫혀 모두 못 지나가고, 공격 쪽 병사가 닿아서 치면 체력이 준다. 0이면 열림.
    /// 지키는 편 = 배치 자리가 성문에 더 가까운 편 (성 안).
    /// </summary>
    public sealed class CrowdGate
    {
        public readonly List<int> Cells = new List<int>();
        public Vec Center;
        public int Hp, MaxHp;
        public int Defender;
        public int LastHitTick = -1;
        /// <summary>성 안쪽 방향 (가로 · 세로 한 축, R-145): 지키는 편 배치 자리 쪽.</summary>
        public Vec Inward;
        /// <summary>1 미만 피해 조각 (문 막기 · 충차로 한 방 피해가 소수가 됨).</summary>
        public Fix Carry;
        public bool Open => Hp <= 0;
    }

    /// <summary>날아가는 화살 (R-127): 쏜 자리 → 쏠 때 대상 자리, 닿는 틱에 대상이 살아 있으면 맞을 수 있다.</summary>
    public sealed class CrowdArrow
    {
        public int ShooterId;
        public int TargetId;
        public Vec From;
        public Vec To;
        public int FireTick;
        public int ArriveTick;
        public int Damage;
        /// <summary>맞을 확률 배수 % (R-143 높이 — 위에서 쏘면 크고 아래서 쏘면 작음).</summary>
        public Fix HitPct = Fix.FromInt(100);
        /// <summary>성벽에서 떨어뜨린 돌 (R-145): 맞을 확률 stoneHitChance.</summary>
        public bool IsStone;
    }

    public static class CrowdOrders
    {
        public const string Hold = "hold", Move = "move", Attack = "attack";
        /// <summary>진군 (R-132): 진형을 지킨 채 적 쪽으로 곧게, 만나는 적과 싸운다 (궁병은 적이 사거리에 들면 멈춰 쏜다).</summary>
        public const string Advance = "advance";
        /// <summary>후퇴 (R-132): 자기 쪽 끝으로 대열 그대로 물러난다 (등을 보인 것으로 쳐 뒤에서 맞음, 쫓지 않음).</summary>
        public const string Retreat = "retreat";
    }

    /// <summary>부대 하나: 병종 · 대열 모양 · 가운데 · 앞 방향 · 명령 (제자리 · 땅으로 이동 · 적 부대 공격).</summary>
    public sealed class CrowdArmy
    {
        public int Id;
        public int Team;
        /// <summary>left · center · right (부대 하나면 center). 총대장 부대는 center.</summary>
        public string Place;
        public bool IsCommander;
        public CrowdTroop Troop = new CrowdTroop();
        public string Formation;
        public Vec Center;
        public Vec Facing;
        public string Order = CrowdOrders.Hold;
        public Vec OrderPoint;
        /// <summary>이동 명령 뒤에 이어 갈 경유 지점들 (R-129 명령 예약): 지금 목표에 닿으면 다음 지점으로.</summary>
        public readonly List<Vec> Waypoints = new List<Vec>();
        public int OrderTarget = -1;
        /// <summary>위임 (R-132): AI가 알아서 싸운다 (적 AI와 같은 규칙 — 가까운 적 공격 · 스킬).</summary>
        public bool Delegated;
        /// <summary>근접 공격 (R-132): 궁병도 활을 놓고 붙으러 간다.</summary>
        public bool Melee;
        /// <summary>경로 끝에 닿으면 공격할 적 부대 (R-132 경로 그리기, 없으면 -1).</summary>
        public int PathThenAttack = -1;
        /// <summary>장수 (R-138): 능력치 · 자리 · 병종 적성.</summary>
        public CrowdGeneral GeneralInfo = new CrowdGeneral();
        /// <summary>장수 도망 (R-142): 부대가 흔들리거나 무너졌을 때 체력 generalEscapeWaverHpPct% 이하 — 싸우지 않고 자기 쪽 끝으로 말을 달린다. 빠져나가면 부대는 장수 없음.</summary>
        public bool GeneralEscaping;
        /// <summary>선봉 장수가 다쳐 대열 안으로 물러남 (R-139): 체력 generalPullBackHpPct% 이하면 선봉 자리 → 앞줄 뒤 (지휘). 먼저 덤비지 않음 (R-142).</summary>
        public bool GeneralPulledBack;
        /// <summary>적과 만남 (R-142): 적 부대가 돌격 거리 + marchReformRange 안. 만나기 전엔 장수가 성향과 관계없이 대열 뒤 (장수 자리)에서 움직인다.</summary>
        public bool GeneralEngaged;
        /// <summary>장수 자리들 (진형 자리 기준, R-142): 만나기 전 · 만난 뒤 성향 자리 · 물러나 지휘 · 부상.</summary>
        public Vec GeneralBaseSlot, GeneralRoleSlot, GeneralCommandSlot, GeneralWoundedSlot;
        /// <summary>장수가 마지막으로 맞은 틱 (R-144 회복: 맞지 않은 지 generalRegenCalmSeconds 지나야 회복). 처음 -1.</summary>
        public int GeneralHitTick = -1;
        /// <summary>공성 명령 (R-145, SiegeTasks), 없으면 null.</summary>
        public string SiegeTask;
        /// <summary>시작 행동 (CrowdUnitSpec.Behavior), 없으면 null.</summary>
        public string StartBehavior;
        /// <summary>장수 회복 누적 (체력 1 미만 조각, R-144).</summary>
        public Fix GeneralRegenAcc;
        /// <summary>지형 길 (R-133): 못 가는 땅을 돌아가는 맵 칸 길 (목표 칸 · 찾은 틱), 이번 틱 향하는 곳 (병사가 막히면 여기로).</summary>
        public readonly List<Vec> Route = new List<Vec>();
        public int RouteGoal = -1, RouteTick = -9999;
        public Vec Aim;
        /// <summary>
        /// 행군 대열 (R-136): 돌아가는 길 · 좁은 땅(숲 · 늪 · 고갯길)을 지날 때 자동으로, 앞장선 부대 가운데가 걸은 자취(Trail)를 병사들이 줄줄이 밟는다.
        /// 목적지에 닿거나 적이 가까우면 고른 진형으로 다시 펼친다. AutoMarch = false면 행군하지 않는다 (진형 고집).
        /// </summary>
        public bool Marching;
        /// <summary>지형 따라 저절로 행군 (기본 끔 — 2026-11-22 사용자: 진형을 지키고, 행군은 [행군 이동]으로 고를 때만).</summary>
        public bool AutoMarch;
        /// <summary>이번 이동을 행군 대열로 ([행군 이동], R-137): 도착하면 풀림.</summary>
        public bool MarchMove;
        /// <summary>행군 순서 (행군을 시작할 때 목표에 가까운 병사부터 — 줄 앞).</summary>
        public readonly List<CrowdSoldier> MarchOrder = new List<CrowdSoldier>();
        /// <summary>지름길 지도 (R-137): 부대 가운데 칸까지 남은 거리 · 만든 틱 · 목표 칸.</summary>
        public long[] Flow;
        public int FlowTick = -9999, FlowCell = -1;
        public int MarchOffTicks;
        /// <summary>부대 가운데가 걸어온 자취 (맨 앞 = 가장 최근, marchTrailStep칸마다 한 점).</summary>
        public readonly List<Vec> Trail = new List<Vec>();
        /// <summary>돌격 중 (공격 명령으로 적에게 ChargeRange 안까지 다가옴, 맞붙기 전까지) — 병사 · 부대가 빨라진다. 궁병은 돌격하지 않음.</summary>
        public bool Charging;
        public readonly List<CrowdSoldier> Soldiers = new List<CrowdSoldier>();
        public CrowdSoldier General;
        public int StartCount;
        /// <summary>대열 반 폭 · 반 깊이 (자리에서, 칸).</summary>
        public Fix HalfWidth, HalfDepth;
        /// <summary>병종 빠르기 배수 (speed ÷ 기준 빠르기).</summary>
        public Fix SpeedFactor = Fix.OneValue;
        /// <summary>사기 (R-128, 0 ~ moraleStart).</summary>
        public Fix Morale;
        /// <summary>흔들림 (사기 waverAt 이하): 느려지고 덜 친다.</summary>
        public bool Wavering;
        /// <summary>무너짐 (사기 routAt 이하, 되돌아오지 않음): 도망치고 명령을 못 받는다.</summary>
        public bool Routed;
        public int RoutedTick = -1;
        /// <summary>포위됨 (최근 앞이 아닌 두 방향 이상에서 맞음) — 사기가 계속 깎인다.</summary>
        public bool Encircled;
        /// <summary>방향마다 마지막으로 맞은 틱 (0 앞 · 1 왼쪽 · 2 오른쪽 · 3 뒤).</summary>
        public readonly int[] SideHitTick = { -9999, -9999, -9999, -9999 };
        // 스킬 (R-130)
        /// <summary>남은 사용 횟수 (스킬 id → 횟수).</summary>
        public readonly Dictionary<string, int> SkillUses = new Dictionary<string, int>();
        public int LastSkillTick = -9999;
        public Fix AttackBuffPp, DefenseBuffPp, MoraleLossPct, MoveBuffPct, MarchEndMorale;
        public int AttackBuffUntil = -1, FortifyUntil = -1, MarchUntil = -1;
        /// <summary>이번 틱 시작 때 살아 있는 병사 가운데 (옆 · 뒤 판정의 기준).</summary>
        public Vec TickCentroid;
        public int LastHitTick = -9999;

        public int AliveCount
        {
            get { int n = 0; foreach (var s in Soldiers) if (s.Alive && !s.IsGeneral) n++; return n; }
        }

        /// <summary>장수가 살아 있으면 명령을 받는다 (장수를 잃은 부대는 제자리에서 싸움만).</summary>
        public bool Leading => General.Alive && !Routed;

        public Vec Right => new Vec(Facing.Y, -Facing.X); // 앞이 +y일 때 오른쪽 +x

        /// <summary>대열 자리의 전장 위치.</summary>
        public Vec SlotWorld(Vec slot) => Center + Right * slot.X + Facing * slot.Y;

        /// <summary>살아 있는 병사 가운데 (없으면 부대 가운데).</summary>
        public Vec Centroid()
        {
            Fix sx = Fix.Zero, sy = Fix.Zero;
            int n = 0;
            foreach (var s in Soldiers) if (s.Alive && !s.IsGeneral) { sx += s.Pos.X; sy += s.Pos.Y; n++; }
            return n == 0 ? (General.Alive ? General.Pos : Center) : new Vec(sx / n, sy / n);
        }
    }

    /// <summary>
    /// 3×3 칸에 놓는 부대 하나 (R-131, 테스트 탭): 줄(0 앞 · 1 가운데 · 2 뒤) · 칸(0 왼쪽 · 1 가운데 · 2 오른쪽, 보는 쪽 = 우리 편 기준),
    /// 병종 · 진형 · 병사 수 · 총대장.
    /// </summary>
    public sealed class CrowdUnitSpec
    {
        public int Row;
        public int Col;
        public string Troop = "infantry";
        public string Formation = CrowdFormations.Line;
        public int Count = 60;
        public bool Commander;
        /// <summary>장수 성향 (R-138): warrior 맹장 · standard 장수 · strategist 책사 (crowd.json generalPresets).</summary>
        public string General = "standard";
        /// <summary>시작 행동 (2026-11-23 사용자, 시험 탭 적 부대): hold 대기 (기본) · advance 진군 · delegate 위임 (AI가 지휘).</summary>
        public string Behavior = Behaviors.Hold;

        public static class Behaviors { public const string Hold = "hold", Advance = "advance", Delegate = "delegate"; }

        public static readonly string[] RowIds = { "front", "middle", "back" };
        public static readonly string[] ColIds = { "left", "center", "right" };
        /// <summary>부대 자리 이름 (CrowdArmy.Place): "front_left" 등.</summary>
        public string Place => RowIds[Row] + "_" + ColIds[Col];
    }

    /// <summary>샘플전투2 시작 설정.</summary>
    public sealed class CrowdSetup
    {
        /// <summary>3×3 칸 배치 (R-131). 비어 있지 않으면 부대 수 · 병종 · 진형 설정 대신 이것을 쓴다.</summary>
        public List<CrowdUnitSpec> AllyUnits;
        public List<CrowdUnitSpec> EnemyUnits;
        /// <summary>편마다 병사 수 (부대가 여럿이면 나눠 가진다).</summary>
        public int AllyCount = 200;
        public int EnemyCount = 200;
        /// <summary>편마다 부대 수 (1 또는 3: 좌 · 중 · 우, 가운데가 총대장).</summary>
        public int AllyCorps = 1;
        public int EnemyCorps = 1;
        public string AllyFormation = CrowdFormations.Line;
        public string EnemyFormation = CrowdFormations.Line;
        /// <summary>부대마다 병종 id (좌 · 중 · 우 순, 하나면 [0]). 비면 보병 (R-127).</summary>
        public string[] AllyTroops = { "infantry" };
        public string[] EnemyTroops = { "infantry" };
        /// <summary>true면 시작부터 우리 부대가 모두 적 총대장 부대를 공격 (시험 · 캡처). 사용자 조작이면 false.</summary>
        public bool AllyAdvance = true;
        /// <summary>true면 적 부대가 가장 가까운 우리 부대를 공격, false면 제자리.</summary>
        public bool EnemyAdvance;
        public ulong Seed = 1;
        /// <summary>지형 맵 (R-133). 없으면 평지 (fieldWidth × fieldDepth), 있으면 전장 크기 = 맵 크기 × mapCellSize, 배치는 맵의 시작 자리 기준.</summary>
        public CrowdTerrain Terrain;
    }

    /// <summary>
    /// 샘플전투2: 병사 단위 전투 (R-125 · R-126 · R-127, 시험 중 — docs/design/content/battle-crowd.md).
    /// 병사마다 자리 · 체력이 있고, 서로 밀어내 겹치지 않으며 (적과도), 가까운 적(EngageRange 안)에게 몰려가 닿으면 친다.
    /// 편마다 부대가 하나 또는 셋이고, 부대마다 병종 · 명령(제자리 · 이동 · 공격) · 진형을 따로 받는다.
    /// 병종 (R-127): 빠르기 · 닿는 거리(창이 길다) · 공격 · 방어 · 상성(troopCounters.json), 궁병은 멀리서 화살, 기병은 크고 빠르며 부딪침이 세다,
    /// 창병은 달려드는 기병을 받아친다.
    /// 규칙 계산이라 고정소수점 · 시드 난수만 쓰고, 병사를 id 순서로 계산한 뒤 한꺼번에 반영한다 (같은 입력 = 같은 결과).
    /// </summary>
    public sealed partial class CrowdBattle
    {
        public const int Ally = 0, Enemy = 1;
        public static readonly string[] Places3 = { "left", "center", "right" };

        public CrowdRules Rules { get; }
        /// <summary>지형 (R-133, 없으면 평지).</summary>
        public CrowdTerrain Terrain { get; }
        /// <summary>성문 (R-140). 없으면 빈 목록.</summary>
        public readonly List<CrowdGate> Gates = new List<CrowdGate>();
        /// <summary>성을 지키는 편 (성문이 있으면, 없으면 -1).</summary>
        public int SiegeDefender { get; private set; } = -1;
        /// <summary>막는 소품 (R-134): 나무 기둥 · 바위. 병사는 들어가지 못한다 (화면도 이 자리에 그림).</summary>
        public readonly List<CrowdObstacle> Obstacles = new List<CrowdObstacle>();
        private readonly Dictionary<long, List<int>> _obstacleGrid = new Dictionary<long, List<int>>();
        private Fix _maxObstacleRadius;
        public int Tick { get; private set; }
        public readonly List<CrowdArmy> Armies = new List<CrowdArmy>();
        public readonly List<CrowdSoldier> Soldiers = new List<CrowdSoldier>();
        /// <summary>날아가는 화살 (닿으면 빠진다).</summary>
        public readonly List<CrowdArrow> Arrows = new List<CrowdArrow>();
        /// <summary>끝났으면 이긴 편 (Ally · Enemy), 비기면 -2, 진행 중이면 -1.</summary>
        public int Winner { get; private set; } = -1;
        /// <summary>끝난 까닭: general (총대장 사로잡음) · wiped (전멸) · routed (모든 부대가 무너짐) · time.</summary>
        public string EndReason { get; private set; }
        public bool IsOver => Winner != -1;
        /// <summary>이번 틱 부딪침 수 (화면 흔들림).</summary>
        public int ImpactsThisTick { get; private set; }
        /// <summary>지금까지 맞은 화살 수 (시험 · 화면 정보).</summary>
        public int ArrowHits { get; private set; }

        private readonly bool _enemyAdvance;
        private readonly SeededRandom _rng;
        private readonly IReadOnlyList<TroopCounterRule> _counters;
        private readonly Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>();
        private Vec[] _next = new Vec[0];
        private Vec[] _knock = new Vec[0];
        /// <summary>이번 틱 병사가 가려는 곳 (자리 · 쫓는 적, R-134 비켜 가기 판단).</summary>
        private Vec[] _goal = new Vec[0];
        private int[] _damage = new int[0];
        private Fix _maxRadius;
        private readonly int[] _allRoutedSince = { -1, -1 };

        /// <param name="troops">병종 데이터 (없으면 모두 보병 기본값).</param>
        /// <param name="counters">병종 상성 (없으면 상성 없음).</param>
        public CrowdBattle(CrowdRules rules, CrowdSetup setup, IReadOnlyDictionary<string, TroopTypeDef> troops = null, IReadOnlyList<TroopCounterRule> counters = null)
        {
            Terrain = setup.Terrain;
            Rules = Terrain != null ? rules.WithField(Terrain.Width, Terrain.Depth) : rules;
            rules = Rules;
            _counters = counters ?? new TroopCounterRule[0];
            if (Terrain != null)
            {
                Obstacles.AddRange(Terrain.MakeObstacles(rules.Obstacles));
                for (int k = 0; k < Obstacles.Count; k++)
                {
                    var o = Obstacles[k];
                    long key = Key(o.Pos.X.FloorToLong(), o.Pos.Y.FloorToLong());
                    if (!_obstacleGrid.TryGetValue(key, out var l)) _obstacleGrid[key] = l = new List<int>();
                    l.Add(k);
                    if (o.Radius > _maxObstacleRadius) _maxObstacleRadius = o.Radius;
                }
            }
            _rng = new SeededRandom(setup.Seed == 0 ? 1 : setup.Seed);
            _enemyAdvance = setup.EnemyAdvance;
            _maxRadius = Fix.Max(rules.SoldierRadius, rules.MountedRadius);
            // 편의 앞줄 자리: 평지면 전장 30% · 70%, 지형 맵이면 맵 시작 자리(teamA 아래 · teamB 위)가 가운데 줄 (R-133)
            Vec allyAt = new Vec(rules.FieldWidth / 2, rules.FieldDepth * Fix.FromRatio(3, 10)), enemyAt = new Vec(rules.FieldWidth / 2, rules.FieldDepth * Fix.FromRatio(7, 10));
            if (Terrain != null)
            {
                // 뒤집힌 맵(수비, R-139)이면 맵의 teamB(지키는 쪽) 자리가 우리 쪽
                string allyKey = Terrain.Flipped ? "teamB" : "teamA", enemyKey = Terrain.Flipped ? "teamA" : "teamB";
                if (Terrain.Map.Spawns.TryGetValue(allyKey, out var sa)) allyAt = SpawnPoint(sa) + new Vec(Fix.Zero, rules.GridRowPitch);
                if (Terrain.Map.Spawns.TryGetValue(enemyKey, out var sb)) enemyAt = SpawnPoint(sb) - new Vec(Fix.Zero, rules.GridRowPitch);
            }
            if (setup.AllyUnits != null && setup.AllyUnits.Count > 0) MakeGrid(Ally, setup.AllyUnits, troops, allyAt, new Vec(Fix.Zero, Fix.OneValue));
            else MakeTeam(Ally, setup.AllyCorps, setup.AllyCount, setup.AllyFormation, setup.AllyTroops, troops, allyAt.Y, new Vec(Fix.Zero, Fix.OneValue));
            if (setup.EnemyUnits != null && setup.EnemyUnits.Count > 0) MakeGrid(Enemy, setup.EnemyUnits, troops, enemyAt, new Vec(Fix.Zero, -Fix.OneValue));
            else MakeTeam(Enemy, setup.EnemyCorps, setup.EnemyCount, setup.EnemyFormation, setup.EnemyTroops, troops, enemyAt.Y, new Vec(Fix.Zero, -Fix.OneValue));
            if (Terrain != null) MakeGates(allyAt, enemyAt);
            if (Terrain != null)
                foreach (var a in Armies)
                {
                    // 못 가는 땅에 놓였으면 가까운 땅으로 (부대 가운데 기준)
                    var at = Terrain.Snap(a.Troop, a.Center);
                    if (!at.Equals(a.Center)) { a.Center = at; foreach (var s in a.Soldiers) s.Pos = a.SlotWorld(s.Slot); }
                }
            foreach (var a in Armies) a.Aim = a.Center;
            if (setup.AllyAdvance)
                foreach (var a in Armies) if (a.Team == Ally) OrderAttack(a.Id, Commander(Enemy).Id);
            // 부대 시작 행동: 위임이면 AI가 지휘, 진군이면 앞으로
            foreach (var a in Armies)
            {
                if (a.StartBehavior == CrowdUnitSpec.Behaviors.Delegate) a.Delegated = true;
                else if (a.StartBehavior == CrowdUnitSpec.Behaviors.Advance) OrderAdvance(a.Id);
            }
        }

        public CrowdArmy Commander(int team)
        {
            foreach (var a in Armies) if (a.Team == team && a.IsCommander) return a;
            return null;
        }

        public IEnumerable<CrowdArmy> TeamArmies(int team)
        {
            foreach (var a in Armies) if (a.Team == team) yield return a;
        }

        public int AliveCount(int team)
        {
            int n = 0;
            foreach (var a in Armies) if (a.Team == team) n += a.AliveCount;
            return n;
        }

        /// <summary>
        /// 3×3 칸 배치 (R-131): 칸(왼 · 가 · 오) → x = 전장 가운데 + (칸 − 1) × gridColPitch (보는 쪽 기준, 적도 같은 화면 왼쪽이 왼쪽),
        /// 줄(앞 · 가운데 · 뒤) → 앞줄이 frontY, 뒤로 갈수록 자기 쪽으로 gridRowPitch씩. 총대장은 하나 (없으면 첫 부대, 여럿이면 첫 것만).
        /// 병사 수는 1 ~ gridMaxCount.
        /// </summary>
        /// <summary>
        /// 성문 만들기 (R-140): gate 칸을 4방향으로 이어진 덩어리마다 성문 하나, 체력 gateHp, 지키는 편 = 배치 자리가 더 가까운 편. 칸을 닫는다.
        /// </summary>
        private void MakeGates(Vec allyAt, Vec enemyAt)
        {
            Terrain.ResetClosed();
            Terrain.GatePlanCost = Rules.GatePlanCost;
            int w = Terrain.Map.Width, h = Terrain.Map.Height;
            var seen = new bool[w * h];
            for (int i = 0; i < w * h; i++)
            {
                if (seen[i] || Terrain.Map.TerrainAt(i % w, i / w) != "gate") continue;
                var g = new CrowdGate { Hp = Rules.GateHp, MaxHp = Rules.GateHp };
                var stack = new Stack<int>();
                stack.Push(i);
                seen[i] = true;
                Fix sx = Fix.Zero, sy = Fix.Zero;
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    g.Cells.Add(c);
                    var cc = Terrain.CellCenter(c % w, c / w);
                    sx += cc.X; sy += cc.Y;
                    int x = c % w, y = c / w;
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int ni = ny * w + nx;
                        if (seen[ni] || Terrain.Map.TerrainAt(nx, ny) != "gate") continue;
                        seen[ni] = true;
                        stack.Push(ni);
                    }
                }
                g.Cells.Sort();
                g.Center = new Vec(sx / g.Cells.Count, sy / g.Cells.Count);
                g.Defender = Vec.DistanceSquared(allyAt, g.Center) < Vec.DistanceSquared(enemyAt, g.Center) ? Ally : Enemy;
                var toIn = (g.Defender == Ally ? allyAt : enemyAt) - g.Center;
                g.Inward = Fix.Max(toIn.Y, -toIn.Y) >= Fix.Max(toIn.X, -toIn.X)
                    ? new Vec(Fix.Zero, toIn.Y >= Fix.Zero ? Fix.OneValue : -Fix.OneValue)
                    : new Vec(toIn.X >= Fix.Zero ? Fix.OneValue : -Fix.OneValue, Fix.Zero);
                foreach (var c in g.Cells) Terrain.SetClosed(c, true);
                Gates.Add(g);
            }
            if (Gates.Count > 0) SiegeDefender = Gates[0].Defender;
        }

        /// <summary>그 부대가 성을 치는 쪽 (길 찾기에서 닫힌 성문을 지나갈 수 있는 것으로 봄).</summary>
        private bool Besieging(CrowdArmy a) => SiegeDefender >= 0 && a.Team != SiegeDefender;

        /// <summary>그 병사가 닿는 닫힌 성문 (칸 가운데까지 거리 ≤ reach), 없으면 -1.</summary>
        private int GateInReach(CrowdSoldier s, Fix reach, out Vec at)
        {
            at = s.Pos;
            int w = Terrain.Map.Width;
            Fix reach2 = reach * reach;
            for (int k = 0; k < Gates.Count; k++)
            {
                var g = Gates[k];
                if (g.Open) continue;
                foreach (var c in g.Cells)
                {
                    var p = Terrain.CellCenter(c % w, c / w);
                    if (Vec.DistanceSquared(p, s.Pos) <= reach2) { at = p; return k; }
                }
            }
            return -1;
        }

        /// <summary>성문을 친 한 방 (R-140): 체력 − gateHitDamage (장수는 × 2). 0이 되면 열린다 (칸을 열고 길 지도를 새로).</summary>
        private void HitGate(CrowdSoldier s, int gi, Vec at, CrowdArmy army)
        {
            var g = Gates[gi];
            // 문 막기 (지키는 병사가 많을수록 덜 들어감) · 충차 (R-145)
            Fix hit = Fix.FromInt(Rules.GateHitDamage * (s.IsGeneral ? 2 : 1)) * RamMult(army, g) * (Fix.FromInt(100) - GateBracePct(g)) / 100;
            g.Carry += hit;
            int whole = (int)g.Carry.FloorToLong();
            g.Hp -= whole;
            g.Carry -= Fix.FromInt(whole);
            g.LastHitTick = Tick;
            s.LastAttackTick = Tick;
            s.LastTargetId = -1;
            s.HitStructure = true;
            s.StrikeAt = at;
            s.Cooldown = Rules.AttackTicks + (Rules.AttackJitterTicks > 0 ? _rng.NextInt(Rules.AttackJitterTicks + 1) : 0);
            if (g.Hp <= 0)
            {
                g.Hp = 0;
                foreach (var c in g.Cells) Terrain.SetClosed(c, false);
                foreach (var a in Armies) { a.Flow = null; a.Route.Clear(); }
                if (BreachTick < 0) OnBreach(); // 공성 끝 → 야전 명령 (R-145)
            }
        }

        /// <summary>맵 시작 자리 (맵 칸) → 전장 자리 (칸 가운데).</summary>
        private Vec SpawnPoint(SpawnDef sp) => new Vec(Terrain.CellSize * sp.X + Terrain.CellSize / 2,
            Terrain.Flipped ? Terrain.CellSize * sp.Y + Terrain.CellSize / 2 : Terrain.CellSize * (Fix.FromInt(Terrain.Map.Height) - sp.Y) - Terrain.CellSize / 2);

        private void MakeGrid(int team, List<CrowdUnitSpec> units, IReadOnlyDictionary<string, TroopTypeDef> troops, Vec front, Vec facing)
        {
            Fix frontY = front.Y;
            var sorted = new List<CrowdUnitSpec>(units);
            sorted.Sort((a, b) => a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.Col.CompareTo(b.Col));
            int cmd = sorted.FindIndex(u => u.Commander);
            if (cmd < 0) cmd = 0;
            var mid = front.X;
            for (int k = 0; k < sorted.Count; k++)
            {
                var u = sorted[k];
                int row = System.Math.Max(0, System.Math.Min(2, u.Row)), col = System.Math.Max(0, System.Math.Min(2, u.Col));
                int count = System.Math.Max(1, System.Math.Min(Rules.GridMaxCount, u.Count));
                var form = CrowdFormations.IsKnown(u.Formation) ? u.Formation : CrowdFormations.Line;
                var a = MakeArmy(team, CrowdUnitSpec.RowIds[row] + "_" + CrowdUnitSpec.ColIds[col], k == cmd, form, count, facing, CrowdTroop.Of(u.Troop, troops), u.General);
                a.StartBehavior = u.Behavior;
                Fix back = Rules.GridRowPitch * row;
                a.Center = new Vec(mid + Rules.GridColPitch * (col - 1), team == Ally ? frontY - back : frontY + back);
                foreach (var s in a.Soldiers) s.Pos = a.SlotWorld(s.Slot);
            }
        }

        private void MakeTeam(int team, int corps, int count, string formation, string[] troopIds, IReadOnlyDictionary<string, TroopTypeDef> troops, Fix y, Vec facing)
        {
            corps = corps >= 3 ? 3 : 1;
            var form = CrowdFormations.IsKnown(formation) ? formation : CrowdFormations.Line;
            var made = new List<CrowdArmy>();
            for (int k = 0; k < corps; k++)
            {
                int share = count / corps + (k < count % corps ? 1 : 0);
                // 좌 · 우는 보는 쪽(우리 편) 기준: 적은 앞이 반대라 자기 오른쪽이 화면 왼쪽 → 이름을 뒤집어 화면 왼쪽 부대가 "좌" (2026-11-21 캡처: 우리 좌군이 적 좌군을 치러 전장을 가로질러 감)
                int pi = corps == 1 ? 1 : team == Ally ? k : 2 - k;
                string place = corps == 1 ? "center" : Places3[pi];
                string tid = troopIds != null && troopIds.Length > 0 ? troopIds[System.Math.Min(corps == 1 ? 0 : pi, troopIds.Length - 1)] : "infantry"; // 병종도 좌 · 중 · 우 이름 순서대로
                made.Add(MakeArmy(team, place, place == "center", form, share, facing, CrowdTroop.Of(tid, troops)));
            }
            // 좌 · 중 · 우로 나란히 (사이 CorpsGap), 편의 가운데가 전장 가운데. 오른쪽 = 부대의 Right (적은 좌우가 뒤집힘)
            Fix total = Fix.Zero;
            foreach (var a in made) total += a.HalfWidth * 2;
            total += Rules.CorpsGap * (made.Count - 1);
            Fix x = -total / 2;
            var mid = new Vec(Rules.FieldWidth / 2, y);
            foreach (var a in made)
            {
                Fix cx = x + a.HalfWidth;
                a.Center = mid + a.Right * cx;
                x += a.HalfWidth * 2 + Rules.CorpsGap;
                foreach (var s in a.Soldiers) s.Pos = a.SlotWorld(s.Slot);
            }
        }

        private Fix SpacingOf(CrowdTroop t) => t.Mounted ? Rules.Spacing * Rules.MountedRadius / Rules.SoldierRadius : Rules.Spacing;

        /// <summary>장수 성향 → 장수 정보 (crowd.json generalPresets, 모르면 standard).</summary>
        public CrowdGeneral GeneralFrom(string preset)
        {
            if (preset == null || !Rules.GeneralPresets.TryGetValue(preset, out var p)) { preset = "standard"; Rules.GeneralPresets.TryGetValue(preset, out p); }
            var g = new CrowdGeneral { Preset = preset };
            if (p == null) return g;
            g.Might = p.Might; g.Intellect = p.Intellect; g.Role = p.Role;
            foreach (var kv in p.Aptitude) g.Aptitude[kv.Key] = kv.Value;
            return g;
        }

        /// <summary>
        /// 장수 자리들 (R-138 · R-142 · R-144, 2026-11-23 사용자 "진형에서 선두 · 중위 · 후위"). 진형 안에서:
        /// front(맹장) = 만나기 전엔 앞줄 바로 안쪽(앞줄 − 간격 ÷ 2), 만나면 앞줄에서 generalFront칸 앞 · middle(지장) = 대열 한가운데((앞줄 + 뒷줄) ÷ 2) · rear(책사) = 대열 뒤 generalRear칸.
        /// 물러나 지휘 = 앞줄에서 generalPullBackRows줄 뒤 (대열 뒤 generalBack칸보다는 앞). 부상 = 대열 뒤 generalRear칸.
        /// 원진 · 종대는 성향과 관계없이 가운데 (원진은 원 중심, 종대는 대열 가운데). 원진은 부상도 가운데.
        /// </summary>
        /// <summary>
        /// 병사 자리 + 장수 빈자리 (R-144): 장수가 대열 안에 서면 (맹장 = 앞줄 가운데, 지장 · 종대 = 대열 가운데, 원진 = 중심) 자리를 하나 더 만들어
        /// 장수가 설 곳에 가장 가까운 자리(간격 × 0.75 안일 때만)를 비워 둔다 — 말 탄 장수가 병사 사이에 끼어 서로 밀어 대지 않게. 책사(후위)는 대열 밖이라 비우지 않음.
        /// </summary>
        private List<Vec> SoldierSlots(CrowdArmy a, string formation, int count, out Vec? hole)
        {
            hole = null;
            Fix sp = SpacingOf(a.Troop);
            bool inside = a.GeneralInfo.Role != "rear" || formation == CrowdFormations.Circle || formation == CrowdFormations.Column;
            if (!inside || count <= 0) return CrowdFormations.Slots(formation, count, sp);
            var more = CrowdFormations.Slots(formation, count + 1, sp);
            Fix front = Fix.FromInt(-999);
            foreach (var s in more) if (s.Y > front) front = s.Y;
            Fix back = CrowdFormations.BackOf(more);
            Vec want = formation == CrowdFormations.Circle ? Vec.Zero
                : formation != CrowdFormations.Column && a.GeneralInfo.Role == "front" ? new Vec(Fix.Zero, front)
                : new Vec(Fix.Zero, (front + back) / 2);
            int best = -1;
            Fix bestD = sp * Fix.FromRatio(3, 4);
            bestD = bestD * bestD;
            for (int i = 0; i < more.Count; i++)
            {
                Fix d2 = Vec.DistanceSquared(more[i], want);
                if (d2 < bestD || (d2 == bestD && best >= 0 && i < best)) { bestD = d2; best = i; }
            }
            if (best < 0) return CrowdFormations.Slots(formation, count, sp);
            hole = more[best];
            more.RemoveAt(best);
            return more;
        }

        private void PlaceGeneral(CrowdArmy a, List<Vec> slots, Vec? hole = null)
        {
            var r = Rules;
            Fix front = Fix.FromInt(-999);
            foreach (var s in slots) if (s.Y > front) front = s.Y;
            if (slots.Count == 0) front = Fix.Zero;
            Fix back = CrowdFormations.BackOf(slots);
            Fix sp = SpacingOf(a.Troop);
            var middle = new Vec(Fix.Zero, (front + back) / 2);
            switch (a.GeneralInfo.Role)
            {
                case "front":
                    a.GeneralBaseSlot = new Vec(Fix.Zero, front - sp / 2);
                    a.GeneralRoleSlot = new Vec(Fix.Zero, front + r.GeneralFront);
                    break;
                case "rear":
                    a.GeneralBaseSlot = a.GeneralRoleSlot = new Vec(Fix.Zero, back - r.GeneralRear);
                    break;
                default: // middle (예전 back도 여기로)
                    a.GeneralBaseSlot = a.GeneralRoleSlot = middle;
                    break;
            }
            a.GeneralCommandSlot = a.GeneralInfo.Role == "rear" ? new Vec(Fix.Zero, back - r.GeneralRear) : new Vec(Fix.Zero, Fix.Max(back - r.GeneralBack, front - sp * r.GeneralPullBackRows));
            a.GeneralWoundedSlot = new Vec(Fix.Zero, back - r.GeneralRear);
            if (a.Formation == CrowdFormations.Circle)
                a.GeneralBaseSlot = a.GeneralRoleSlot = a.GeneralCommandSlot = a.GeneralWoundedSlot = hole ?? Vec.Zero;
            else if (a.Formation == CrowdFormations.Column)
                a.GeneralBaseSlot = a.GeneralRoleSlot = a.GeneralCommandSlot = hole ?? middle;
            else if (hole.HasValue)
            {
                // 비워 둔 자리에 선다: 맹장은 만나기 전 (앞줄 가운데), 지장은 늘
                if (a.GeneralInfo.Role == "front") a.GeneralBaseSlot = hole.Value;
                else if (a.GeneralInfo.Role != "rear") a.GeneralBaseSlot = a.GeneralRoleSlot = hole.Value;
            }
            if (a.General != null) SetGeneralSlot(a);
        }

        /// <summary>그 부대가 스킬을 다시 쓰기까지 틱 (R-144): skillGap × (100 − (지력 − 50) × skillGapIntellectPct)%, skillGapMinPct ~ skillGapMaxPct.</summary>
        public int SkillGapTicksFor(CrowdArmy a)
        {
            var r = Rules;
            Fix pct = Fix.Clamp(Fix.FromInt(100) - Fix.FromInt(a.GeneralInfo.Intellect - 50) * r.SkillGapIntellectPct, r.SkillGapMinPct, r.SkillGapMaxPct);
            return System.Math.Max(1, (int)(Fix.FromInt(r.SkillGapTicks) * pct / 100).RoundToLong());
        }

        /// <summary>지금 장수가 설 자리: 부상 → 물러나 지휘 → 만났으면 성향 자리 → 아니면 장수 자리.</summary>
        /// 공성으로 성문 · 사다리 앞에 선 동안은 맹장도 앞으로 나서지 않음 (R-145 — 성문 앞에서 돌 · 화살을 다 맞음).
        private static Vec GeneralSlotNow(CrowdArmy a) =>
            a.GeneralPulledBack ? a.GeneralCommandSlot
            : a.GeneralEngaged && a.SiegeTask == null ? a.GeneralRoleSlot : a.GeneralBaseSlot;

        /// <summary>장수 자리를 바꾼다 (바뀌면 멈춤을 풀어 새 자리로 가게).</summary>
        private static void SetGeneralSlot(CrowdArmy a)
        {
            var g = a.General;
            var slot = GeneralSlotNow(a);
            if (g.Slot.Equals(slot)) return;
            g.Slot = slot;
            g.Settled = false; g.BestDist = Fix.FromInt(9999); g.StuckTicks = 0;
        }

        /// <summary>
        /// 장수 호위 (R-139): 장수가 이번 틱 맞은 피해의 generalGuardSharePct%를, 장수에게서 generalGuardRange칸 안의 자기 병사가
        /// generalGuardMin명 이상이면 가장 가까운 병사들(최대 3명, 같으면 id)이 나눠 받는다 — 병사가 많으면 장수가 잘 안 죽고, 부대가 녹으면 위험해짐.
        /// </summary>
        private void GuardGenerals()
        {
            var r = Rules;
            Fix range2 = r.GeneralGuardRange * r.GeneralGuardRange;
            foreach (var a in Armies)
            {
                var g = a.General;
                if (!g.Alive || _damage[g.Id] <= 0 || a.GeneralEscaping) continue;
                var guards = new List<(Fix d, CrowdSoldier s)>();
                foreach (var s in a.Soldiers)
                {
                    if (!s.Alive || s.IsGeneral) continue;
                    Fix d2 = Vec.DistanceSquared(s.Pos, g.Pos);
                    if (d2 <= range2) guards.Add((d2, s));
                }
                if (guards.Count < r.GeneralGuardMin) continue;
                guards.Sort((p, q) => { int c = p.d.CompareTo(q.d); return c != 0 ? c : p.s.Id.CompareTo(q.s.Id); });
                int share = _damage[g.Id] * r.GeneralGuardSharePct / 100;
                if (share <= 0) continue;
                _damage[g.Id] -= share;
                int n = System.Math.Min(3, guards.Count);
                for (int k = 0; k < n; k++) _damage[guards[k].s.Id] += share / n + (k < share % n ? 1 : 0);
            }
        }

        /// <summary>높이 차 (단, R-143): a 자리 높이 − b 자리 높이 (맵 heights ÷ 3 — 성벽 위 · 계단 높이도 들어 있음), ±heightMaxLevels. 지형 없으면 0.</summary>
        public Fix HeightDiff(Vec a, Vec b)
        {
            if (Terrain == null) return Fix.Zero;
            Fix d = Terrain.LevelAt(a) - Terrain.LevelAt(b);
            return Fix.Clamp(d, -Rules.HeightMaxLevels, Rules.HeightMaxLevels);
        }

        /// <summary>궁병 사거리 (R-143): 기본 × (1 + max(0, 높이 차) × heightRangePctPerLevel%).</summary>
        public Fix ShotRange(CrowdSoldier s, CrowdSoldier e, Fix baseRange)
        {
            Fix d = HeightDiff(s.Pos, e.Pos);
            return d > Fix.Zero ? baseRange * (Fix.OneValue + d * Rules.HeightRangePctPerLevel / 100) : baseRange;
        }

        /// <summary>
        /// 비탈 걸음 (R-143): 이번 걸음 방향으로 한 칸 앞이 s단 높으면 걸음 × (100 − s × uphillSlowPctPerLevel)%, 낮으면 × (100 + |s| × downhillFastPctPerLevel)%,
        /// slopeMinPct ~ slopeMaxPct (계단도 높이 층이 2 → 4 → 6으로 오르므로 느려짐).
        /// </summary>
        private Vec Slope(CrowdSoldier s, Vec p)
        {
            var d = p - s.Pos;
            Fix pct = SlopePct(s.Pos, d);
            return pct == Fix.FromInt(100) ? p : s.Pos + d * (pct / 100);
        }

        /// <summary>from에서 dir 쪽으로 걸을 때 비탈 걸음 배수 % (R-143, 높이 층이 없으면 100).</summary>
        public Fix SlopePct(Vec from, Vec dir)
        {
            var r = Rules;
            Fix len = dir.Length;
            if (Terrain == null || !Terrain.Map.HasHeights || len <= Fix.Zero) return Fix.FromInt(100);
            var ahead = from + dir * (Terrain.CellSize / len);
            Fix rise = Terrain.LevelAt(ahead) - Terrain.LevelAt(from);
            if (rise == Fix.Zero) return Fix.FromInt(100);
            Fix pct = rise > Fix.Zero ? Fix.FromInt(100) - rise * r.UphillSlowPctPerLevel : Fix.FromInt(100) - rise * r.DownhillFastPctPerLevel;
            return Fix.Clamp(pct, r.SlopeMinPct, r.SlopeMaxPct);
        }

        /// <summary>진형 특성 배수 % (표에 없으면 100).</summary>
        private static Fix FormationPct(IReadOnlyDictionary<string, Fix> table, string formation) =>
            table.TryGetValue(formation, out var v) ? v : Fix.FromInt(100);

        /// <summary>진형 상성 %p (R-141, crowd.json formationCounterPp "공격진형>맞는진형" — 실제 시험하며 채움, 지금은 비어 있음).</summary>
        public Fix FormationCounterPp(CrowdArmy atk, CrowdArmy def) =>
            Rules.FormationCounterPp.TryGetValue(atk.Formation + ">" + def.Formation, out var pp) ? pp : Fix.Zero;

        /// <summary>병종 적성 %p (R-138): 장수의 그 병종 적성 등급 → aptitudePp (S · A · B · C).</summary>
        public Fix AptitudePp(CrowdArmy a) =>
            Rules.AptitudePp.TryGetValue(a.GeneralInfo.AptitudeOf(a.Troop.BaseId), out var pp) ? pp : Fix.Zero;

        private CrowdArmy MakeArmy(int team, string place, bool commander, string formation, int count, Vec facing, CrowdTroop troop, string general = "standard")
        {
            var a = new CrowdArmy { Id = Armies.Count, Team = team, Place = place, IsCommander = commander, Formation = formation, Facing = facing, StartCount = count, Troop = troop };
            a.GeneralInfo = GeneralFrom(general);
            a.SpeedFactor = troop.Speed / Rules.BaseSpeedStat;
            a.Morale = Rules.MoraleStart;
            var slots = SoldierSlots(a, formation, count, out var hole);
            Fix radius = troop.Mounted ? Rules.MountedRadius : Rules.SoldierRadius;
            foreach (var slot in slots)
            {
                var s = new CrowdSoldier { Id = Soldiers.Count, Team = team, Army = a.Id, Slot = slot, Hp = Rules.SoldierHp, MaxHp = Rules.SoldierHp, Radius = radius };
                s.Cooldown = _rng.NextInt(Rules.AttackTicks);
                a.Soldiers.Add(s);
                Soldiers.Add(s);
            }
            // 장수 체력 = generalHp × 무력 ÷ generalHpMight (무력이 높을수록 둘러싸여도 오래 버팀, R-138)
            int ghp = System.Math.Max(1, (int)(Fix.FromInt(Rules.GeneralHp) * Fix.FromInt(a.GeneralInfo.Might) / Rules.GeneralHpMight).RoundToLong());
            var g = new CrowdSoldier { Id = Soldiers.Count, Team = team, Army = a.Id, IsGeneral = true, Hp = ghp, MaxHp = ghp, Radius = Rules.MountedRadius };
            a.General = g;
            PlaceGeneral(a, slots, hole);
            a.Soldiers.Add(g);
            Soldiers.Add(g);
            Measure(a, slots);
            Armies.Add(a);
            return a;
        }

        private static void Measure(CrowdArmy a, List<Vec> slots)
        {
            Fix minX = Fix.Zero, maxX = Fix.Zero, minY = Fix.Zero, maxY = Fix.Zero;
            foreach (var s in slots)
            {
                if (s.X < minX) minX = s.X; if (s.X > maxX) maxX = s.X;
                if (s.Y < minY) minY = s.Y; if (s.Y > maxY) maxY = s.Y;
            }
            a.HalfWidth = (maxX - minX) / 2;
            a.HalfDepth = (maxY - minY) / 2;
        }

        // ---- 명령 ----

        /// <summary>자동 행군 켜기 · 끄기 (R-136). 끄면 숲 · 좁은 길에서도 진형을 지킨다.</summary>
        public void SetAutoMarch(int army, bool on)
        {
            if (army < 0 || army >= Armies.Count) return;
            Armies[army].AutoMarch = on;
            if (!on && Armies[army].Marching) { Armies[army].Marching = false; if (Armies[army].Leading) SetFormation(army, Armies[army].Formation); }
        }

        /// <summary>
        /// 대열 바꾸기: 새 모양 자리를 병사들에게 다시 나눠 준다 (지금 위치의 좌우 순서를 지켜 서로 덜 엇갈리게).
        /// 병사는 다음 틱부터 새 자리로 걸어간다 (맞붙은 병사는 싸움을 계속).
        /// </summary>
        public void SetFormation(int army, string formation)
        {
            if (!CrowdFormations.IsKnown(formation) || IsOver || army < 0 || army >= Armies.Count) return;
            var a = Armies[army];
            if (!a.Leading) return;
            a.Formation = formation;
            var alive = new List<CrowdSoldier>();
            foreach (var s in a.Soldiers) if (s.Alive && !s.IsGeneral) alive.Add(s);
            var slots = SoldierSlots(a, formation, alive.Count, out var hole);
            var right = a.Right;
            alive.Sort((p, q) =>
            {
                var dp = p.Pos - a.Center; var dq = q.Pos - a.Center;
                int c = dp.Dot(right).CompareTo(dq.Dot(right));
                if (c != 0) return c;
                c = dp.Dot(a.Facing).CompareTo(dq.Dot(a.Facing));
                return c != 0 ? c : p.Id.CompareTo(q.Id);
            });
            slots.Sort((p, q) => { int c = p.X.CompareTo(q.X); return c != 0 ? c : p.Y.CompareTo(q.Y); });
            for (int i = 0; i < alive.Count; i++)
            {
                // 누르자마자 새 자리로 (R-142): 대기 중 멈춘 병사도 멈춤을 푼다 (풀지 않으면 이동 명령 전까지 옛 진형에 머묾)
                var s = alive[i];
                s.Slot = slots[i];
                s.Settled = false; s.BestDist = Fix.FromInt(9999); s.StuckTicks = 0;
            }
            PlaceGeneral(a, slots, hole);
            Measure(a, slots);
        }

        public void OrderHold(int army)
        {
            if (IsOver || army < 0 || army >= Armies.Count || !Armies[army].Leading) return;
            var a = Armies[army];
            ClearSiege(a); // 다른 명령이면 공성 명령을 지움 (R-145)
            a.Order = CrowdOrders.Hold; a.OrderTarget = -1; a.Charging = false;
            a.Waypoints.Clear();
            a.Melee = false; a.PathThenAttack = -1;
        }

        private Vec InField(Vec p) => new Vec(Fix.Clamp(p.X, Fix.Zero, Rules.FieldWidth), Fix.Clamp(p.Y, Fix.Zero, Rules.FieldDepth));

        /// <summary>그 부대가 못 가는 땅이면 가장 가까운 갈 수 있는 땅 (R-133).</summary>
        private Vec Open(CrowdArmy a, Vec p) => Terrain != null ? Terrain.Snap(a.Troop, p) : p;

        /// <summary>그 자리 지형의 걸음 배수 (평지 · 지형 없음 = 1).</summary>
        private Fix TerrainSpeed(CrowdTroop t, Vec p) => Terrain != null ? Terrain.SpeedFactor(t, p) : Fix.OneValue;

        /// <summary>치는 병사가 선 지형의 병종 보정 %p (R-133, 기존 전투 R-17과 같음).</summary>
        private Fix TerrainPp(CrowdTroop t, Vec p) => Terrain != null ? Terrain.CombatPp(t, p) : Fix.Zero;

        /// <summary>
        /// 부대가 이번 틱 향할 곳 (R-133): 목표까지 곧은 길에 못 가는 땅이 없으면 목표, 있으면 맵 칸 길을 찾아 곧게 보이는 가장 먼 길 점.
        /// 길은 목표 칸이 바뀌거나 40틱마다 다시 찾는다. 길이 없으면 목표 (막혀 멈춘다).
        /// </summary>
        private Vec Steer(CrowdArmy a, Vec goal)
        {
            if (Terrain == null || Terrain.Clear(a.Troop, a.Center, goal)) { a.Route.Clear(); return goal; }
            int gc = Terrain.CellIndex(goal);
            if (a.Route.Count == 0 || a.RouteGoal != gc || Tick - a.RouteTick >= 40)
            {
                a.Route.Clear();
                a.Route.AddRange(Terrain.FindPath(a.Troop, a.Center, goal, Besieging(a)));
                a.RouteGoal = gc;
                a.RouteTick = Tick;
            }
            if (a.Route.Count == 0) return goal;
            for (int k = a.Route.Count - 1; k > 0; k--)
                if (Terrain.Clear(a.Troop, a.Center, a.Route[k])) { a.Route.RemoveRange(0, k); break; }
            if (a.Route.Count > 1 && Vec.DistanceSquared(a.Center, a.Route[0]) <= Fix.OneValue) a.Route.RemoveAt(0);
            return a.Route[0];
        }

        /// <summary>
        /// 땅 한 곳으로 이동 (가면서 만나는 적과는 싸운다, 도착하면 제자리).
        /// append = true면 예약 (R-129): 지금 이동 중이면 그 뒤 경유 지점으로 붙이고, 아니면 바로 간다.
        /// </summary>
        public void OrderMove(int army, Vec point, bool append = false, bool march = false)
        {
            if (IsOver || army < 0 || army >= Armies.Count || !Armies[army].Leading) return;
            var a = Armies[army];
            ClearSiege(a); // 다른 명령이면 공성 명령을 지움 (R-145)
            point = Open(a, InField(point));
            if (append && a.Order == CrowdOrders.Move) { if (a.Waypoints.Count < MaxWaypoints) a.Waypoints.Add(point); return; }
            a.MarchMove = march; // [행군 이동] (R-137): 이번 이동만 행군 대열
            a.Order = CrowdOrders.Move;
            a.OrderPoint = point;
            a.Waypoints.Clear();
            a.OrderTarget = -1;
            a.Charging = false;
            a.Melee = false; a.PathThenAttack = -1;
        }

        /// <summary>한 경로에 넣을 수 있는 점 수 (손으로 그린 길, R-132).</summary>
        public const int MaxPathPoints = 16;

        /// <summary>
        /// 경로 이동 (R-132, 손으로 그린 길): 점들을 차례로 지나간다 (MaxPathPoints까지). thenAttack이 적 부대면 끝에 닿은 뒤 그 부대를 공격.
        /// </summary>
        public void OrderPath(int army, IReadOnlyList<Vec> points, int thenAttack = -1)
        {
            if (IsOver || army < 0 || army >= Armies.Count || !Armies[army].Leading || points == null || points.Count == 0) return;
            var a = Armies[army];
            ClearSiege(a); // 다른 명령이면 공성 명령을 지움 (R-145)
            OrderMove(army, points[0]);
            for (int k = 1; k < points.Count && k < MaxPathPoints; k++) a.Waypoints.Add(Open(a, InField(points[k])));
            if (thenAttack >= 0 && thenAttack < Armies.Count && Armies[thenAttack].Team != a.Team) a.PathThenAttack = thenAttack;
        }

        /// <summary>진군 (R-132): 진형을 지킨 채 적 쪽 끝으로 곧게 (만나는 적과 싸움, 가까워지면 돌격).</summary>
        public void OrderAdvance(int army)
        {
            if (IsOver || army < 0 || army >= Armies.Count || !Armies[army].Leading) return;
            var a = Armies[army];
            ClearSiege(a); // 다른 명령이면 공성 명령을 지움 (R-145)
            OrderHold(army);
            a.Order = CrowdOrders.Advance;
        }

        /// <summary>후퇴 (R-132): 자기 쪽 끝(retreatMargin 안쪽)으로 대열 그대로 물러난다. 물러나는 동안은 등을 보인 것으로 친다 (앞에서 맞아도 뒤).</summary>
        public void OrderRetreat(int army)
        {
            if (IsOver || army < 0 || army >= Armies.Count || !Armies[army].Leading) return;
            var a = Armies[army];
            ClearSiege(a); // 다른 명령이면 공성 명령을 지움 (R-145)
            OrderHold(army);
            a.Order = CrowdOrders.Retreat;
        }

        /// <summary>가장 가까운 서 있는 적 부대 (없으면 -1).</summary>
        public int NearestEnemyArmy(int army)
        {
            if (army < 0 || army >= Armies.Count) return -1;
            var a = Armies[army];
            int best = -1;
            Fix bestD = Fix.Zero;
            var me = a.Centroid();
            foreach (var o in Armies)
            {
                if (o.Team == a.Team || !Standing(o)) continue;
                Fix d = Vec.DistanceSquared(o.Centroid(), me);
                if (best < 0 || d < bestD) { best = o.Id; bestD = d; }
            }
            return best;
        }

        /// <summary>근접 공격 (R-132): 가장 가까운 적 부대에게 붙는다 (궁병도 활 대신 칼). 적이 없으면 false.</summary>
        public bool OrderAttackNearest(int army)
        {
            int t = NearestEnemyArmy(army);
            if (t < 0) return false;
            OrderAttack(army, t);
            if (Armies[army].Order != CrowdOrders.Attack) return false;
            Armies[army].Melee = true;
            return true;
        }

        /// <summary>위임 켜기 · 끄기 (R-132). 켜면 다음 AI 차례부터 가까운 적을 공격 · 스킬을 쓴다.</summary>
        public void SetDelegated(int army, bool on)
        {
            if (army < 0 || army >= Armies.Count) return;
            Armies[army].Delegated = on;
        }

        /// <summary>한 부대가 예약해 둘 수 있는 경유 지점 수.</summary>
        public const int MaxWaypoints = 6;

        /// <summary>적 부대 공격 (그 부대 쪽으로 가서, 가까워지면 돌격 — 궁병은 사거리에서 멈춰 쏜다).</summary>
        public void OrderAttack(int army, int target)
        {
            if (IsOver || army < 0 || army >= Armies.Count || target < 0 || target >= Armies.Count) return;
            var a = Armies[army];
            ClearSiege(a); // 다른 명령이면 공성 명령을 지움 (R-145)
            if (!a.Leading || Armies[target].Team == a.Team) return;
            a.Order = CrowdOrders.Attack;
            a.OrderTarget = target;
            a.Waypoints.Clear();
            a.Melee = false; a.PathThenAttack = -1;
        }

        /// <summary>활을 쏘는 부대 (원거리 병종이고 근접 공격 중이 아님).</summary>
        public static bool Shooting(CrowdArmy a) => a.Troop.Ranged && !a.Melee;

        /// <summary>AI가 움직이는 부대: 적은 전진 설정이면, 우리는 위임이면 (R-132).</summary>
        private bool AiControls(CrowdArmy a) => a.Team == Enemy ? _enemyAdvance || a.Delegated : a.Delegated;

        /// <summary>그 부대가 아직 싸울 수 있음 (병사나 장수가 남음).</summary>
        public bool Standing(CrowdArmy a) => a.General.Alive || a.AliveCount > 0;

        /// <summary>원거리 병종의 사거리 (칸).</summary>
        public Fix RangeOf(CrowdTroop t) => t.AttackRange * Rules.RangeScale;

        // ---- 피해 ----

        /// <summary>
        /// 맞는 부대 기준 어느 쪽에서 맞았나 (R-128): 0 앞 · 1 왼쪽 · 2 오른쪽 · 3 뒤.
        /// 부대 가운데(병사 평균)에서 때린 자리 방향과 부대 앞 방향의 내적이 frontDot 이상이면 앞, rearDot 이하면 뒤, 아니면 옆.
        /// (병사 자리 기준이면 V자 팔 병사가 안쪽 적에게 맞는 것도 옆이 되어, 감싼 쪽이 오히려 포위당한 셈이 됨)
        /// </summary>
        public int SideOf(CrowdArmy defender, Vec defenderPos, Vec attackerPos)
        {
            var d = (attackerPos - defenderPos).Normalized();
            Fix dot = d.Dot(defender.Facing);
            if (defender.Order == CrowdOrders.Retreat) dot = -dot; // 후퇴 중 = 적에게 등 (R-132): 앞에서 친 것이 뒤
            if (dot >= Rules.FrontDot) return 0;
            if (dot <= Rules.RearDot) return 3;
            return d.Dot(defender.Right) < Fix.Zero ? 1 : 2;
        }

        private Fix SidePp(int side) => side == 3 ? Rules.RearPp : side == 0 ? Fix.Zero : Rules.FlankPp;

        /// <summary>맞은 쪽을 적어 둔다 (사기: 옆 · 뒤에서 맞는 동안 깎임 · 포위 판정).</summary>
        private void NoteHit(CrowdArmy defender, int side)
        {
            defender.SideHitTick[side] = Tick;
            defender.LastHitTick = Tick;
        }

        /// <summary>
        /// 한 번 친(쏜) 피해: hitDamage × (1 + (공격 − 방어) × statPpPerPoint% + 상성%), 1 이상.
        /// 상성 = troopCounters.json에서 공격 쪽 태그 → 방어 쪽 태그 규칙의 합.
        /// </summary>
        public int HitDamage(CrowdTroop atk, CrowdTroop def) => HitDamage(atk, def, Fix.Zero);

        /// <param name="extraPp">옆 · 뒤에서 친 피해 더 (R-128).</param>
        public int HitDamage(CrowdTroop atk, CrowdTroop def, Fix extraPp)
        {
            Fix pp = (atk.Attack - def.Defense) * Rules.StatPpPerPoint + extraPp;
            foreach (var c in _counters)
                if (atk.Has(c.AttackerTag) && def.Has(c.DefenderTag)) pp += c.Pp;
            Fix mult = Fix.OneValue + pp / 100;
            if (mult < Fix.FromRatio(1, 10)) mult = Fix.FromRatio(1, 10);
            long d = (Fix.FromInt(Rules.HitDamage) * mult).RoundToLong();
            return d < 1 ? 1 : (int)d;
        }

        // ---- 한 틱 ----

        public void Step()
        {
            if (IsOver) return;
            Tick++;
            var r = Rules;
            Fix dt = r.TickSeconds;
            Fix engage2 = r.EngageRange * r.EngageRange;

            int n = Soldiers.Count;
            if (_next.Length < n) { _next = new Vec[n]; _damage = new int[n]; _knock = new Vec[n]; _goal = new Vec[n]; }
            BuildGrid();
            ImpactsThisTick = 0;

            // 1) 부대: 적 AI 명령, 맞붙은 비율, 돌격 여부, 가운데 옮기기
            foreach (var a in Armies) a.TickCentroid = a.Centroid();
            if (Tick % 10 == 1) AiThink();
            var engaged = new int[Armies.Count];
            for (int i = 0; i < n; i++)
            {
                var s = Soldiers[i];
                if (s.Alive && s.LastAttackTick >= Tick - r.AttackTicks * 2 && !Shooting(Armies[s.Army])) engaged[s.Army]++;
            }
            foreach (var a in Armies) MoveArmy(a, engaged[a.Id]);
            foreach (var a in Armies) UpdateMarch(a);

            // 2) 병사마다 갈 곳 · 칠 대상 (id 순서로 계산, 반영은 한꺼번에)
            for (int i = 0; i < n; i++) { _next[i] = Soldiers[i].Pos; _damage[i] = 0; _knock[i] = Vec.Zero; _goal[i] = Soldiers[i].Pos; }
            for (int i = 0; i < n; i++)
            {
                var s = Soldiers[i];
                if (!s.Alive) continue;
                if (s.Cooldown > 0) s.Cooldown--;
                var army = Armies[s.Army];
                var troop = army.Troop;
                Fix ground = TerrainSpeed(troop, s.Pos); // 지형 걸음 (R-133): 숲에 든 기병은 느려 대열이 흐트러진다
                Fix walkStep = r.MoveSpeed * army.SpeedFactor * MoveMult(army) * ground * dt;
                Fix stepLen = army.Charging ? r.ChargeSpeed * army.SpeedFactor * MoveMult(army) * ground * dt : walkStep;
                if (army.Wavering) { walkStep = walkStep * r.WaverSpeed; stepLen = stepLen * r.WaverSpeed; }
                if (army.Routed)
                {
                    // 무너짐: 자기 쪽 가장자리로 도망 (가까운 적에게서 멀어지는 쪽으로 조금 비켜), 치지 않는다
                    var away = new Vec(Fix.Zero, s.Team == Ally ? -Fix.OneValue : Fix.OneValue);
                    int near = NearestEnemy(s, engage2, r.EngageRange);
                    if (near >= 0) away = (away + (s.Pos - Soldiers[near].Pos).Normalized()).Normalized();
                    _next[i] = s.Pos + away * (r.FleeSpeed * army.SpeedFactor * ground * dt) + Separation(s);
                    continue;
                }
                if (Ladders.Count > 0 && ClimbStep(s, army, i)) continue; // 사다리 오르는 중 (R-145)
                if (s.IsGeneral && army.GeneralEscaping)
                {
                    // 장수 물러나기 (R-138): 자기 쪽 끝으로 말을 달린다 (무력이 높을수록 빠르게 — 적 사이를 뚫고), 싸우지 않음
                    var back = new Vec(Fix.Zero, s.Team == Ally ? -Fix.OneValue : Fix.OneValue);
                    int near = NearestEnemy(s, engage2, r.EngageRange);
                    if (near >= 0) back = (back + (s.Pos - Soldiers[near].Pos).Normalized()).Normalized();
                    Fix run = r.FleeSpeed * r.GeneralEscapeSpeed * (Fix.OneValue + Fix.FromInt(army.GeneralInfo.Might) / 200) * ground * dt;
                    _next[i] = s.Pos + back * run + Separation(s);
                    continue;
                }
                Vec want;
                // 장수: 무력이 generalFightMight 이상이면 직접 싸운다 (맹장), 아니면 싸우지 않고 자리만 (책사)
                int foe;
                if (s.IsGeneral && army.GeneralInfo.Might < r.GeneralFightMight) foe = -1;
                else if (s.IsGeneral && army.GeneralPulledBack)
                {
                    // 물러나 지휘 · 부상 (R-142): 먼저 덤비지 않고 자리로 — 바로 붙은 적만 맞받는다
                    Fix touch = s.Radius + r.MountedRadius + r.AttackReach;
                    foe = NearestEnemy(s, touch * touch, touch);
                }
                else if (s.IsGeneral && army.GeneralInfo.Role != "front" && army.Leading)
                {
                    // 지장 (R-144): 대열 가운데서 자리 둘레 generalRoamRange칸 안 적에게 칠 수 있을 때만 다가가 한 대, 치고 나면 자리로 (대열 밖까지 쫓지 않음)
                    foe = s.Cooldown == 0 ? NearestEnemy(s, r.GeneralRoamRange * r.GeneralRoamRange, r.GeneralRoamRange) : -1;
                    if (foe >= 0 && (Soldiers[foe].Pos - army.SlotWorld(s.Slot)).Length > r.GeneralRoamRange) foe = -1;
                }
                else foe = NearestEnemy(s, engage2, r.EngageRange);
                // 성벽 · 닫힌 성문 너머 적과는 맞붙지 않음 (R-145, 2026-11-23 캡처 47: 문 막는 적 보병 · 창병이 닫힌 문 너머로 쳐서 충차 부대가 녹음) — 화살 · 돌은 넘어감
                if (foe >= 0 && Gates.Count > 0 && !Terrain.Clear(troop, s.Pos, Soldiers[foe].Pos)) foe = -1;
                // 성벽 위 지키는 보병 · 창병: 아래 적에게 돌 (R-145)
                if (foe < 0 && !troop.Ranged && s.Team == SiegeDefender && SiegeActive && TryDropStone(s))
                {
                    _next[i] = s.Pos + ToSlot(army, s, walkStep) + Separation(s);
                    continue;
                }
                // 궁병: 맞붙을 만큼 가깝지 않으면 사거리 안의 적에게 쏜다 (자리를 지킨 채)
                if (!s.IsGeneral && Shooting(army) && foe < 0)
                {
                    if (s.Cooldown == 0)
                    {
                        // 높은 곳 궁병은 더 멀리 (R-143): 내 높이로 닿을 수 있는 가장 먼 거리 안에서 찾고, 그 적과의 높이 차로 실제 사거리를 다시 봄
                        Fix range = RangeOf(troop);
                        Fix far = range * (Fix.OneValue + r.HeightMaxLevels * r.HeightRangePctPerLevel / 100);
                        int target = NearestEnemy(s, far * far, far);
                        if (target >= 0)
                        {
                            Fix reachNow = ShotRange(s, Soldiers[target], range);
                            if ((Soldiers[target].Pos - s.Pos).LengthSquared <= reachNow * reachNow) Shoot(s, target, troop);
                        }
                    }
                    _next[i] = s.Pos + ToSlot(army, s, walkStep) + Separation(s);
                    continue;
                }
                if (foe < 0 && Gates.Count > 0 && Besieging(army) && !Shooting(army))
                {
                    // 성문 치기 (R-140): 닿는 닫힌 성문이 있으면 서서 친다
                    int gi = GateInReach(s, s.Radius + Terrain.CellSize * Fix.FromRatio(3, 4) + r.AttackReach, out var gat);
                    if (gi >= 0)
                    {
                        if (s.Cooldown == 0) HitGate(s, gi, gat, army);
                        _next[i] = s.Pos + Separation(s);
                        continue;
                    }
                }
                if (foe >= 0)
                {
                    var e = Soldiers[foe];
                    _goal[i] = e.Pos;
                    var d = e.Pos - s.Pos;
                    Fix d2 = d.LengthSquared;
                    Fix reach = s.Radius + e.Radius + r.AttackReach + (troop.AttackRange > Fix.OneValue && !troop.Ranged ? (troop.AttackRange - Fix.OneValue) * r.PolearmReach : Fix.Zero);
                    if (d2 <= reach * reach)
                    {
                        // 닿았다: 서서 친다. 물러나 지휘 · 부상 장수는 치면서도 자리로 물러난다 (R-142 — 앞줄에 붙잡혀 계속 싸우지 않게)
                        want = s.IsGeneral && army.GeneralPulledBack ? ToSlot(army, s, walkStep) : Vec.Zero;
                        if (s.Cooldown == 0) Strike(s, e, d, d2, army, walkStep);
                    }
                    else if (army.Order == CrowdOrders.Retreat || (s.IsGeneral && army.GeneralPulledBack)) want = ToSlot(army, s, stepLen); // 후퇴 · 지휘 장수: 닿은 적만 치고 쫓지 않는다
                    else
                    {
                        Fix len = Fix.Sqrt(d2);
                        Fix go = Fix.Min(stepLen, len - reach + Fix.FromRatio(1, 100));
                        want = len > Fix.Zero ? d * (go / len) : Vec.Zero;
                    }
                }
                else want = ToSlot(army, s, stepLen);
                _next[i] = s.Pos + want + Separation(s);
            }
            for (int i = 0; i < n; i++) if (!_knock[i].Equals(Vec.Zero)) _next[i] = _next[i] + _knock[i];

            // 2.4) 스킬: 적 AI 스킬, 화살비 떨어짐, 끝난 효과
            if (Tick % 10 == 5) AiSkills();
            StepSkills();

            // 2.5) 다음 위치끼리 겹친 것을 두 번 풀어 준다 (뒤에서 미는 병사가 앞 병사 속으로 파고들지 않게)
            for (int pass = 0; pass < RelaxPasses; pass++) Relax();
            if (Obstacles.Count > 0)
            {
                // 나무 기둥 · 바위 (R-134): 들어간 만큼 밖으로, 막혀 멈추지 않게 옆으로 비켜. 두 그루 사이에 끼어 그래도 안이면 이번 틱은 제자리
                PushOutOfObstacles();
                PushOutOfObstacles();
                for (int i = 0; i < n; i++)
                    if (Soldiers[i].Alive && InsideObstacle(Soldiers[i], _next[i]) && !InsideObstacle(Soldiers[i], Soldiers[i].Pos)) _next[i] = Soldiers[i].Pos;
            }

            // 2.7) 화살: 닿는 틱이면 대상이 살아 있을 때 맞을 수 있다 (시드 난수)
            for (int k = 0; k < Arrows.Count; k++)
            {
                var ar = Arrows[k];
                if (ar.ArriveTick > Tick) continue;
                if (ar.TargetId < 0) { Arrows.RemoveAt(k); k--; continue; } // 화살비 화살 (보이기만, 피해는 StepSkills)
                var t = Soldiers[ar.TargetId];
                Fix hitChance = ar.IsStone ? r.StoneHitChance : r.ArrowHitChance * FormationPct(r.FormationArrowHitPct, Armies[t.Army].Formation) / 100 * ar.HitPct / 100; // 산개는 덜 맞음 (R-141), 높이 (R-143), 돌 (R-145)
                if (SiegeActive && UnderRamRoof(t)) hitChance = hitChance * (Fix.FromInt(100) - r.RamCoverPct) / 100; // 충차 지붕 밑 (R-145)
                if (t.Alive && _rng.Chance(hitChance))
                {
                    var ta = Armies[t.Army];
                    // 성벽 위에서 쏜 화살 · 돌은 앞에서 맞은 것으로 (R-145 — 성문 앞 부대가 양옆 성벽에서 맞아 옆 · 포위로 사기가 무너짐, 캡처 47)
                    int side = Terrain != null && Terrain.IsRaised(ar.From) ? 0 : SideOf(ta, ta.TickCentroid, ar.From);
                    _damage[t.Id] += side == 0 ? ar.Damage : ar.Damage + System.Math.Max(0, (int)(Fix.FromInt(ar.Damage) * SidePp(side) / 100).RoundToLong());
                    NoteHit(ta, side);
                    if (ar.IsStone) StoneHits++; else ArrowHits++;
                }
                Arrows.RemoveAt(k);
                k--;
            }

            // 2.9) 장수 호위 (R-139): 장수가 맞은 피해의 일부를 가까운 자기 병사들이 대신 받는다
            if (r.GeneralGuardSharePct > 0) GuardGenerals();

            // 3) 반영: 위치 · 피해 · 쓰러짐
            Fix maxX = r.FieldWidth, maxY = r.FieldDepth;
            for (int i = 0; i < n; i++)
            {
                var s = Soldiers[i];
                if (!s.Alive) continue;
                var p = _next[i];
                p = new Vec(Fix.Clamp(p.X, Fix.Zero, maxX), Fix.Clamp(p.Y, Fix.Zero, maxY));
                if (Terrain != null && Terrain.Map.HasHeights) p = Slope(s, p); // 오르막은 느리고 내리막은 조금 빠름 (R-143)
                if (Terrain != null)
                {
                    // 못 가는 땅(강 · 절벽 …)에는 들어가지 않는다: 한 축으로만 미끄러지거나 제자리 (이미 그 안이면 나오는 것은 됨)
                    var t = Armies[s.Army].Troop;
                    // 성벽 위 ↔ 땅은 바로 못 오감 — 계단으로만 (R-142)
                    // 공격 쪽은 계단에서도 성벽 위로 올라서지 않음 (밀려도 — 2026-11-23 캡처 45)
                    bool climber = SiegeDefender >= 0 && s.Team != SiegeDefender;
                    bool Stop(Vec q) => (Terrain.Blocks(t, q) && !Terrain.Blocks(t, s.Pos)) || !Terrain.Links(s.Pos, q)
                        || (climber && Terrain.IsRaised(q) && !Terrain.IsRaised(s.Pos));
                    if (Stop(p))
                    {
                        var px = new Vec(p.X, s.Pos.Y);
                        var py = new Vec(s.Pos.X, p.Y);
                        p = !Stop(px) ? px : !Stop(py) ? py : s.Pos;
                    }
                }
                s.Moved = p - s.Pos;
                s.Pos = p;
                if (_damage[i] > 0 && s.IsGeneral)
                {
                    Armies[s.Army].GeneralHitTick = Tick; // 회복은 맞지 않은 동안만 (R-144)
                    // 장수는 맞아도 덜 다친다 (R-138): 받는 피해 % = 100 − (무력 − 30) × generalGuardPerMight (generalGuardMinPct ~ 100)
                    int pct = 100 - (Armies[s.Army].GeneralInfo.Might - 30) * r.GeneralGuardPerMight / 100;
                    pct = System.Math.Max(r.GeneralGuardMinPct, System.Math.Min(100, pct));
                    _damage[i] = System.Math.Max(1, _damage[i] * pct / 100);
                }
                if (_damage[i] > 0)
                {
                    s.Hp -= _damage[i];
                    if (s.Hp <= 0)
                    {
                        s.Hp = 0; s.DiedTick = Tick; s.Moved = Vec.Zero;
                        var da = Armies[s.Army];
                        if (s.IsGeneral) LoseMorale(da, r.GeneralLostMorale);
                        else LoseMorale(da, r.MoraleDeathTotal / System.Math.Max(1, da.StartCount));
                        continue;
                    }
                }
                // 무너진 병사가 자기 쪽 가장자리에 닿으면 전장 밖으로 (빠짐)
                if (Armies[s.Army].Routed && (s.Team == Ally ? p.Y <= Fix.FromRatio(1, 2) : p.Y >= maxY - Fix.FromRatio(1, 2))) { s.FledTick = Tick; s.Moved = Vec.Zero; }
            }
            if (Ladders.Count > 0) FinishClimbs(); // 사다리 꼭대기에 닿은 병사 (R-145)
            UpdateMorale();
            foreach (var a in Armies)
            {
                var g = a.General;
                // 장수가 크게 다치면 (체력 generalEscapeHpPct% 이하) 물러난다 (R-138)
                // 선봉 장수가 다치면 대열 뒤로 (R-139)
                // 대열 맨 뒤가 아니라 앞줄 generalPullBackRows줄 바로 뒤 (대열 안) — 싸움 가까이서 지휘 (2026-11-22 사용자: 맨 뒤까지 가면 퇴각처럼 보임)
                // R-145 (2026-11-23 사용자 "장수 뒤로 물러남은 20%일 때 뒤로 빠져서 체력 회복"): 성향과 관계없이 체력 generalPullBackHpPct% 이하면 물러나 회복
                if (g.Alive && !a.GeneralPulledBack && g.Hp * 100 <= g.MaxHp * r.GeneralPullBackHpPct && a.Leading)
                    a.GeneralPulledBack = true;
                // 맹장 회복 (R-144, 2026-11-23 사용자 "뒤로 잠시 빠졌다가 체력 회복해서 다시 싸움"): 물러난 동안 맞지 않은 지 generalRegenCalmSeconds 지나면
                // 초당 최대 체력의 generalRegenPctPerSec% 회복, generalReturnHpPct%가 되면 다시 선두
                if (g.Alive && a.GeneralPulledBack && !a.GeneralEscaping)
                {
                    int calm = (int)(r.GeneralRegenCalmSeconds / r.TickSeconds).RoundToLong();
                    if (a.GeneralHitTick < 0 || Tick - a.GeneralHitTick >= calm)
                    {
                        a.GeneralRegenAcc += Fix.FromInt(g.MaxHp) * r.GeneralRegenPctPerSec / 100 * r.TickSeconds;
                        int add = (int)a.GeneralRegenAcc.FloorToLong();
                        if (add > 0) { g.Hp = System.Math.Min(g.MaxHp, g.Hp + add); a.GeneralRegenAcc -= Fix.FromInt(add); }
                    }
                    if (g.Hp * 100 >= g.MaxHp * r.GeneralReturnHpPct) { a.GeneralPulledBack = false; a.GeneralRegenAcc = Fix.Zero; }
                }
                // 부대 후퇴 (R-145, 2026-11-23 사용자 "부대 후퇴가 5%일 때"): 장수 체력 generalEscapeHpPct% 이하면 장수는 퇴로로 빠지고 부대도 후퇴. 부대가 무너져도 장수가 빠짐
                if (g.Alive && !a.GeneralEscaping && (a.Routed || g.Hp <= System.Math.Max(1, g.MaxHp * r.GeneralEscapeHpPct / 100)))
                {
                    if (!a.Routed && a.Leading) OrderRetreat(a.Id);
                    a.GeneralEscaping = true;
                }
                // 적과 만나면 성향 자리로 (R-142, 2026-11-22 사용자: 만나기 전까지는 장수 자리로 이동)
                if (g.Alive && !a.GeneralEscaping)
                {
                    a.GeneralEngaged = EnemyNear(a, r.ChargeRange + r.MarchReformRange);
                    SetGeneralSlot(a);
                }
                if (a.GeneralEscaping && g.Alive && (g.Team == Ally ? g.Pos.Y <= Fix.FromRatio(1, 2) : g.Pos.Y >= maxY - Fix.FromRatio(1, 2))) { g.FledTick = Tick; g.Moved = Vec.Zero; }
                if (!a.General.Alive && a.Order != CrowdOrders.Hold) { a.Order = CrowdOrders.Hold; a.Charging = false; } // 장수를 잃으면 제자리
            }

            // 4) 끝: 총대장을 사로잡거나 (체력 0) 병사가 다 쓰러지면, 시간이 다 되면 남은 병사 많은 쪽
            // 모두 무너지면 RoutEndTicks 동안 도망치는 모습을 보인 뒤 끝 (바로 끝내면 무너진 줄 모름)
            for (int t = 0; t < 2; t++) { if (!AllRouted(t)) _allRoutedSince[t] = -1; else if (_allRoutedSince[t] < 0) _allRoutedSince[t] = Tick; }
            bool allyOut = !Commander(Ally).General.Alive || AliveCount(Ally) == 0 || (_allRoutedSince[Ally] >= 0 && Tick - _allRoutedSince[Ally] >= r.RoutEndTicks);
            bool enemyOut = !Commander(Enemy).General.Alive || AliveCount(Enemy) == 0 || (_allRoutedSince[Enemy] >= 0 && Tick - _allRoutedSince[Enemy] >= r.RoutEndTicks);
            if (allyOut || enemyOut)
            {
                Winner = allyOut && enemyOut ? -2 : allyOut ? Enemy : Ally;
                int lt = allyOut ? Ally : Enemy;
                var lg = Commander(lt).General;
                EndReason = !lg.Alive ? (lg.FledTick >= 0 ? "generalFled" : "general") : AliveCount(lt) == 0 ? "wiped" : "routed"; // 총대장이 달아남 (R-138) · 사로잡음
            }
            else if (Tick >= r.MaxTicks)
            {
                int a = AliveCount(Ally), e = AliveCount(Enemy);
                Winner = a == e ? -2 : a > e ? Ally : Enemy;
                EndReason = "time";
            }
        }

        /// <summary>그 편 부대가 모두 무너졌거나 싸울 수 없음.</summary>
        public bool AllRouted(int team)
        {
            foreach (var a in Armies) if (a.Team == team && !a.Routed && Standing(a)) return false;
            return true;
        }

        /// <summary>
        /// 사기 (R-128): 포위면 초당 encircleDrain 깎임, 맞지 않은 지 calmSeconds면 초당 moraleRegen 오름.
        /// waverAt 이하 = 흔들림, routAt 이하 = 무너짐 (되돌아오지 않음, 같은 편 부대 사기 allyRoutedMorale 깎임).
        /// </summary>
        private void UpdateMorale()
        {
            var r = Rules;
            Fix dt = r.TickSeconds;
            foreach (var a in Armies)
            {
                if (a.Routed || !Standing(a)) continue;
                int sides = 0;
                for (int k = 1; k < 4; k++) if (a.SideHitTick[k] >= Tick - r.EncircleTicks) sides++;
                a.Encircled = sides >= 2;
                // 이번 틱 옆 · 뒤에서 맞고 있으면 초당 flankDrain · rearDrain (여러 번 맞아도 한 번)
                if (a.SideHitTick[3] == Tick) LoseMorale(a, r.RearDrain * dt);
                else if (a.SideHitTick[1] == Tick || a.SideHitTick[2] == Tick) LoseMorale(a, r.FlankDrain * dt);
                if (a.Encircled) LoseMorale(a, r.EncircleDrain * dt * FormationPct(r.FormationEncircleDrainPct, a.Formation) / 100); // 원진은 포위에 덜 흔들림 (R-141)
                else if (a.LastHitTick < Tick - r.CalmTicks) a.Morale = Fix.Min(r.MoraleStart, a.Morale + r.MoraleRegen * dt);
                if (a.Morale < Fix.Zero) a.Morale = Fix.Zero;
                a.Wavering = a.Morale <= r.WaverAt;
            }
            foreach (var a in Armies)
            {
                if (a.Routed || !Standing(a) || a.Morale > r.RoutAt) continue;
                a.Routed = true;
                a.RoutedTick = Tick;
                a.Order = CrowdOrders.Hold;
                a.Charging = false;
                foreach (var o in Armies) if (o.Team == a.Team && o != a && !o.Routed) LoseMorale(o, r.AllyRoutedMorale);
            }
        }

        private Vec ToSlot(CrowdArmy army, CrowdSoldier s, Fix stepLen)
        {
            bool spot = s.Spot.HasValue && s.SpotPath != null && Terrain != null;
            var target = spot ? SpotTarget(s) : army.Marching ? ColumnPoint(army, s) : army.SlotWorld(s.Slot);
            // 사다리 명령: 아직 안 오른 병사는 사다리 발로 모인다 (R-145)
            if (!spot && !s.IsGeneral && army.SiegeTask == SiegeTasks.Ladder && SiegeActive && LadderOf(army) is CrowdLadder lad) target = lad.Foot;
            // 자리가 바로 못 가는 높이(땅에 선 병사에게 성벽 위)면 가장 가까운 이어진 칸 (R-142 — 진형 맞춘다고 성벽 위로 올라가지 않게)
            // 공격 쪽은 계단에 섰어도 성벽 위 자리로 가지 않고, 이미 올라갔으면 내려옴 (2026-11-23 캡처 45). 지키는 쪽은 성벽 위에 남음
            if (Terrain != null && !spot)
                target = SiegeDefender >= 0 && army.Team != SiegeDefender ? Terrain.SnapOffRaised(army.Troop, s.Pos, target) : Terrain.SnapLinked(army.Troop, s.Pos, target);
            bool detour = false;
            if (Terrain != null && !spot && !Terrain.Clear(army.Troop, s.Pos, target))
            {
                detour = true;
                // 자리까지 곧은 길이 막혔으면: 부대 가운데가 걸어온 자취를 따라간다 (협곡에서 길을 잃지 않게, 2026-11-22 사용자 영상),
                // 자취가 없으면 부대 가운데 · 부대가 향하는 곳으로
                // 지름길 지도를 따라 부대로 (앞 사람 자취를 고집하면 바로 갈 길도 돌아감, 2026-11-22 사용자 영상 — R-137)
                var onFlow = FlowTarget(army, s);
                target = onFlow ?? (Terrain.Clear(army.Troop, s.Pos, army.Center) ? army.Center : army.Aim);
            }
            if (Obstacles.Count > 0) target = FreeSpot(s, target); // 자리가 나무 · 바위 안이면 테두리 바로 밖 (갈 수 있는 데까지, 2026-11-22 사용자 영상)
            _goal[s.Id] = target;
            var d = target - s.Pos;
            Fix len = d.Length;
            // 제자리(대기)인 부대: 자리에 거의 왔으면 멈추고, 멈춘 뒤엔 이웃에게 조금 밀려나도(UnsettleDistance 안) 다시 당기지 않는다
            // (나무 · 이웃과 밀고 당기는 잔떨림 없앰, 2026-11-22 사용자 영상). 부대가 움직이면 풀림.
            // 자리로 못 가면 (StuckTicks 동안 가까워지지 못함) 갈 수 있는 데까지 간 그 자리에서 멈춘다 (2026-11-22 사용자 "못 가면 갈 때까지만 가고 멈춤")
            // 자취를 따라 돌아오는 중(detour)이면 멈추지 않는다 (가까워지는지는 돌아가는 길에선 알 수 없음 — 협곡에서 멈춰 버림, R-136)
            if (army.Order != CrowdOrders.Hold || detour || (spot && s.SpotIdx < s.SpotPath.Count - 1)) { s.Settled = false; s.BestDist = Fix.FromInt(9999); s.StuckTicks = 0; }
            else
            {
                if (s.Settled && (s.Pos - s.SettleAt).Length <= UnsettleDistance && (target - s.SettleGoal).Length <= UnsettleDistance) return Vec.Zero;
                s.Settled = false;
                bool stuck = false;
                if (len < s.BestDist - Fix.FromRatio(2, 100)) { s.BestDist = len; s.StuckTicks = 0; }
                else stuck = ++s.StuckTicks >= StuckLimitTicks;
                if (len <= SettleDistance || stuck) { s.Settled = true; s.SettleAt = s.Pos; s.SettleGoal = target; s.StuckTicks = 0; return Vec.Zero; }
            }
            return len <= stepLen ? d : d * (stepLen / len);
        }

        /// <summary>
        /// 근접 한 방. 빠르게 달려와(지난 틱 걸음의 1.1배 넘게) 처음 친 한 방 = 부딪침: 피해 더 (기병은 mountedImpact배), 상대를 밀침.
        /// 단, 기병이 창병에게 달려들면 받아치기: 부딪침 없이 기병이 피해를 입고 밀려난다.
        /// </summary>
        private void Strike(CrowdSoldier s, CrowdSoldier e, Vec d, Fix d2, CrowdArmy army, Fix walkStep)
        {
            var r = Rules;
            var troop = army.Troop;
            var foeArmy = Armies[e.Army];
            var foeTroop = foeArmy.Troop;
            int side = SideOf(foeArmy, foeArmy.TickCentroid, s.Pos);
            Fix mightPp = s.IsGeneral ? Fix.FromInt(army.GeneralInfo.Might - 50) * Rules.GeneralMightPp : Fix.Zero; // 장수가 직접 친 한 방 (R-138)
            int dmg = HitDamage(troop, foeTroop, SidePp(side) + BuffPp(army, foeArmy) + TerrainPp(troop, s.Pos) + mightPp + AptitudePp(army) - AptitudePp(foeArmy) + FormationCounterPp(army, foeArmy)
                + HeightDiff(s.Pos, e.Pos) * Rules.HeightMeleePpPerLevel); // 내려치면 세고 올려치면 약함 (R-143)
            _damage[e.Id] += dmg;
            NoteHit(foeArmy, side);
            Fix fast = walkStep * Fix.FromRatio(11, 10);
            bool rushed = s.Moved.LengthSquared > fast * fast && s.LastAttackTick < Tick - r.AttackTicks * 3;
            if (rushed)
            {
                Fix len = Fix.Sqrt(d2);
                if (troop.Mounted && foeTroop.Polearm && !e.IsGeneral)
                {
                    // 받아치기: 창끝에 걸린 기병이 피해를 입고 튕겨 나간다
                    _damage[s.Id] += HitDamage(foeTroop, troop, BuffPp(foeArmy, army) + TerrainPp(foeTroop, e.Pos)) * r.BraceDamage;
                    if (len > Fix.Zero) _knock[s.Id] = _knock[s.Id] - d * (r.ImpactPush / len);
                    s.ImpactTick = Tick;
                    e.ImpactTick = Tick;
                    ImpactsThisTick++;
                }
                else
                {
                    int mult = troop.Mounted ? r.MountedImpact : 1;
                    _damage[e.Id] += dmg * r.ImpactDamage * mult;
                    if (len > Fix.Zero) _knock[e.Id] = _knock[e.Id] + d * (r.ImpactPush * mult / len);
                    s.ImpactTick = Tick;
                    e.ImpactTick = Tick;
                    ImpactsThisTick++;
                }
            }
            s.LastAttackTick = Tick;
            s.LastTargetId = e.Id;
            s.HitStructure = false;
            s.Cooldown = r.AttackTicks + (r.AttackJitterTicks > 0 ? _rng.NextInt(r.AttackJitterTicks + 1) : 0);
            if (army.Wavering) s.Cooldown = (int)(Fix.FromInt(s.Cooldown) * r.WaverAttack).RoundToLong(); // 흔들리면 덜 친다
        }

        /// <summary>화살 한 대: 쏜 틱 + 거리 ÷ 화살 빠르기 뒤에 닿는다.</summary>
        private void Shoot(CrowdSoldier s, int target, CrowdTroop troop)
        {
            var r = Rules;
            var e = Soldiers[target];
            Fix dist = (e.Pos - s.Pos).Length;
            int flight = System.Math.Max(1, (int)(dist / (r.ArrowSpeed * r.TickSeconds)).RoundToLong());
            Arrows.Add(new CrowdArrow { ShooterId = s.Id, TargetId = target, From = s.Pos, To = e.Pos, FireTick = Tick, ArriveTick = Tick + flight, Damage = System.Math.Max(1, (int)(Fix.FromInt(HitDamage(troop, Armies[e.Army].Troop, BuffPp(Armies[s.Army], Armies[e.Army]) + TerrainPp(troop, s.Pos) + AptitudePp(Armies[s.Army]) - AptitudePp(Armies[e.Army]))) * r.ArrowDamageScale).RoundToLong()),
                HitPct = Fix.Max(Fix.Zero, Fix.FromInt(100) + HeightDiff(s.Pos, e.Pos) * r.HeightArrowHitPctPerLevel) }); // 위에서 쏘면 잘 맞음 (R-143)
            s.LastAttackTick = Tick;
            s.LastTargetId = target;
            s.HitStructure = false;
            s.Cooldown = r.ShootTicks + (r.AttackJitterTicks > 0 ? _rng.NextInt(r.AttackJitterTicks * 2 + 1) : 0);
        }

        /// <summary>AI 부대 (적 전진 · 우리 위임, R-132): 공격 대상이 없거나 무너졌으면 가장 가까운 상대 부대를 공격.</summary>
        private void AiThink()
        {
            foreach (var a in Armies)
            {
                if (!a.Leading || !AiControls(a)) continue;
                if (AiSiege(a)) continue; // 공성 중엔 공성 명령 (R-145)
                if (a.Order == CrowdOrders.Attack && a.OrderTarget >= 0 && Standing(Armies[a.OrderTarget])) continue;
                int best = NearestEnemyArmy(a.Id);
                if (best >= 0) OrderAttack(a.Id, best);
            }
        }

        /// <summary>부대 가운데를 명령대로 옮긴다. 공격이면 가까워질 때 돌격 (궁병은 사거리에서 멈춤), 맞붙으면 돌격 끝 · 느려짐.</summary>
        private void MoveArmy(CrowdArmy a, int engagedSoldiers)
        {
            var r = Rules;
            Fix dt = r.TickSeconds;
            if (a.Order == CrowdOrders.Hold || a.Routed) { a.Charging = false; return; }
            Vec goal;
            Fix ahead = a.Team == Ally ? Fix.OneValue : -Fix.OneValue;
            if (a.Order == CrowdOrders.Move) goal = a.OrderPoint;
            else if (a.Order == CrowdOrders.Retreat)
            {
                a.Charging = false;
                Fix edge = r.RetreatMargin + a.HalfDepth;
                goal = Open(a, new Vec(a.Center.X, a.Team == Ally ? edge : r.FieldDepth - edge));
                Fix step = r.AdvanceSpeed * r.RetreatSpeed * a.SpeedFactor * MoveMult(a) * TerrainSpeed(a.Troop, a.Center) * dt;
                if ((goal - a.Center).Length <= step)
                {
                    a.Center = goal;
                    a.Order = CrowdOrders.Hold;
                    return;
                }
                // 대열은 그대로 (앞 방향을 뒤집으면 병사들이 서로 자리를 바꾸며 엉킴, 캡처 39h) 뒤로 물러난다 — 맞는 쪽은 등으로 친다 (SideOf)
                a.Aim = Steer(a, goal);
                var dr = a.Aim - a.Center;
                if (dr.Length > Fix.Zero) a.Center = a.Center + dr.Normalized() * Fix.Min(step, dr.Length);
                return;
            }
            else if (a.Order == CrowdOrders.Advance)
            {
                Fix edge = a.HalfDepth + Fix.FromInt(2);
                goal = Open(a, new Vec(a.Center.X, a.Team == Ally ? r.FieldDepth - edge : edge));
                int near = NearestEnemyArmy(a.Id);
                if (near >= 0)
                {
                    var t = Armies[near];
                    Fix gap = (t.Centroid() - a.Center).Length - a.HalfDepth - t.HalfDepth;
                    if (Shooting(a))
                    {
                        a.Charging = false;
                        if (gap <= RangeOf(a.Troop) * Fix.FromRatio(4, 5)) return; // 궁병: 적이 사거리에 들면 멈춰 쏜다
                    }
                    else
                    {
                        int alive0 = System.Math.Max(1, a.AliveCount);
                        bool contact = engagedSoldiers * 10 >= alive0;
                        if (contact) a.Charging = false;
                        else if (gap <= r.ChargeRange) a.Charging = true;
                    }
                }
                if ((goal - a.Center).Length <= Fix.OneValue) { a.Order = CrowdOrders.Hold; a.Charging = false; return; }
            }
            else
            {
                var t = Armies[a.OrderTarget];
                if (!Standing(t)) { a.Order = CrowdOrders.Hold; a.Charging = false; return; }
                goal = t.Centroid();
                Fix gap = (goal - a.Center).Length - a.HalfDepth - t.HalfDepth;
                if (Shooting(a))
                {
                    // 궁병: 사거리의 80% 안이면 멈춰서 쏜다 (방향만 맞춤)
                    a.Charging = false;
                    if (gap <= RangeOf(a.Troop) * Fix.FromRatio(4, 5))
                    {
                        var dd = goal - a.Center;
                        if (dd.LengthSquared > Fix.OneValue) a.Facing = dd.Normalized();
                        return;
                    }
                }
                else
                {
                    int alive0 = System.Math.Max(1, a.AliveCount);
                    bool contact = engagedSoldiers * 10 >= alive0; // 10% 넘게 맞붙음
                    if (contact) a.Charging = false;
                    else if (gap <= r.ChargeRange) a.Charging = true;
                }
            }
            var d = goal - a.Center;
            Fix dist = d.Length;
            Fix baseSpeed = (a.Charging ? r.ChargeAdvanceSpeed : r.AdvanceSpeed) * a.SpeedFactor * MoveMult(a) * TerrainSpeed(a.Troop, a.Center) * FormationPct(r.FormationSpeedPct, a.Formation) / 100
                * SlopePct(a.Center, d) / 100; // 부대 가운데도 비탈에서 느려짐 (R-143 — 병사만 느리면 대열이 뒤처짐)
            if (a.Order == CrowdOrders.Move && dist <= baseSpeed * dt)
            {
                a.Center = goal;
                // 예약한 경유 지점이 있으면 다음으로, 없으면 제자리
                if (a.Waypoints.Count > 0) { a.OrderPoint = a.Waypoints[0]; a.Waypoints.RemoveAt(0); }
                else if (a.PathThenAttack >= 0 && Standing(Armies[a.PathThenAttack])) OrderAttack(a.Id, a.PathThenAttack); // 그린 길 끝이 적이면 공격
                else { a.Order = CrowdOrders.Hold; a.Charging = false; a.PathThenAttack = -1; }
                return;
            }
            if (a.Order == CrowdOrders.Attack && dist <= Fix.OneValue) return;
            // 지형 (R-133): 못 가는 땅이 가로막으면 돌아가는 길의 다음 점으로
            a.Aim = Steer(a, goal);
            bool detour = !a.Aim.Equals(goal);
            var dv = a.Aim - a.Center;
            Fix dl = dv.Length;
            if (a.Order == CrowdOrders.Advance && !detour) a.Facing = new Vec(Fix.Zero, ahead); // 진군: 곧게 앞으로
            else if (dl > Fix.OneValue) a.Facing = dv.Normalized(); // 도착 직전에는 방향을 그대로 (뒤집히지 않게)
            int alive = System.Math.Max(1, a.AliveCount);
            Fix slow = Fix.OneValue - Fix.Min(Fix.OneValue, Fix.FromRatio(engagedSoldiers * 2, alive));
            var dir = a.Order == CrowdOrders.Advance && !detour ? a.Facing : dl > Fix.Zero ? dv * (Fix.OneValue / dl) : Vec.Zero;
            var nextCenter = a.Center + dir * Fix.Min(detour ? dl : dist, baseSpeed * dt * slow);
            // 닫힌 성문 앞에서는 기다린다 (R-140: 병사들이 문을 부술 때까지 — 가운데만 성 안으로 들어가지 않게)
            if (Gates.Count > 0 && Terrain.IsClosed(Terrain.CellIndex(nextCenter)) && !Terrain.IsClosed(Terrain.CellIndex(a.Center))) return; // 닫힌 성문만 (강 모서리를 스치는 건 그대로)
            a.Center = nextCenter;
        }

        private const int RelaxPasses = 2;

        /// <summary>
        /// 다음 위치(_next)가 막는 소품(나무 기둥 · 바위) 안이면 테두리 밖으로 (R-134).
        /// 가던 방향이 소품 가운데를 곧게 향하면 (밀어내기만 하면 그 자리에 멈춤) 소품 옆쪽으로 비켜 간다 — 소품이 가는 길 왼쪽이면 오른쪽으로, 아니면 왼쪽으로.
        /// </summary>
        // ---- 행군 대열 (R-136) ----

        /// <summary>
        /// 행군할지: 자동 행군이 켜져 있고, 움직이는 명령이며, (못 가는 땅을 돌아가는 중이거나 부대 가운데가 marchTerrains 위) · 적이 가깝지 않을 때.
        /// 켜는 건 바로, 끄는 건 marchOffTicks 동안 조건이 아닐 때 (들락날락 막음). 끌 때 지금 자리 순서대로 진형 자리를 다시 나눈다.
        /// 자취: 부대 가운데가 marchTrailStep칸 움직일 때마다 한 점 (최대 marchTrailMax점).
        /// </summary>
        private void UpdateMarch(CrowdArmy a)
        {
            var r = Rules;
            if (a.Trail.Count == 0 || (a.Center - a.Trail[0]).Length >= r.MarchTrailStep)
            {
                a.Trail.Insert(0, a.Center);
                if (a.Trail.Count > r.MarchTrailMax) a.Trail.RemoveAt(a.Trail.Count - 1);
            }
            bool moving = a.Order == CrowdOrders.Move || a.Order == CrowdOrders.Advance || a.Order == CrowdOrders.Attack || a.Order == CrowdOrders.Retreat;
            if (a.Order != CrowdOrders.Move) a.MarchMove = false;
            bool near = EnemyNear(a, r.ChargeRange + r.MarchReformRange);
            bool want = a.Leading && moving && !near
                && ((a.MarchMove && a.Order == CrowdOrders.Move) || (a.AutoMarch && Terrain != null && (a.Route.Count > 0 || MarchGround(a.Center))));
            if (want)
            {
                if (!a.Marching) StartMarch(a);
                a.Marching = true;
                a.MarchOffTicks = 0;
                return;
            }
            if (!a.Marching) return;
            bool stop = !moving || !a.Leading || EnemyNear(a, r.ChargeRange + r.MarchReformRange);
            if (stop || ++a.MarchOffTicks >= r.MarchOffTicks)
            {
                a.Marching = false;
                a.MarchOffTicks = 0;
                if (a.Leading) SetFormation(a.Id, a.Formation); // 지금 자리 순서대로 진형 자리를 다시 나눔 (멀리 엇갈려 가지 않게)
            }
        }

        /// <summary>
        /// 행군 시작 (R-137): 자취를 지금 자리에서 새로 (예전 자취를 되밟아 왔다 갔다 하지 않게, 2026-11-22 사용자 영상),
        /// 줄 순서 = 가는 곳에 가까운 병사부터 (같으면 id).
        /// </summary>
        private void StartMarch(CrowdArmy a)
        {
            a.Trail.Clear();
            a.Trail.Add(a.Center);
            var goal = a.Order == CrowdOrders.Move ? a.OrderPoint : a.Center + a.Facing * Fix.FromInt(10);
            a.MarchOrder.Clear();
            foreach (var s in a.Soldiers) if (s.Alive && !s.IsGeneral) a.MarchOrder.Add(s);
            a.MarchOrder.Sort((p, q) =>
            {
                int c = Vec.DistanceSquared(p.Pos, goal).CompareTo(Vec.DistanceSquared(q.Pos, goal));
                return c != 0 ? c : p.Id.CompareTo(q.Id);
            });
        }

        private bool MarchGround(Vec p)
        {
            if (Terrain == null) return false;
            string t = Terrain.TerrainAt(p);
            foreach (var id in Rules.MarchTerrains) if (id == t) return true;
            return false;
        }

        private bool EnemyNear(CrowdArmy a, Fix range)
        {
            var c = a.Center;
            Fix r2 = range * range;
            foreach (var o in Armies)
                if (o.Team != a.Team && Standing(o) && Vec.DistanceSquared(o.TickCentroid, c) <= r2) return true;
            return false;
        }

        /// <summary>자취 위 dist칸 뒤의 점 (부대 가운데에서 시작). 자취가 짧으면 끝 점.</summary>
        private Vec TrailPoint(CrowdArmy a, Fix dist, out Vec dir)
        {
            var prev = a.Center;
            dir = a.Facing;
            Fix left = dist;
            foreach (var p in a.Trail)
            {
                var seg = p - prev;
                Fix len = seg.Length;
                if (len > Fix.Zero)
                {
                    dir = seg * (-Fix.OneValue / len); // 앞(머리) 쪽
                    if (len >= left) return prev + seg * (left / len);
                    left -= len;
                }
                prev = p;
            }
            return prev;
        }

        /// <summary>
        /// 행군 대열에서 그 병사의 자리: 살아 있는 병사 id 순으로 marchWidth명씩 줄, 줄마다 자취 위 (줄 + 1) × 간격 뒤, 줄 안에서는 자취와 직각으로 벌림.
        /// 장수는 대열 가운데 (나중에 성향으로 앞 · 뒤). 벌린 자리가 못 가는 땅이면 자취 위.
        /// </summary>
        private Vec ColumnPoint(CrowdArmy a, CrowdSoldier s)
        {
            int width = System.Math.Max(1, Rules.MarchWidth);
            Fix sp = SpacingOf(a.Troop) * Fix.FromRatio(13, 10);
            int alive = a.AliveCount;
            if (s.IsGeneral)
            {
                int rows = alive / width;
                int at = rows / 2 + 1; // 행군은 적과 만나기 전 — 성향과 관계없이 대열 가운데 (R-142)
                return TrailPoint(a, sp * at + sp / 2, out _);
            }
            int k = 0;
            foreach (var o in a.MarchOrder) { if (o == s) break; if (o.Alive) k++; }
            int row = k / width, col = k % width;
            var p = TrailPoint(a, sp * (row + 1), out var dir);
            var side = new Vec(dir.Y, -dir.X);
            var q = p + side * (sp * (2 * col - (width - 1)) / 2);
            return Terrain != null && Terrain.Blocks(a.Troop, q) ? p : q;
        }


        /// <summary>
        /// 막힌 병사의 다음 목표 (R-137): 부대 가운데 칸까지의 지름길 지도에서 두 칸 앞 (곧게 갈 수 있으면 두 칸, 아니면 한 칸) 칸 가운데.
        /// 지도는 부대 가운데 칸이 바뀌었거나 flowRefreshTicks 지나면 새로 (막힌 병사가 있는 부대만). 부대 칸에 닿았으면 null (자리 · 가운데로).
        /// </summary>
        private Vec? FlowTarget(CrowdArmy a, CrowdSoldier s)
        {
            int goal = Terrain.CellIndex(a.Center);
            if (a.Flow == null || a.FlowCell != goal || Tick - a.FlowTick >= Rules.FlowRefreshTicks)
            {
                a.Flow = Terrain.DistanceField(a.Troop, a.Center, Besieging(a));
                a.FlowCell = goal;
                a.FlowTick = Tick;
            }
            int cell = Terrain.CellIndex(s.Pos);
            if (cell == goal || a.Flow[cell] == long.MaxValue) return null;
            int w = Terrain.Map.Width;
            int n1 = Terrain.NextCell(a.Troop, a.Flow, cell, Besieging(a));
            if (n1 < 0) return null;
            var p1 = Terrain.CellCenter(n1 % w, n1 / w);
            int n2 = n1 == goal ? -1 : Terrain.NextCell(a.Troop, a.Flow, n1, Besieging(a));
            if (n2 >= 0)
            {
                var p2 = Terrain.CellCenter(n2 % w, n2 / w);
                if (Terrain.Clear(a.Troop, s.Pos, p2)) return p2;
            }
            return p1;
        }

        /// <summary>제자리 부대 병사가 자리에 이만큼 가까우면 멈춘다 (칸).</summary>
        private static readonly Fix SettleDistance = Fix.FromRatio(20, 100);
        /// <summary>제자리 부대 병사가 이만큼 틱 동안 자리에 가까워지지 못하면 그 자리에서 멈춘다.</summary>
        private const int StuckLimitTicks = 15;
        /// <summary>멈춘 병사가 멈춘 곳에서 이만큼 넘게 밀려나면 다시 자리로 (칸).</summary>
        private static readonly Fix UnsettleDistance = Fix.FromRatio(80, 100);

        /// <summary>그 자리가 막는 소품 안이면 가장 가까운 테두리 바로 밖 (두 번 — 두 그루 사이도), 아니면 그대로.</summary>
        private Vec FreeSpot(CrowdSoldier s, Vec p)
        {
            int reach = (int)(_maxObstacleRadius + _maxRadius).FloorToLong() + 1;
            for (int pass = 0; pass < 2; pass++)
            {
                long cx = p.X.FloorToLong(), cy = p.Y.FloorToLong();
                for (long x = cx - reach; x <= cx + reach; x++)
                    for (long y = cy - reach; y <= cy + reach; y++)
                    {
                        if (!_obstacleGrid.TryGetValue(Key(x, y), out var list)) continue;
                        foreach (int k in list)
                        {
                            var o = Obstacles[k];
                            Fix min = o.Radius + ObstacleBody(s) + Fix.FromRatio(2, 100);
                            var d = p - o.Pos;
                            Fix d2 = d.LengthSquared;
                            if (d2 >= min * min) continue;
                            Fix len = Fix.Sqrt(d2);
                            p = o.Pos + (len > Fix.Zero ? d * (min / len) : new Vec(min, Fix.Zero));
                        }
                    }
            }
            return p;
        }

        /// <summary>나무 · 바위에 부딪치는 몸집: 장수(말)도 병사만큼 (바위 틈에 갇히지 않게, 2026-11-22 사용자 영상 — 말은 비집고 지나간다).</summary>
        private Fix ObstacleBody(CrowdSoldier s) => s.IsGeneral ? Rules.SoldierRadius : s.Radius;

        /// <summary>그 자리면 병사가 막는 소품 안에 들어감 (반올림 여유 0.01칸).</summary>
        private bool InsideObstacle(CrowdSoldier s, Vec p)
        {
            int reach = (int)(_maxObstacleRadius + _maxRadius).FloorToLong() + 1;
            long cx = p.X.FloorToLong(), cy = p.Y.FloorToLong();
            for (long x = cx - reach; x <= cx + reach; x++)
                for (long y = cy - reach; y <= cy + reach; y++)
                {
                    if (!_obstacleGrid.TryGetValue(Key(x, y), out var list)) continue;
                    foreach (int k in list)
                    {
                        var o = Obstacles[k];
                        Fix min = o.Radius + ObstacleBody(s) - Fix.FromRatio(1, 100);
                        if (Vec.DistanceSquared(p, o.Pos) < min * min) return true;
                    }
                }
            return false;
        }

        private void PushOutOfObstacles()
        {
            int reach = (int)(_maxObstacleRadius + _maxRadius).FloorToLong() + 1;
            for (int i = 0; i < Soldiers.Count; i++)
            {
                var s = Soldiers[i];
                if (!s.Alive) continue;
                var p = _next[i];
                long cx = p.X.FloorToLong(), cy = p.Y.FloorToLong();
                for (long x = cx - reach; x <= cx + reach; x++)
                    for (long y = cy - reach; y <= cy + reach; y++)
                    {
                        if (!_obstacleGrid.TryGetValue(Key(x, y), out var list)) continue;
                        foreach (int k in list)
                        {
                            var o = Obstacles[k];
                            Fix min = o.Radius + ObstacleBody(s);
                            var d = p - o.Pos;
                            Fix d2 = d.LengthSquared;
                            if (d2 >= min * min) continue;
                            Fix len = Fix.Sqrt(d2);
                            Vec outDir = len > Fix.Zero ? d * (Fix.OneValue / len) : new Vec(s.Id % 2 == 0 ? Fix.OneValue : -Fix.OneValue, Fix.Zero);
                            var move = p - s.Pos;
                            Fix beyond = o.Radius + s.Radius + Fix.FromRatio(3, 10);
                            bool passing = Vec.DistanceSquared(_goal[i], o.Pos) > beyond * beyond && Vec.DistanceSquared(_goal[i], s.Pos) > Vec.DistanceSquared(o.Pos, s.Pos);
                            if (passing && move.LengthSquared > Fix.Zero && outDir.Dot(move) < Fix.Zero)
                            {
                                // 정면으로 부딪힘: 옆으로 (소품이 가는 길의 어느 쪽인지로 비킬 쪽을 정함)
                                var side = new Vec(-move.Y, move.X); // 왼쪽
                                var toObs = o.Pos - s.Pos;
                                Fix cross = move.X * toObs.Y - move.Y * toObs.X; // > 0 = 소품이 왼쪽
                                if (cross > Fix.Zero || (cross == Fix.Zero && s.Id % 2 == 1)) side = new Vec(move.Y, -move.X);
                                outDir = (outDir + side.Normalized()).Normalized();
                            }
                            p = o.Pos + outDir * min;
                        }
                    }
                _next[i] = p;
            }
        }

        /// <summary>다음 위치(_next) 기준으로 겹친 두 병사를 반씩 떼어 놓는다 (id 순서, 이웃은 지금 칸 격자로).</summary>
        private void Relax()
        {
            for (int i = 0; i < Soldiers.Count; i++)
            {
                var s = Soldiers[i];
                if (!s.Alive) continue;
                long cx = s.Pos.X.FloorToLong(), cy = s.Pos.Y.FloorToLong();
                for (long x = cx - 1; x <= cx + 1; x++)
                    for (long y = cy - 1; y <= cy + 1; y++)
                    {
                        if (!_grid.TryGetValue(Key(x, y), out var list)) continue;
                        foreach (int j in list)
                        {
                            if (j <= i) continue;
                            var o = Soldiers[j];
                            Fix diameter = s.Radius + o.Radius;
                            var d = _next[i] - _next[j];
                            Fix d2 = d.LengthSquared;
                            if (d2 >= diameter * diameter || d2 == Fix.Zero) continue;
                            Fix len = Fix.Sqrt(d2);
                            var corr = d * ((diameter - len) / 2 / len);
                            bool gi = s.IsGeneral, gj = o.IsGeneral;
                            _next[i] = _next[i] + (gi && !gj ? corr * Fix.FromRatio(1, 4) : gj && !gi ? corr * Fix.FromRatio(7, 4) : corr);
                            _next[j] = _next[j] - (gj && !gi ? corr * Fix.FromRatio(1, 4) : gi && !gj ? corr * Fix.FromRatio(7, 4) : corr);
                        }
                    }
            }
        }

        // ---- 이웃 찾기: 1칸 격자 ----

        private static long Key(long x, long y) => x * 100003L + y;

        private void BuildGrid()
        {
            foreach (var kv in _grid) kv.Value.Clear();
            for (int i = 0; i < Soldiers.Count; i++)
            {
                var s = Soldiers[i];
                if (!s.Alive) continue;
                long k = Key(s.Pos.X.FloorToLong(), s.Pos.Y.FloorToLong());
                if (!_grid.TryGetValue(k, out var list)) _grid[k] = list = new List<int>();
                list.Add(i);
            }
        }

        /// <summary>이 거리 제곱 안의 가장 가까운 적 (같으면 작은 id). 없으면 -1.</summary>
        private int NearestEnemy(CrowdSoldier s, Fix range2, Fix range)
        {
            int best = -1;
            Fix bestD = range2;
            long cx = s.Pos.X.FloorToLong(), cy = s.Pos.Y.FloorToLong();
            int cells = (int)range.FloorToLong() + 1;
            for (long x = cx - cells; x <= cx + cells; x++)
                for (long y = cy - cells; y <= cy + cells; y++)
                {
                    if (!_grid.TryGetValue(Key(x, y), out var list)) continue;
                    foreach (int j in list)
                    {
                        var o = Soldiers[j];
                        if (o.Team == s.Team) continue;
                        Fix d2 = Vec.DistanceSquared(o.Pos, s.Pos);
                        if (d2 < bestD || (d2 == bestD && best >= 0 && j < best)) { bestD = d2; best = j; }
                    }
                }
            return best;
        }

        /// <summary>겹친 이웃(편 상관없이)에게서 겹친 만큼의 절반씩 밀려난다.</summary>
        private Vec Separation(CrowdSoldier s)
        {
            Fix px = Fix.Zero, py = Fix.Zero;
            long cx = s.Pos.X.FloorToLong(), cy = s.Pos.Y.FloorToLong();
            for (long x = cx - 1; x <= cx + 1; x++)
                for (long y = cy - 1; y <= cy + 1; y++)
                {
                    if (!_grid.TryGetValue(Key(x, y), out var list)) continue;
                    foreach (int j in list)
                    {
                        if (j == s.Id) continue;
                        var o = Soldiers[j];
                        Fix diameter = s.Radius + o.Radius;
                        var d = s.Pos - o.Pos;
                        Fix d2 = d.LengthSquared;
                        if (d2 >= diameter * diameter) continue;
                        if (d2 == Fix.Zero)
                        {
                            Fix e = Fix.FromRatio(1, 100);
                            px += s.Id < j ? -e : e;
                            continue;
                        }
                        Fix len = Fix.Sqrt(d2);
                        Fix push = (diameter - len) / 2;
                        if (s.IsGeneral) push = push / 2;
                        px += d.X * push / len;
                        py += d.Y * push / len;
                    }
                }
            return new Vec(px, py);
        }

        /// <summary>결과 지문 (같은 입력이면 같아야 함 — 결정성 시험용).</summary>
        public long Fingerprint()
        {
            unchecked
            {
                long h = 17;
                foreach (var s in Soldiers)
                {
                    h = h * 31 + s.Pos.X.Raw;
                    h = h * 31 + s.Pos.Y.Raw;
                    h = h * 31 + s.Hp;
                    h = h * 31 + s.DiedTick;
                }
                return h * 31 + Tick;
            }
        }
    }
}
