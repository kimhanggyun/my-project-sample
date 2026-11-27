using System.Collections.Generic;
using Game.Data.Json;
using Game.Foundation.Math;
using Newtonsoft.Json.Linq;

namespace Game.Battle.Model.Crowd
{
    /// <summary>
    /// 샘플전투2 (병사 단위 전투, R-125) 수치. data/Rules/crowd.json.
    /// 거리 단위는 칸, 시간은 초. 시험 중인 방식이라 기존 전투(combat.json)와 따로 둔다.
    /// </summary>
    public sealed class CrowdRules
    {
        public const string File = "crowd.json";

        public Fix TickSeconds { get; private set; }
        public Fix FieldWidth { get; private set; }
        public Fix FieldDepth { get; private set; }
        /// <summary>병사 하나가 차지하는 반지름 (서로 이만큼 밀어낸다 — 적과도 섞이지 않음).</summary>
        public Fix SoldierRadius { get; private set; }
        /// <summary>대열 자리 간격.</summary>
        public Fix Spacing { get; private set; }
        /// <summary>병사 이동 속도 (칸/초).</summary>
        public Fix MoveSpeed { get; private set; }
        /// <summary>군단(대열 가운데) 전진 속도 (칸/초). 맞붙은 병사가 많을수록 느려진다.</summary>
        public Fix AdvanceSpeed { get; private set; }
        /// <summary>이 거리 안에 적이 있으면 대열 자리를 떠나 그 적에게 간다 (뒷줄도 앞으로 몰려 나옴).</summary>
        public Fix EngageRange { get; private set; }
        /// <summary>두 병사 가장자리 사이가 이 거리 안이면 칠 수 있다.</summary>
        public Fix AttackReach { get; private set; }
        public Fix AttackSeconds { get; private set; }
        public int AttackJitterTicks { get; private set; }
        public int SoldierHp { get; private set; }
        public int GeneralHp { get; private set; }
        /// <summary>장수는 대열 맨 뒤에서 이만큼 더 뒤.</summary>
        public Fix GeneralBack { get; private set; }
        public Fix MaxSeconds { get; private set; }
        /// <summary>돌격 (R-126): 공격 명령으로 두 대열 가장자리 사이가 이 거리 안이면 맞붙기 전까지 빨라진다.</summary>
        public Fix ChargeRange { get; private set; }
        public Fix ChargeSpeed { get; private set; }
        public Fix ChargeAdvanceSpeed { get; private set; }
        /// <summary>부딪침: 달려와 처음 친 한 방의 피해 더 · 상대를 밀치는 거리.</summary>
        public int ImpactDamage { get; private set; }
        public Fix ImpactPush { get; private set; }
        /// <summary>부대 셋일 때 부대 사이 간격.</summary>
        public Fix CorpsGap { get; private set; }
        // 병종 (R-127)
        public int HitDamage { get; private set; }
        public Fix StatPpPerPoint { get; private set; }
        public Fix BaseSpeedStat { get; private set; }
        public Fix PolearmReach { get; private set; }
        public Fix RangeScale { get; private set; }
        public Fix ShootSeconds { get; private set; }
        public Fix ArrowSpeed { get; private set; }
        public Fix ArrowHitChance { get; private set; }
        /// <summary>화살 한 대 피해 = 근접 피해 × 이 배수 (활은 많이 쏘는 대신 한 대는 약하게).</summary>
        public Fix ArrowDamageScale { get; private set; }
        public Fix MountedRadius { get; private set; }
        public int MountedImpact { get; private set; }
        public int BraceDamage { get; private set; }
        public int ShootTicks => System.Math.Max(1, (int)(ShootSeconds / TickSeconds).RoundToLong());
        // 사기 · 퇴각 (R-128)
        public Fix MoraleStart { get; private set; }
        public Fix MoraleDeathTotal { get; private set; }
        /// <summary>옆 · 뒤에서 맞는 동안 초당 사기 깎임 (맞은 횟수가 아니라 맞고 있는 시간).</summary>
        public Fix FlankDrain { get; private set; }
        public Fix RearDrain { get; private set; }
        public Fix GeneralLostMorale { get; private set; }
        public Fix AllyRoutedMorale { get; private set; }
        public Fix EncircleSeconds { get; private set; }
        public Fix EncircleDrain { get; private set; }
        public Fix CalmSeconds { get; private set; }
        public Fix MoraleRegen { get; private set; }
        public Fix WaverAt { get; private set; }
        // 사기 약화 · 이탈 (R-148)
        public Fix MoraleLowAt { get; private set; }
        public Fix MoraleLowPct { get; private set; }
        public Fix MoraleWaverPct { get; private set; }
        public Fix DesertSeconds { get; private set; }
        public Fix DesertMinPct { get; private set; }
        public Fix DesertMaxPct { get; private set; }
        public Fix WaverSpeed { get; private set; }
        // 사기 충만 · 함성 혼란 (R-152, 2026-11-25 사용자 "고무 쓰면 사기 충만, 혼란한 상태"): 초 · 공격 · 방어 %p
        public Fix InspireSeconds { get; private set; }
        public Fix InspirePp { get; private set; }
        public Fix ConfuseSeconds { get; private set; }
        public Fix ConfusePp { get; private set; }
        public Fix WaverAttack { get; private set; }
        public Fix FleeSpeed { get; private set; }
        public Fix FlankPp { get; private set; }
        public Fix RearPp { get; private set; }
        public Fix FrontDot { get; private set; }
        public Fix RearDot { get; private set; }
        /// <summary>한 편이 모두 무너진 뒤 도망치는 모습을 보이고 끝나기까지 (초).</summary>
        public Fix RoutEndSeconds { get; private set; }
        // 스킬 (R-130)
        public Fix SkillGapSeconds { get; private set; }
        public Fix SkillContactRange { get; private set; }
        public int ArrowRainVolleys { get; private set; }
        public Fix ArrowRainChance { get; private set; }
        /// <summary>병종(뿌리 병종 id)마다 추천 진형 (R-132, 진형 고르기에 "추천" 표시). 없으면 표시 없음.</summary>
        public System.Collections.Generic.IReadOnlyDictionary<string, string> RecommendedFormation { get; private set; } = new System.Collections.Generic.Dictionary<string, string>();
        /// <summary>지형마다 막는 소품 (R-134, 나무 기둥 · 바위): 맵 칸 하나에 몇 개 · 반지름.</summary>
        public System.Collections.Generic.IReadOnlyDictionary<string, CrowdObstacleSpec> Obstacles { get; private set; } = new System.Collections.Generic.Dictionary<string, CrowdObstacleSpec>();
        // 장수 성향 (R-138)
        public sealed class GeneralPreset
        {
            public int Might, Intellect;
            public string Role;
            public System.Collections.Generic.IReadOnlyDictionary<string, string> Aptitude = new System.Collections.Generic.Dictionary<string, string>();
        }
        public System.Collections.Generic.IReadOnlyDictionary<string, GeneralPreset> GeneralPresets { get; private set; } = new System.Collections.Generic.Dictionary<string, GeneralPreset>();
        public System.Collections.Generic.IReadOnlyDictionary<string, Fix> AptitudePp { get; private set; } = new System.Collections.Generic.Dictionary<string, Fix>();
        public Fix GeneralHpMight { get; private set; }
        public int GeneralFightMight { get; private set; }
        public Fix GeneralMightPp { get; private set; }
        public Fix GeneralFront { get; private set; }
        public Fix GeneralRear { get; private set; }
        public int GeneralEscapeHpPct { get; private set; }
        public Fix GeneralEscapeSpeed { get; private set; }
        /// <summary>장수 받는 피해 줄이기: 무력 1당 % (× 100 — 100 = 1%p).</summary>
        public int GeneralGuardPerMight { get; private set; }
        public int GeneralGuardMinPct { get; private set; }
        // 진형 특성 · 상성 (R-141)
        public IReadOnlyDictionary<string, Fix> FormationArrowHitPct { get; private set; } = new Dictionary<string, Fix>();
        public IReadOnlyDictionary<string, Fix> FormationEncircleDrainPct { get; private set; } = new Dictionary<string, Fix>();
        public IReadOnlyDictionary<string, Fix> FormationSpeedPct { get; private set; } = new Dictionary<string, Fix>();
        public IReadOnlyDictionary<string, Fix> FormationCounterPp { get; private set; } = new Dictionary<string, Fix>();
        /// <summary>샘플전투2 전용 병종 조정 (R-154, 병종 id → 값). 비면 병종 데이터 그대로.</summary>
        public IReadOnlyDictionary<string, CrowdTroopTuning> TroopTuning { get; private set; } = new Dictionary<string, CrowdTroopTuning>();
        /// <summary>기병 반복 돌격 (R-156): 맞붙은 뒤 빠지기까지 초 (0이면 끔) · 빠지는 시간 · 빠지는 칸.</summary>
        public Fix CavalryContactSeconds { get; private set; }
        public Fix CavalryWithdrawSeconds { get; private set; }
        public Fix CavalryWithdrawCells { get; private set; }
        /// <summary>한 방 피해 흔들림 ± % (R-154, 없으면 0).</summary>
        public int DamageSpreadPct { get; private set; }
        /// <summary>
        /// 샘플전투2 전용 상성 (R-154, "공격태그>방어태그" → %p, 방어 쪽이 "#병종id"면 그 병종만 — 예 "ranged>#infantry" = 1단계 보병만).
        /// 있으면 troopCounters.json 대신 이것 (옛 전투는 troopCounters.json 그대로). 비면 troopCounters.json.
        /// </summary>
        public IReadOnlyDictionary<string, Fix> CounterPp { get; private set; } = new Dictionary<string, Fix>();
        // 공성 (R-140)
        public int GateHp { get; private set; }
        public int GateHitDamage { get; private set; }
        public long GatePlanCost { get; private set; }
        // 높이 (R-143)
        public Fix HeightMeleePpPerLevel { get; private set; }
        public Fix HeightRangePctPerLevel { get; private set; }
        public Fix HeightArrowHitPctPerLevel { get; private set; }
        public Fix HeightMaxLevels { get; private set; }
        public Fix UphillSlowPctPerLevel { get; private set; }
        public Fix DownhillFastPctPerLevel { get; private set; }
        public Fix SlopeMinPct { get; private set; }
        public Fix SlopeMaxPct { get; private set; }
        // 호위 · 물러남 (R-139)
        public int GeneralPullBackHpPct { get; private set; }
        public Fix GeneralPullBackRows { get; private set; }
        // 공성 명령 (R-145)
        public Fix GateBraceRange { get; private set; }
        public Fix GateBracePctPerWeight { get; private set; }
        public Fix GateBraceMaxPct { get; private set; }
        public IReadOnlyDictionary<string, Fix> GateBraceWeight { get; private set; } = new Dictionary<string, Fix>();
        public Fix GateRamMult { get; private set; }
        public int RamCrew { get; private set; }
        public Fix RamCoverPct { get; private set; }
        public Fix LadderClimbSeconds { get; private set; }
        public Fix LadderReach { get; private set; }
        public Fix LadderGuardRange { get; private set; }
        public Fix LadderPushPct { get; private set; }
        public Fix LadderPushMaxPct { get; private set; }
        public int LadderFallDamagePct { get; private set; }
        public Fix LadderMinGateCells { get; private set; }
        public Fix StoneSeconds { get; private set; }
        public Fix StoneRange { get; private set; }
        public int StoneDamage { get; private set; }
        public Fix StoneHitChance { get; private set; }
        public int WallPerCell { get; private set; }
        public Fix ShootWallRangePct { get; private set; }
        public Fix WaitBreachCells { get; private set; }
        // 장수 자리 · 회복 · 지장 · 책사 스킬 (R-144)
        public int GeneralReturnHpPct { get; private set; }
        public Fix GeneralRegenPctPerSec { get; private set; }
        public Fix GeneralRegenCalmSeconds { get; private set; }
        public Fix GeneralRoamRange { get; private set; }
        public int ArmyRetreatTroopPct { get; private set; }
        public Fix SkillGapIntellectPct { get; private set; }
        public Fix SkillGapMinPct { get; private set; }
        public Fix SkillGapMaxPct { get; private set; }
        public int GeneralGuardSharePct { get; private set; }
        // 행군 대열 (R-136)
        public int MarchWidth { get; private set; }
        public Fix MarchTrailStep { get; private set; }
        public int MarchTrailMax { get; private set; }
        public int MarchOffTicks { get; private set; }
        public Fix MarchReformRange { get; private set; }
        public System.Collections.Generic.IReadOnlyList<string> MarchTerrains { get; private set; } = new string[0];
        /// <summary>지름길 지도를 새로 만드는 간격 (틱, R-137).</summary>
        public int FlowRefreshTicks { get; private set; }
        /// <summary>지형 맵의 전장 폭 (R-135): 맵 1칸 = mapFieldWidth ÷ 맵 폭 (24칸 맵 = 2.5, 48칸 맵 = 1.25).</summary>
        public Fix MapFieldWidth { get; private set; }

        /// <summary>전장 크기만 바꾼 사본 (지형 맵 크기에 맞춤, R-133).</summary>
        public CrowdRules WithField(Fix width, Fix depth)
        {
            var c = (CrowdRules)MemberwiseClone();
            c.FieldWidth = width;
            c.FieldDepth = depth;
            return c;
        }
        // 후퇴 (R-132)
        public Fix RetreatSpeed { get; private set; }
        public Fix RetreatMargin { get; private set; }
        // 3×3 배치 (R-131)
        public Fix GridColPitch { get; private set; }
        public Fix GridRowPitch { get; private set; }
        public int GridDefaultCount { get; private set; }
        public int GridMaxCount { get; private set; }
        /// <summary>진형 기준 앞줄 사람 수 (R-157): 50명. 사람이 늘면 뒤로 두꺼워진다.</summary>
        public int FormationFront { get; private set; }
        /// <summary>일자가 두꺼워지는 최대 줄 수 (R-157): 두툼 2배 · 더 두툼 3배, 넘으면 옆으로 넓어짐.</summary>
        public int FormationRows { get; private set; }
        public int SkillGapTicks => System.Math.Max(1, (int)(SkillGapSeconds / TickSeconds).RoundToLong());
        public int RoutEndTicks => System.Math.Max(1, (int)(RoutEndSeconds / TickSeconds).RoundToLong());
        public int EncircleTicks => System.Math.Max(1, (int)(EncircleSeconds / TickSeconds).RoundToLong());
        public int CalmTicks => System.Math.Max(1, (int)(CalmSeconds / TickSeconds).RoundToLong());

        public int AttackTicks => System.Math.Max(1, (int)(AttackSeconds / TickSeconds).RoundToLong());
        public int MaxTicks => (int)(MaxSeconds / TickSeconds).FloorToLong();

        public static CrowdRules From(JObject o)
        {
            const string c = "crowd";
            var rules = new CrowdRules
            {
                TickSeconds = JsonUtil.RequireFix(o, "tickSeconds", c),
                FieldWidth = JsonUtil.RequireFix(o, "fieldWidth", c),
                FieldDepth = JsonUtil.RequireFix(o, "fieldDepth", c),
                SoldierRadius = JsonUtil.RequireFix(o, "soldierRadius", c),
                Spacing = JsonUtil.RequireFix(o, "spacing", c),
                MoveSpeed = JsonUtil.RequireFix(o, "moveSpeed", c),
                AdvanceSpeed = JsonUtil.RequireFix(o, "advanceSpeed", c),
                EngageRange = JsonUtil.RequireFix(o, "engageRange", c),
                AttackReach = JsonUtil.RequireFix(o, "attackReach", c),
                AttackSeconds = JsonUtil.RequireFix(o, "attackSeconds", c),
                AttackJitterTicks = (int)JsonUtil.RequireLong(o, "attackJitterTicks", c),
                SoldierHp = (int)JsonUtil.RequireLong(o, "soldierHp", c),
                GeneralHp = (int)JsonUtil.RequireLong(o, "generalHp", c),
                GeneralBack = JsonUtil.RequireFix(o, "generalBack", c),
                MaxSeconds = JsonUtil.RequireFix(o, "maxSeconds", c),
                ChargeRange = JsonUtil.RequireFix(o, "chargeRange", c),
                ChargeSpeed = JsonUtil.RequireFix(o, "chargeSpeed", c),
                ChargeAdvanceSpeed = JsonUtil.RequireFix(o, "chargeAdvanceSpeed", c),
                ImpactDamage = (int)JsonUtil.RequireLong(o, "impactDamage", c),
                ImpactPush = JsonUtil.RequireFix(o, "impactPush", c),
                CorpsGap = JsonUtil.RequireFix(o, "corpsGap", c),
                HitDamage = (int)JsonUtil.RequireLong(o, "hitDamage", c),
                StatPpPerPoint = JsonUtil.RequireFix(o, "statPpPerPoint", c),
                BaseSpeedStat = JsonUtil.RequireFix(o, "baseSpeedStat", c),
                PolearmReach = JsonUtil.RequireFix(o, "polearmReach", c),
                RangeScale = JsonUtil.RequireFix(o, "rangeScale", c),
                ShootSeconds = JsonUtil.RequireFix(o, "shootSeconds", c),
                ArrowSpeed = JsonUtil.RequireFix(o, "arrowSpeed", c),
                ArrowHitChance = JsonUtil.RequireFix(o, "arrowHitChance", c),
                ArrowDamageScale = JsonUtil.RequireFix(o, "arrowDamageScale", c),
                MountedRadius = JsonUtil.RequireFix(o, "mountedRadius", c),
                MountedImpact = (int)JsonUtil.RequireLong(o, "mountedImpact", c),
                BraceDamage = (int)JsonUtil.RequireLong(o, "braceDamage", c),
                MoraleStart = JsonUtil.RequireFix(o, "moraleStart", c),
                MoraleDeathTotal = JsonUtil.RequireFix(o, "moraleDeathTotal", c),
                FlankDrain = JsonUtil.RequireFix(o, "flankDrain", c),
                RearDrain = JsonUtil.RequireFix(o, "rearDrain", c),
                GeneralLostMorale = JsonUtil.RequireFix(o, "generalLostMorale", c),
                AllyRoutedMorale = JsonUtil.RequireFix(o, "allyRoutedMorale", c),
                EncircleSeconds = JsonUtil.RequireFix(o, "encircleSeconds", c),
                EncircleDrain = JsonUtil.RequireFix(o, "encircleDrain", c),
                CalmSeconds = JsonUtil.RequireFix(o, "calmSeconds", c),
                MoraleRegen = JsonUtil.RequireFix(o, "moraleRegen", c),
                WaverAt = JsonUtil.RequireFix(o, "waverAt", c),
                MoraleLowAt = JsonUtil.RequireFix(o, "moraleLowAt", c),
                MoraleLowPct = JsonUtil.RequireFix(o, "moraleLowPct", c),
                MoraleWaverPct = JsonUtil.RequireFix(o, "moraleWaverPct", c),
                DesertSeconds = JsonUtil.RequireFix(o, "desertSeconds", c),
                InspireSeconds = JsonUtil.RequireFix(o, "inspireSeconds", c),
                InspirePp = JsonUtil.RequireFix(o, "inspirePp", c),
                ConfuseSeconds = JsonUtil.RequireFix(o, "confuseSeconds", c),
                ConfusePp = JsonUtil.RequireFix(o, "confusePp", c),
                DesertMinPct = JsonUtil.RequireFix(o, "desertMinPct", c),
                DesertMaxPct = JsonUtil.RequireFix(o, "desertMaxPct", c),
                WaverSpeed = JsonUtil.RequireFix(o, "waverSpeed", c),
                WaverAttack = JsonUtil.RequireFix(o, "waverAttack", c),
                FleeSpeed = JsonUtil.RequireFix(o, "fleeSpeed", c),
                FlankPp = JsonUtil.RequireFix(o, "flankPp", c),
                RearPp = JsonUtil.RequireFix(o, "rearPp", c),
                FrontDot = JsonUtil.RequireFix(o, "frontDot", c),
                RearDot = JsonUtil.RequireFix(o, "rearDot", c),
                RoutEndSeconds = JsonUtil.RequireFix(o, "routEndSeconds", c),
                SkillGapSeconds = JsonUtil.RequireFix(o, "skillGapSeconds", c),
                SkillContactRange = JsonUtil.RequireFix(o, "skillContactRange", c),
                ArrowRainVolleys = (int)JsonUtil.RequireLong(o, "arrowRainVolleys", c),
                ArrowRainChance = JsonUtil.RequireFix(o, "arrowRainChance", c),
                RetreatSpeed = JsonUtil.RequireFix(o, "retreatSpeed", c),
                MapFieldWidth = JsonUtil.RequireFix(o, "mapFieldWidth", c),
                MarchWidth = (int)JsonUtil.RequireLong(o, "marchWidth", c),
                MarchTrailStep = JsonUtil.RequireFix(o, "marchTrailStep", c),
                MarchTrailMax = (int)JsonUtil.RequireLong(o, "marchTrailMax", c),
                MarchOffTicks = (int)JsonUtil.RequireLong(o, "marchOffTicks", c),
                MarchReformRange = JsonUtil.RequireFix(o, "marchReformRange", c),
                MarchTerrains = JsonUtil.StringList(o, "marchTerrains"),
                FlowRefreshTicks = (int)JsonUtil.RequireLong(o, "flowRefreshTicks", c),
                GeneralHpMight = JsonUtil.RequireFix(o, "generalHpMight", c),
                GeneralFightMight = (int)JsonUtil.RequireLong(o, "generalFightMight", c),
                GeneralMightPp = JsonUtil.RequireFix(o, "generalMightPp", c),
                GeneralFront = JsonUtil.RequireFix(o, "generalFront", c),
                GeneralRear = JsonUtil.RequireFix(o, "generalRear", c),
                GeneralEscapeHpPct = (int)JsonUtil.RequireLong(o, "generalEscapeHpPct", c),
                GeneralEscapeSpeed = JsonUtil.RequireFix(o, "generalEscapeSpeed", c),
                GeneralGuardPerMight = (int)JsonUtil.RequireLong(o, "generalGuardPerMight", c),
                GeneralGuardMinPct = (int)JsonUtil.RequireLong(o, "generalGuardMinPct", c),
                GeneralPullBackHpPct = (int)JsonUtil.RequireLong(o, "generalPullBackHpPct", c),
                GeneralPullBackRows = JsonUtil.RequireFix(o, "generalPullBackRows", c),
                HeightMeleePpPerLevel = JsonUtil.RequireFix(o, "heightMeleePpPerLevel", c),
                HeightRangePctPerLevel = JsonUtil.RequireFix(o, "heightRangePctPerLevel", c),
                HeightArrowHitPctPerLevel = JsonUtil.RequireFix(o, "heightArrowHitPctPerLevel", c),
                HeightMaxLevels = JsonUtil.RequireFix(o, "heightMaxLevels", c),
                UphillSlowPctPerLevel = JsonUtil.RequireFix(o, "uphillSlowPctPerLevel", c),
                DownhillFastPctPerLevel = JsonUtil.RequireFix(o, "downhillFastPctPerLevel", c),
                SlopeMinPct = JsonUtil.RequireFix(o, "slopeMinPct", c),
                SlopeMaxPct = JsonUtil.RequireFix(o, "slopeMaxPct", c),
                GateHp = (int)JsonUtil.RequireLong(o, "gateHp", c),
                GateHitDamage = (int)JsonUtil.RequireLong(o, "gateHitDamage", c),
                GatePlanCost = JsonUtil.RequireLong(o, "gatePlanCost", c),
                GateBraceRange = JsonUtil.RequireFix(o, "gateBraceRange", c),
                GateBracePctPerWeight = JsonUtil.RequireFix(o, "gateBracePctPerWeight", c),
                GateBraceMaxPct = JsonUtil.RequireFix(o, "gateBraceMaxPct", c),
                GateRamMult = JsonUtil.RequireFix(o, "gateRamMult", c),
                RamCrew = (int)JsonUtil.RequireLong(o, "ramCrew", c),
                RamCoverPct = JsonUtil.RequireFix(o, "ramCoverPct", c),
                LadderClimbSeconds = JsonUtil.RequireFix(o, "ladderClimbSeconds", c),
                LadderReach = JsonUtil.RequireFix(o, "ladderReach", c),
                LadderGuardRange = JsonUtil.RequireFix(o, "ladderGuardRange", c),
                LadderPushPct = JsonUtil.RequireFix(o, "ladderPushPct", c),
                LadderPushMaxPct = JsonUtil.RequireFix(o, "ladderPushMaxPct", c),
                LadderFallDamagePct = (int)JsonUtil.RequireLong(o, "ladderFallDamagePct", c),
                LadderMinGateCells = JsonUtil.RequireFix(o, "ladderMinGateCells", c),
                StoneSeconds = JsonUtil.RequireFix(o, "stoneSeconds", c),
                StoneRange = JsonUtil.RequireFix(o, "stoneRange", c),
                StoneDamage = (int)JsonUtil.RequireLong(o, "stoneDamage", c),
                StoneHitChance = JsonUtil.RequireFix(o, "stoneHitChance", c),
                WallPerCell = (int)JsonUtil.RequireLong(o, "wallPerCell", c),
                ShootWallRangePct = JsonUtil.RequireFix(o, "shootWallRangePct", c),
                WaitBreachCells = JsonUtil.RequireFix(o, "waitBreachCells", c),
                GeneralReturnHpPct = (int)JsonUtil.RequireLong(o, "generalReturnHpPct", c),
                GeneralRegenPctPerSec = JsonUtil.RequireFix(o, "generalRegenPctPerSec", c),
                GeneralRegenCalmSeconds = JsonUtil.RequireFix(o, "generalRegenCalmSeconds", c),
                GeneralRoamRange = JsonUtil.RequireFix(o, "generalRoamRange", c),
                ArmyRetreatTroopPct = (int)JsonUtil.RequireLong(o, "armyRetreatTroopPct", c),
                SkillGapIntellectPct = JsonUtil.RequireFix(o, "skillGapIntellectPct", c),
                SkillGapMinPct = JsonUtil.RequireFix(o, "skillGapMinPct", c),
                SkillGapMaxPct = JsonUtil.RequireFix(o, "skillGapMaxPct", c),
                GeneralGuardSharePct = (int)JsonUtil.RequireLong(o, "generalGuardSharePct", c),
                RetreatMargin = JsonUtil.RequireFix(o, "retreatMargin", c),
                GridColPitch = JsonUtil.RequireFix(o, "gridColPitch", c),
                GridRowPitch = JsonUtil.RequireFix(o, "gridRowPitch", c),
                GridDefaultCount = (int)JsonUtil.RequireLong(o, "gridDefaultCount", c),
                GridMaxCount = (int)JsonUtil.RequireLong(o, "gridMaxCount", c),
                FormationFront = (int)JsonUtil.RequireLong(o, "formationFront", c),
                FormationRows = (int)JsonUtil.RequireLong(o, "formationRows", c),
            };
            var rec = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var p in JsonUtil.RequireObject(o, "recommendedFormation", c).Properties())
            {
                string f = (string)p.Value;
                if (!CrowdFormations.IsKnown(f)) throw new System.FormatException(c + ".recommendedFormation." + p.Name + ": unknown formation " + f);
                rec[p.Name] = f;
            }
            rules.RecommendedFormation = rec;
            var obs = new System.Collections.Generic.Dictionary<string, CrowdObstacleSpec>();
            foreach (var p in JsonUtil.RequireObject(o, "obstacles", c).Properties())
            {
                var v = (JObject)p.Value;
                obs[p.Name] = new CrowdObstacleSpec { PerCell = JsonUtil.RequireFix(v, "perCell", c + ".obstacles." + p.Name), Radius = JsonUtil.RequireFix(v, "radius", c + ".obstacles." + p.Name) };
            }
            rules.Obstacles = obs;
            var presets = new System.Collections.Generic.Dictionary<string, GeneralPreset>();
            foreach (var p in JsonUtil.RequireObject(o, "generalPresets", c).Properties())
            {
                var v = (JObject)p.Value;
                string pc = c + ".generalPresets." + p.Name;
                var apt = new System.Collections.Generic.Dictionary<string, string>();
                if (v["aptitude"] is JObject ao) foreach (var ap in ao.Properties()) apt[ap.Name] = (string)ap.Value;
                presets[p.Name] = new GeneralPreset
                {
                    Might = (int)JsonUtil.RequireLong(v, "might", pc),
                    Intellect = (int)JsonUtil.RequireLong(v, "intellect", pc),
                    Role = JsonUtil.RequireString(v, "role", pc),
                    Aptitude = apt,
                };
            }
            rules.GeneralPresets = presets;
            var aptPp = new System.Collections.Generic.Dictionary<string, Fix>();
            foreach (var p in JsonUtil.RequireObject(o, "aptitudePp", c).Properties()) aptPp[p.Name] = JsonUtil.ToFix(p.Value, c + ".aptitudePp." + p.Name);
            rules.AptitudePp = aptPp;
            Dictionary<string, Fix> FixMap(string key)
            {
                var d = new Dictionary<string, Fix>();
                foreach (var p in JsonUtil.RequireObject(o, key, c).Properties()) d[p.Name] = JsonUtil.ToFix(p.Value, c + "." + key + "." + p.Name);
                return d;
            }
            rules.FormationArrowHitPct = FixMap("formationArrowHitPct");
            rules.FormationEncircleDrainPct = FixMap("formationEncircleDrainPct");
            rules.FormationSpeedPct = FixMap("formationSpeedPct");
            rules.FormationCounterPp = FixMap("formationCounterPp");
            rules.GateBraceWeight = FixMap("gateBraceWeight");
            if (o["counterPp"] is Newtonsoft.Json.Linq.JObject) rules.CounterPp = FixMap("counterPp");
            if (o["damageSpreadPct"] != null) rules.DamageSpreadPct = (int)JsonUtil.RequireLong(o, "damageSpreadPct", c);
            if (o["cavalryContactSeconds"] != null)
            {
                rules.CavalryContactSeconds = JsonUtil.RequireFix(o, "cavalryContactSeconds", c);
                rules.CavalryWithdrawSeconds = JsonUtil.RequireFix(o, "cavalryWithdrawSeconds", c);
                rules.CavalryWithdrawCells = JsonUtil.RequireFix(o, "cavalryWithdrawCells", c);
            }
            if (o["troopTuning"] is Newtonsoft.Json.Linq.JObject tt)
            {
                var tun = new Dictionary<string, CrowdTroopTuning>();
                Fix? Opt(Newtonsoft.Json.Linq.JObject x, string k, string where) => x[k] == null ? (Fix?)null : JsonUtil.ToFix(x[k], where + "." + k);
                foreach (var p in tt.Properties())
                {
                    if (p.Name.StartsWith("_") || !(p.Value is Newtonsoft.Json.Linq.JObject x)) continue;
                    string w = c + ".troopTuning." + p.Name;
                    tun[p.Name] = new CrowdTroopTuning { Attack = Opt(x, "attack", w), Defense = Opt(x, "defense", w), AttackSeconds = Opt(x, "attackSeconds", w), ShootSeconds = Opt(x, "shootSeconds", w) };
                }
                rules.TroopTuning = tun;
            }
            return rules;
        }

        public static CrowdRules Load(IDataSource source) => From(JsonUtil.ParseObject(source.ReadText("Rules/" + File)));
    }
}
