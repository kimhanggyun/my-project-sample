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
        public Fix RoutAt { get; private set; }
        public Fix WaverSpeed { get; private set; }
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
        // 행군 대열 (R-136)
        public int MarchWidth { get; private set; }
        public Fix MarchTrailStep { get; private set; }
        public int MarchTrailMax { get; private set; }
        public int MarchOffTicks { get; private set; }
        public Fix MarchReformRange { get; private set; }
        public System.Collections.Generic.IReadOnlyList<string> MarchTerrains { get; private set; } = new string[0];
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
                RoutAt = JsonUtil.RequireFix(o, "routAt", c),
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
                RetreatMargin = JsonUtil.RequireFix(o, "retreatMargin", c),
                GridColPitch = JsonUtil.RequireFix(o, "gridColPitch", c),
                GridRowPitch = JsonUtil.RequireFix(o, "gridRowPitch", c),
                GridDefaultCount = (int)JsonUtil.RequireLong(o, "gridDefaultCount", c),
                GridMaxCount = (int)JsonUtil.RequireLong(o, "gridMaxCount", c),
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
            return rules;
        }

        public static CrowdRules Load(IDataSource source) => From(JsonUtil.ParseObject(source.ReadText("Rules/" + File)));
    }
}
