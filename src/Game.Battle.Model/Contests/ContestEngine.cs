using System.Collections.Generic;
using Game.Data.Defs;
using Game.Foundation.Math;
using Game.Foundation.Random;

namespace Game.Battle.Model.Contests
{
    // 대결 엔진 (battle.md 2.7 일기토, 2.7.6 논쟁, 2.7.7 공통 엔진).
    // 행동 목록·결과표·전투 영향은 데이터(contests.json). 새 대결은 데이터만 추가한다.

    public static class ContestBase
    {
        public const string Attack = "attack";
        public const string Evade = "evade";
        public const string Charge = "charge";
        public const string Bow = "bow";
        public const string Retreat = "retreat";
        public const string Special = ContestDef.Special;
    }

    /// <summary>대결 참가자 상태. 체력(평정심)과 기운(통찰).</summary>
    public sealed class ContestFighter
    {
        public string Id { get; internal set; }
        public Fix Stat { get; internal set; }
        public string Temperament { get; internal set; }
        public Fix Resource { get; internal set; }
        public Fix Charge { get; internal set; }
        public bool Retreated { get; internal set; }
        public bool Withdrew { get; internal set; }
        public bool Fallen => Resource <= Fix.Zero;
        /// <summary>주는 피해 % (장수 특성 contestDamagePct, R-67). 0이면 보정 없음.</summary>
        public Fix DamagePct { get; set; }

        public ContestFighter(string id, Fix stat, string temperament)
        {
            Id = id;
            Stat = stat;
            Temperament = temperament;
        }
    }

    public static class ContestEnds
    {
        /// <summary>한쪽 체력 0.</summary>
        public const string Fall = "fall";
        /// <summary>한쪽 퇴각 성공.</summary>
        public const string Retreat = "retreat";
        /// <summary>둘 다 물러남, 또는 동시에 쓰러짐, 또는 최대 라운드에 체력 같음.</summary>
        public const string Draw = "draw";
        /// <summary>최대 라운드: 체력이 많은 쪽 승리.</summary>
        public const string Win = "win";
    }

    public sealed class ContestRound
    {
        public int Number { get; internal set; }
        public string ActionA { get; internal set; }
        public string ActionB { get; internal set; }
        public Fix ResourceA { get; internal set; }
        public Fix ResourceB { get; internal set; }
        public Fix ChargeA { get; internal set; }
        public Fix ChargeB { get; internal set; }
        /// <summary>이 라운드에 회심(큰 피해)을 맞았나 (화면 연출용, 결과 계산에는 쓰지 않는다).</summary>
        public bool CritOnA { get; internal set; }
        public bool CritOnB { get; internal set; }
    }

    public sealed class ContestResult
    {
        public string ContestId { get; internal set; }
        /// <summary><see cref="ContestEnds"/>.</summary>
        public string End { get; internal set; }
        /// <summary>"A" 또는 "B". 무승부·퇴각이면 null.</summary>
        public string Winner { get; internal set; }
        /// <summary>퇴각한 쪽 ("A"/"B"), 없으면 null.</summary>
        public string Retreater { get; internal set; }
        public IReadOnlyList<ContestRound> Rounds { get; internal set; }
        public ContestFighter A { get; internal set; }
        public ContestFighter B { get; internal set; }
    }

    /// <summary>
    /// 대결 한 판. 매 라운드 두 행동을 동시에 받아 결과표대로 적용한다.
    /// 피해 = 기본 × 배수 × 능력치 보정(차이 × 계수, 상한) × 무작위 폭 × 회심. 난수가 없으면(규칙 확인용) 무작위·회심 없음.
    /// </summary>
    public sealed class ContestSession
    {
        private readonly IRandom _rng;
        private readonly List<ContestRound> _rounds = new List<ContestRound>();

        public ContestDef Def { get; }
        public ContestFighter A { get; }
        public ContestFighter B { get; }
        public int Round => _rounds.Count;
        /// <summary>지난 라운드들 (화면 기록용).</summary>
        public IReadOnlyList<ContestRound> Rounds => _rounds;
        public ContestResult Result { get; private set; }
        public bool IsOver => Result != null;

        private bool _crit;

        public ContestSession(ContestDef def, ContestFighter a, ContestFighter b, IRandom rng)
        {
            Def = def;
            A = a;
            B = b;
            _rng = rng;
            a.Resource = def.Start;
            b.Resource = def.Start;
            a.Charge = Fix.Zero;
            b.Charge = Fix.Zero;
        }

        /// <summary>지금 고를 수 있는 행동 id (필살은 기운이 가득 찼을 때만).</summary>
        public List<string> Available(ContestFighter f) => Def.AvailableFor(f.Charge);

        public bool CanChoose(ContestFighter f, string actionId) => Available(f).Contains(actionId);

        /// <summary>한 라운드. 고를 수 없는 행동이면 false (아무것도 바뀌지 않음).</summary>
        public bool Play(string actionA, string actionB)
        {
            if (IsOver || !CanChoose(A, actionA) || !CanChoose(B, actionB)) return false;
            string ba = Def.Action(actionA).Base, bb = Def.Action(actionB).Base;
            _crit = false;
            ApplyCell(A, B, ba, bb);
            bool critOnA = _crit;
            _crit = false;
            ApplyCell(B, A, bb, ba);
            bool critOnB = _crit;
            A.Resource = Fix.Max(Fix.Zero, A.Resource);
            B.Resource = Fix.Max(Fix.Zero, B.Resource);
            _rounds.Add(new ContestRound
            {
                Number = _rounds.Count + 1, ActionA = actionA, ActionB = actionB,
                ResourceA = A.Resource, ResourceB = B.Resource, ChargeA = A.Charge, ChargeB = B.Charge,
                CritOnA = critOnA, CritOnB = critOnB,
            });
            CheckEnd();
            return true;
        }

        private void ApplyCell(ContestFighter me, ContestFighter them, string mine, string theirs)
        {
            foreach (var e in Def.Outcomes[mine][theirs])
            {
                switch (e.Type)
                {
                    case "takeDamage": me.Resource -= Damage(Def.BaseDamage * e.Fix("mult"), me, them, true); break;
                    case "takeSpecial": me.Resource -= Damage(Def.SpecialDamage, me, them, true); break;
                    case "takeStatDifference":
                        // R-22: 공격 대 공격 → 능력치가 높은 쪽이 차이만큼 (무작위 폭 적용 전 기준)
                        if (them.Stat > me.Stat) me.Resource -= Vary(them.Stat - me.Stat, false);
                        break;
                    case "energy": me.Charge = Fix.Min(Def.SpecialCharge, me.Charge + e.Fix("amount")); break;
                    case "spendEnergy": me.Charge = Fix.Zero; break;
                    case "retreat": me.Retreated = true; break;
                    case "withdraw": me.Withdrew = true; break;
                    case "halveOrFall":
                        // R-21: 퇴각 vs 활쏘기 → 체력 > 절반이면 −절반, 절반 이하면 0
                        Fix half = Def.Start / 2;
                        me.Resource = me.Resource > half ? me.Resource - half : Fix.Zero;
                        break;
                    default: throw new Data.Json.DataException($"대결 '{Def.Id}': 알 수 없는 결과 '{e.Type}'.");
                }
            }
        }

        /// <summary>능력치 보정: 상대가 강하면 더 아프다 (차이 × 계수, ±상한 %).</summary>
        private Fix Damage(Fix baseDamage, ContestFighter me, ContestFighter them, bool crit)
        {
            Fix pct = Fix.Clamp((them.Stat - me.Stat) * Def.StatInfluencePctPerPoint, -Def.StatInfluenceMaxPct, Def.StatInfluenceMaxPct);
            pct += them.DamagePct; // 때리는 쪽 특성 (R-67)
            return Vary(baseDamage * (Fix.Hundred + pct) / Fix.Hundred, crit);
        }

        private Fix Vary(Fix v, bool crit)
        {
            if (_rng == null) return v;
            int r = _rng.NextInt(-Def.VariancePct, Def.VariancePct + 1);
            v = v * Fix.FromInt(100 + r) / Fix.Hundred;
            if (crit && _rng.Chance(Def.CritChance)) { v = v * Def.CritMultiplier; _crit = true; }
            return v;
        }

        private void CheckEnd()
        {
            bool af = A.Fallen, bf = B.Fallen;
            if (af || bf)
            {
                if (af && bf) Finish(ContestEnds.Draw, null, null);
                else Finish(ContestEnds.Fall, af ? "B" : "A", null);
                return;
            }
            if (A.Withdrew && B.Withdrew) { Finish(ContestEnds.Draw, null, null); return; }
            if (A.Retreated || B.Retreated)
            {
                Finish(ContestEnds.Retreat, null, A.Retreated ? "A" : "B");
                return;
            }
            if (_rounds.Count >= Def.MaxRounds)
            {
                int cmp = A.Resource.CompareTo(B.Resource);
                Finish(cmp == 0 ? ContestEnds.Draw : ContestEnds.Win, cmp == 0 ? null : (cmp > 0 ? "A" : "B"), null);
            }
        }

        private void Finish(string end, string winner, string retreater) =>
            Result = new ContestResult { ContestId = Def.Id, End = end, Winner = winner, Retreater = retreater, Rounds = _rounds, A = A, B = B };
    }

    /// <summary>대결 행동 고르기. 플레이어 입력·기록 재생·AI가 같은 경계를 쓴다.</summary>
    public interface IContestChooser
    {
        string Choose(ContestSession session, ContestFighter me, ContestFighter opponent);
    }

    /// <summary>기록된 선택을 차례로 낸다. 기록이 끝났거나 고를 수 없는 행동이면 대신 고르는 쪽(AI)에 맡긴다.</summary>
    public sealed class RecordedChooser : IContestChooser
    {
        private readonly IReadOnlyList<string> _choices;
        private readonly IContestChooser _fallback;
        private int _next;

        public RecordedChooser(IReadOnlyList<string> choices, IContestChooser fallback)
        {
            _choices = choices ?? new string[0];
            _fallback = fallback;
        }

        public string Choose(ContestSession s, ContestFighter me, ContestFighter opp)
        {
            if (_next < _choices.Count)
            {
                var c = _choices[_next++];
                if (s.CanChoose(me, c)) return c;
            }
            return _fallback.Choose(s, me, opp);
        }
    }

    /// <summary>
    /// 대결 AI (ai.md 9장): 상태(체력·기운)와 성향으로 행동 가중치를 정하고 시드 난수로 고른다.
    /// 기운이 가득 차면 필살, 체력이 적으면 퇴각·회피, 상대 기운이 가득 차면 활쏘기·퇴각 경향.
    /// </summary>
    public sealed class AiContestChooser : IContestChooser
    {
        private readonly AiRules _ai;
        private readonly IRandom _rng;

        public AiContestChooser(AiRules ai, IRandom rng)
        {
            _ai = ai;
            _rng = rng;
        }

        public string Choose(ContestSession s, ContestFighter me, ContestFighter opp)
        {
            var weights = new List<(string id, int w)>();
            int total = 0;
            foreach (var id in s.Available(me))
            {
                string b = s.Def.Action(id).Base;
                int w = Weight(s, b, me, opp);
                if (w <= 0) continue;
                weights.Add((id, w));
                total += w;
            }
            if (total == 0) return s.Available(me)[0];
            int pick = _rng.NextInt(total);
            foreach (var (id, w) in weights)
            {
                if (pick < w) return id;
                pick -= w;
            }
            return weights[weights.Count - 1].id;
        }

        private int Weight(ContestSession s, string b, ContestFighter me, ContestFighter opp)
        {
            int w = b == ContestBase.Special ? _ai.ContestFullChargeSpecialWeight : (_ai.ContestBaseWeights.TryGetValue(b, out var bw) ? bw : 0);
            if (me.Resource <= _ai.ContestLowResourceAtOrBelow)
            {
                if (b == ContestBase.Retreat) w += _ai.ContestLowResourceRetreatBonus;
                if (b == ContestBase.Evade) w += _ai.ContestLowResourceEvadeBonus;
            }
            if (opp.Charge >= s.Def.SpecialCharge)
            {
                if (b == ContestBase.Bow) w += _ai.ContestOpponentFullChargeBowBonus;
                if (b == ContestBase.Retreat) w += _ai.ContestOpponentFullChargeRetreatBonus;
            }
            if (me.Temperament != null && _ai.ContestTemperamentBonus.TryGetValue(me.Temperament, out var row) && row.TryGetValue(b, out var tb))
                w += tb;
            return w;
        }
    }

    /// <summary>대결 발생 판정 (야전 시작 직전, battle.md 2.7): 능력치 차이가 기준 이내면 확률로. 거부 성향이면 없음.</summary>
    public static class ContestTrigger
    {
        public static bool Occurs(ContestDef def, Fix statA, Fix statB, string temperamentA, string temperamentB, IRandom rng)
        {
            foreach (var t in def.RefuseTemperaments)
                if (t == temperamentA || t == temperamentB) return false;
            Fix diff = statA > statB ? statA - statB : statB - statA;
            return diff <= def.TriggerMaxStatDifference && rng.Chance(def.TriggerChance);
        }
    }

    /// <summary>대결을 끝까지 돌린다.</summary>
    public static class ContestRunner
    {
        public static ContestResult Run(ContestSession s, IContestChooser chooserA, IContestChooser chooserB)
        {
            while (!s.IsOver)
            {
                string a = chooserA.Choose(s, s.A, s.B);
                string b = chooserB.Choose(s, s.B, s.A);
                s.Play(a, b);
            }
            return s.Result;
        }
    }
}
