using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Battle.Model.Crowd
{
    /// <summary>스킬 사용 기록 (화면 연출): 쓴 틱 · 부대 · 스킬 · 지점(범위 스킬).</summary>
    public sealed class CrowdSkillEvent
    {
        public int Tick;
        public int Army;
        public string Skill;
        public Vec Point;
        /// <summary>범위 스킬이 실제로 떨어지는 틱 (전조 뒤). 아니면 Tick.</summary>
        public int LandTick;
    }

    /// <summary>
    /// 샘플전투2 스킬 (R-130, 기존 skills.json의 기본 스킬 다섯을 옮김). 부대마다 쓸 수 있는 횟수 = usesPerBattle, 다시 쓰기까지 skillGapSeconds.
    /// - warCry (돌격 함성): durationSec 동안 공격 +pp%p, 쓸 때 가까이(contactRange) 맞붙은 적 부대 사기 −amount.
    /// - rally (고무): 자기와 allyRadius 안 같은 편 부대 사기 +amount.
    /// - holdTheLine (철벽): durationSec 동안 방어 +defensePp%p (맞는 피해 줄임), 사기 깎임 × (1 + moraleLossPct%).
    /// - forcedMarch (강행군): durationSec 동안 걸음 +movePct%, 끝날 때 사기 endMorale.
    /// - arrowRain (화살비, 궁병만): range 안 지점, telegraphSec 뒤 radius 안 병사(편 상관없이)에게 arrowRainVolleys번 화살 (각 arrowRainChance).
    /// 스킬 정의 · 수치는 skills.json 그대로, 샘플전투2에만 있는 값(volleys · chance · 다시 쓰기 간격 · contactRange)은 crowd.json.
    /// </summary>
    public sealed partial class CrowdBattle
    {
        public const string WarCry = "warCry", Rally = "rally", HoldTheLine = "holdTheLine", ForcedMarch = "forcedMarch", ArrowRain = "arrowRain";
        /// <summary>샘플전투2가 지금 아는 스킬 (부대에 주는 순서).</summary>
        public static readonly string[] KnownSkills = { WarCry, Rally, HoldTheLine, ForcedMarch, ArrowRain, FireAttack, Rockfall };
        /// <summary>지점을 고르는 스킬 (화살비 · 화계 · 낙석).</summary>
        public static bool IsAreaSkill(string id) => id == ArrowRain || id == FireAttack || id == Rockfall;

        private IReadOnlyDictionary<string, SkillDef> _skills;
        /// <summary>스킬 사용 기록 (화면 연출, 오래된 것은 지움).</summary>
        public readonly List<CrowdSkillEvent> SkillEvents = new List<CrowdSkillEvent>();
        private readonly List<(int army, Vec point, int landTick)> _pendingRain = new List<(int, Vec, int)>();

        /// <summary>스킬 데이터를 넣고 부대마다 쓸 수 있는 스킬 · 횟수를 정한다 (궁병 부대만 화살비).</summary>
        public void UseSkillData(IReadOnlyDictionary<string, SkillDef> skills)
        {
            _skills = skills;
            if (skills == null) return;
            foreach (var a in Armies)
            {
                a.SkillUses.Clear();
                foreach (var id in KnownSkills)
                {
                    if (!skills.TryGetValue(id, out var def)) continue;
                    if (id == ArrowRain && !a.Troop.Ranged) continue;
                    if (!MeetsIntellect(a, def)) continue; // 화계 · 낙석: 지력 조건 (requires)
                    a.SkillUses[id] = def.UsesPerBattle;
                }
            }
        }

        private static bool MeetsIntellect(CrowdArmy a, SkillDef def)
        {
            if (def.Requires == null || a.GeneralInfo == null) return true;
            foreach (var q in def.Requires)
                if (q.Stat == "intellect" && Fix.FromInt(a.GeneralInfo.Intellect) < q.Min) return false;
            return true;
        }

        /// <summary>그 자리 둘레에 우리 편 병사가 있나 (AI 화계).</summary>
        private bool FriendsNear(int team, Vec p, Fix range)
        {
            Fix r2 = range * range;
            foreach (var s in Soldiers) if (s.Alive && s.Team == team && Vec.DistanceSquared(s.Pos, p) <= r2) return true;
            return false;
        }

        public SkillDef SkillDef(string id) => _skills != null && _skills.TryGetValue(id, out var d) ? d : null;

        /// <summary>지금 쓸 수 있나 (횟수 남음 · 명령 받는 부대 · 다시 쓰기 간격 · 범위 스킬은 사거리 안).</summary>
        public bool CanUseSkill(int army, string skill, Vec? point = null)
        {
            if (IsOver || army < 0 || army >= Armies.Count) return false;
            var a = Armies[army];
            var def = SkillDef(skill);
            if (def == null || !a.Leading || !a.SkillUses.TryGetValue(skill, out var left) || left <= 0) return false;
            if (Tick - a.LastSkillTick < SkillGapTicksFor(a)) return false; // 지력이 높으면 자주 (R-144)
            if (IsAreaSkill(skill))
            {
                if (point == null) return true; // 지점은 아직 (고르는 중)
                Fix range = def.Range ?? Fix.FromInt(14);
                if ((point.Value - a.Centroid()).Length > range) return false;
                if (skill == FireAttack && !CanFire(point.Value)) return false; // 숲 · 풀만 탐 (R-164)
                if (skill == Rockfall && !CanRock(a, point.Value)) return false; // 높은 곳에서 낮은 곳으로만
            }
            return true;
        }

        /// <summary>스킬을 쓴다 (못 쓰면 false). 범위 스킬은 point가 필요.</summary>
        private bool UseSkillCore(int army, string skill, Vec? point)
        {
            if (!CanUseSkill(army, skill, point)) return false;
            if (IsAreaSkill(skill) && point == null) return false;
            var a = Armies[army];
            var def = SkillDef(skill);
            a.SkillUses[skill]--;
            a.LastSkillTick = Tick;
            var ev = new CrowdSkillEvent { Tick = Tick, Army = army, Skill = skill, Point = point ?? a.Centroid(), LandTick = Tick };
            int Ticks(Fix sec) => System.Math.Max(1, (int)(sec / Rules.TickSeconds).RoundToLong());
            foreach (var e in def.Effects)
            {
                switch (e.Type)
                {
                    case "attackBuff":
                        a.AttackBuffPp = e.Fix("pp");
                        a.AttackBuffUntil = Tick + Ticks(e.Fix("durationSec"));
                        break;
                    case "contactMoraleDamage":
                        var me = a.Centroid();
                        Fix reach2 = Rules.SkillContactRange * Rules.SkillContactRange;
                        foreach (var o in Armies)
                            if (o.Team != a.Team && Standing(o) && Vec.DistanceSquared(o.Centroid(), me) <= reach2)
                            {
                                LoseMorale(o, e.Fix("amount"));
                                o.ConfusedUntil = Tick + Ticks(Rules.ConfuseSeconds); // 함성 혼란 (R-152)
                            }
                        break;
                    case "morale":
                        Fix r2 = e.Fix("allyRadius") * e.Fix("allyRadius");
                        var c = a.Centroid();
                        foreach (var o in Armies)
                            if (o.Team == a.Team && !o.Routed && Standing(o) && (o == a || Vec.DistanceSquared(o.Centroid(), c) <= r2))
                            {
                                o.Morale = Fix.Min(Rules.MoraleStart, o.Morale + e.Fix("amount"));
                                o.InspiredUntil = Tick + Ticks(Rules.InspireSeconds); // 사기 충만 (R-152)
                            }
                        break;
                    case "fortify":
                        a.DefenseBuffPp = e.Fix("defensePp");
                        a.MoraleLossPct = e.Fix("moraleLossPct");
                        a.FortifyUntil = Tick + Ticks(e.Fix("durationSec"));
                        break;
                    case "forcedMarch":
                        a.MoveBuffPct = e.Fix("movePct");
                        a.MarchEndMorale = e.Fix("endMorale");
                        a.MarchUntil = Tick + Ticks(e.Fix("durationSec"));
                        break;
                    case "ignite": // 화계 (R-164): 그 둘레 숲 · 풀 칸에 불 (옛 전투의 바람 · 날씨 조건은 보지 않음)
                        Ignite(point.Value);
                        break;
                    case "rockfall": // 낙석 (R-164): telegraphSec 뒤 떨어짐
                        ev.LandTick = Tick + Ticks(def.TelegraphSec);
                        _pendingRocks.Add((army, point.Value, ev.LandTick));
                        break;
                    case "areaDamage":
                        ev.LandTick = Tick + Ticks(def.TelegraphSec);
                        _pendingRain.Add((army, point.Value, ev.LandTick));
                        // 하늘을 덮는 화살 (보이기만, 피해는 떨어질 때 한꺼번에)
                        int shown = System.Math.Min(60, a.AliveCount);
                        Fix rad = def.Radius;
                        foreach (var s in a.Soldiers)
                        {
                            if (shown-- <= 0) break;
                            if (!s.Alive || s.IsGeneral) continue;
                            var to = point.Value + new Vec(rad * (_rng.NextFix01() * 2 - Fix.OneValue), rad * (_rng.NextFix01() * 2 - Fix.OneValue));
                            Arrows.Add(new CrowdArrow { ShooterId = s.Id, TargetId = -1, From = s.Pos, To = to, FireTick = Tick, ArriveTick = ev.LandTick, Damage = 0 });
                        }
                        break;
                }
            }
            SkillEvents.Add(ev);
            return true;
        }

        /// <summary>매 틱: 화살비가 떨어지고, 끝난 효과를 거두고 (강행군 끝 사기), 오래된 기록을 지운다.</summary>
        private void StepSkills()
        {
            for (int k = 0; k < _pendingRain.Count; k++)
            {
                var (army, point, land) = _pendingRain[k];
                if (land > Tick) continue;
                _pendingRain.RemoveAt(k--);
                var a = Armies[army];
                var def = SkillDef(ArrowRain);
                Fix r2 = def.Radius * def.Radius;
                foreach (var s in Soldiers)
                {
                    if (!s.Alive || Vec.DistanceSquared(s.Pos, point) > r2) continue;
                    var tTroop = Armies[s.Army].Troop;
                    for (int v = 0; v < Rules.ArrowRainVolleys; v++)
                        if (_rng.Chance(Rules.ArrowRainChance)) _damage[s.Id] += HitDamage(a.Troop, tTroop);
                    if (s.Team != a.Team) NoteHit(Armies[s.Army], 0);
                }
            }
            foreach (var a in Armies)
            {
                if (a.MarchUntil >= 0 && Tick >= a.MarchUntil)
                {
                    a.MarchUntil = -1;
                    a.MoveBuffPct = Fix.Zero;
                    if (!a.Routed) a.Morale = Fix.Max(Fix.Zero, a.Morale + a.MarchEndMorale);
                }
            }
            if (SkillEvents.Count > 0 && SkillEvents[0].LandTick < Tick - 50) SkillEvents.RemoveAt(0);
        }

        /// <summary>공격 쪽 버프 − 방어 쪽 버프 (%p).</summary>
        private Fix BuffPp(CrowdArmy atk, CrowdArmy def)
        {
            Fix pp = Fix.Zero;
            if (atk.AttackBuffUntil > Tick) pp += atk.AttackBuffPp;
            if (def.FortifyUntil > Tick) pp -= def.DefenseBuffPp;
            return pp;
        }

        /// <summary>걸음 배수 (강행군).</summary>
        private Fix MoveMult(CrowdArmy a) => a.MarchUntil > Tick ? Fix.OneValue + a.MoveBuffPct / 100 : Fix.OneValue;

        /// <summary>사기를 깎는다 (철벽이면 moraleLossPct만큼 덜).</summary>
        private void LoseMorale(CrowdArmy a, Fix amount)
        {
            if (a.FortifyUntil > Tick) amount = amount * (Fix.OneValue + a.MoraleLossPct / 100);
            a.Morale -= amount;
        }

        /// <summary>
        /// AI 스킬 (적 전진 · 우리 위임, R-132): 사기가 낮으면 고무, 맞붙기 시작하면 돌격 함성 · 철벽,
        /// 궁병은 가까운 적 부대가 사거리 안이면 그 가운데에 화살비.
        /// </summary>
        private void AiSkills()
        {
            foreach (var a in Armies)
            {
                if (!a.Leading || !AiControls(a)) continue;
                if (a.Morale <= Fix.FromInt(40) && UseSkill(a.Id, Rally)) continue;
                bool engaged = a.LastHitTick >= Tick - 5;
                if (engaged && UseSkill(a.Id, WarCry)) continue;
                if (engaged && a.Morale <= Fix.FromInt(60) && UseSkill(a.Id, HoldTheLine)) continue;
                int tgt = NearestEnemyArmy(a.Id);
                if (tgt >= 0)
                {
                    var at = Armies[tgt].Centroid();
                    // 낙석 · 화계 (R-164): 쓸 수 있는 자리면, 화계는 그 둘레에 우리 병사가 없을 때만 (자기 매복 부대를 태우지 않게)
                    if (a.SkillUses.ContainsKey(Rockfall) && UseSkill(a.Id, Rockfall, at)) continue;
                    if (a.SkillUses.ContainsKey(FireAttack) && !FriendsNear(a.Team, at, Rules.FireStartRadius * 3) && UseSkill(a.Id, FireAttack, at)) continue;
                    if (a.SkillUses.ContainsKey(ArrowRain)) UseSkill(a.Id, ArrowRain, at);
                }
            }
        }
    }
}
