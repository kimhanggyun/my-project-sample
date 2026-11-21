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

        public int AttackTicks => System.Math.Max(1, (int)(AttackSeconds / TickSeconds).RoundToLong());
        public int MaxTicks => (int)(MaxSeconds / TickSeconds).FloorToLong();

        public static CrowdRules From(JObject o)
        {
            const string c = "crowd";
            return new CrowdRules
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
            };
        }

        public static CrowdRules Load(IDataSource source) => From(JsonUtil.ParseObject(source.ReadText("Rules/" + File)));
    }
}
