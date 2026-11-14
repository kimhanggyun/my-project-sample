using System.Collections.Generic;
using Game.Battle.Model.Contests;
using Game.Battle.Model.Events;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Flow
{
    /// <summary>숨은 보상 판정에 쓰는 것: 스텝, 전투 설정·결과(재현 가능한 것만), 진행 상태.</summary>
    public sealed class SecretContext
    {
        public DefinitionSet Defs { get; internal set; }
        public CampaignState State { get; internal set; }
        public StepDef Step { get; internal set; }
        public SortiePlan Plan { get; internal set; }
        public BattleResult Result { get; internal set; }
        public bool Won { get; internal set; }
        /// <summary>찾은 시각 (도감 기록).</summary>
        public long NowUnixSeconds { get; internal set; }
    }

    /// <summary>숨은 보상 조건 하나 (secrets.json condition.all[].type). 새 조건은 구현 추가 + 등록.</summary>
    public interface ISecretCondition
    {
        string Type { get; }
        bool IsMet(SecretContext c, SkillPart p);
    }

    /// <summary>보상을 받을 장수 고르기 (reward.to). 없으면 null.</summary>
    public interface ISecretRecipient
    {
        string Type { get; }
        string Pick(SecretContext c, SecretDef s);
    }

    public sealed class FoundSecret
    {
        public string SecretId { get; internal set; }
        public string GeneralId { get; internal set; }
        /// <summary>배운 스킬·특성 (요구 능력치가 모자라면 보관함으로 → StoredAsBook).</summary>
        public string Skill { get; internal set; }
        public string Trait { get; internal set; }
        public bool StoredAsBook { get; internal set; }
    }

    public sealed class CodexEntry
    {
        /// <summary>찾았으면 id, 못 찾았으면 null ("???").</summary>
        public string SecretId { get; internal set; }
        public bool Found { get; internal set; }
        /// <summary>못 찾은 칸에 보이는 힌트 키.</summary>
        public string HintKey { get; internal set; }
        /// <summary>분류 (못 찾아도 보인다).</summary>
        public string Category { get; internal set; }
        /// <summary>찾은 기록 (찾았고 기록이 있을 때만).</summary>
        public SecretFind Find { get; internal set; }
    }

    /// <summary>장수 도감 한 칸 (13 시안): 가졌는지 · 얻는 곳 (계산만, 새 저장 없음).</summary>
    public sealed class GeneralCodexEntry
    {
        public string GeneralId { get; internal set; }
        public string Grade { get; internal set; }
        public bool Owned { get; internal set; }
        /// <summary>얻는 곳: gacha (모집 목록) · start (처음 지급) · story (스토리 합류 · 등용).</summary>
        public List<string> Sources { get; } = new List<string>();
    }

    public sealed class SecretRegistry
    {
        private readonly Dictionary<string, ISecretCondition> _conditions = new Dictionary<string, ISecretCondition>();
        private readonly Dictionary<string, ISecretRecipient> _recipients = new Dictionary<string, ISecretRecipient>();

        public SecretRegistry Register(ISecretCondition c) { _conditions[c.Type] = c; return this; }
        public SecretRegistry Register(ISecretRecipient r) { _recipients[r.Type] = r; return this; }
        public bool HasCondition(string t) => _conditions.ContainsKey(t);
        public bool HasRecipient(string t) => _recipients.ContainsKey(t);
        internal ISecretCondition Condition(string t) => _conditions[t];
        internal ISecretRecipient Recipient(string t) => _recipients[t];

        public static SecretRegistry WithBuiltIns() => new SecretRegistry()
            .Register(new LossRatioAtMost())
            .Register(new WeatherIs())
            .Register(new TimeOfDayIs())
            .Register(new NoPlayerTroopTag())
            .Register(new DuelWonByFall())
            .Register(new FireHitEnemyCorpsAtLeast())
            .Register(new CommanderRecipient())
            .Register(new DuelWinnerRecipient())
            .Register(new CasterOfRecipient());
    }

    /// <summary>
    /// 숨은 보상 (secrets.md): 전투 결과·스텝 조건을 만족하면 그 전투에 나간 장수에게 스킬·특성.
    /// 요구 능력치가 모자라거나 이미 가졌으면 스킬북·특성서로 보관. 대부분 한 번만. 도감에 기록.
    /// 판정은 BattleResult·설정만 쓰므로 서버 재현 결과로 같은 판정을 할 수 있다.
    /// </summary>
    public sealed class SecretEngine
    {
        private readonly SecretRegistry _reg;

        public SecretEngine(SecretRegistry registry = null) { _reg = registry ?? SecretRegistry.WithBuiltIns(); }

        public List<FoundSecret> Evaluate(SecretContext c)
        {
            var found = new List<FoundSecret>();
            foreach (var s in c.Defs.Secrets)
            {
                if (s.Once && c.State.FoundSecrets.Contains(s.Id)) continue;
                if (s.Step != null && s.Step != c.Step.Id) continue;
                if (s.Result == "victory" && !c.Won) continue;
                bool ok = true;
                foreach (var p in s.Conditions)
                    if (!_reg.HasCondition(p.Type) || !_reg.Condition(p.Type).IsMet(c, p)) { ok = false; break; }
                if (!ok || !_reg.HasRecipient(s.To)) continue;
                string gid = _reg.Recipient(s.To).Pick(c, s);
                var g = gid == null ? null : c.State.FindGeneral(gid);
                if (g == null) continue;
                found.Add(Grant(c, s, g));
                if (!c.State.FoundSecrets.Contains(s.Id)) c.State.FoundSecrets.Add(s.Id);
                c.State.SecretFinds[s.Id] = new SecretFind { StepId = c.Step?.Id, GeneralId = g.GeneralId, AtUnixSeconds = c.NowUnixSeconds };
            }
            return found;
        }

        private static FoundSecret Grant(SecretContext c, SecretDef s, GeneralState g)
        {
            var f = new FoundSecret { SecretId = s.Id, GeneralId = g.GeneralId };
            if (s.RewardSkill != null)
            {
                f.Skill = s.RewardSkill;
                var skill = c.Defs.Skills[s.RewardSkill];
                if (!g.LearnedSkills.Contains(skill.Id) && Meets(c.Defs, g, skill)) g.LearnedSkills.Add(skill.Id);
                else { Add(c.State.SkillBooks, skill.Id); f.StoredAsBook = true; }
            }
            if (s.RewardTrait != null)
            {
                f.Trait = s.RewardTrait;
                if (!g.LearnedTraits.Contains(s.RewardTrait)) g.LearnedTraits.Add(s.RewardTrait);
                else { Add(c.State.TraitBooks, s.RewardTrait); f.StoredAsBook = true; }
            }
            return f;
        }

        private static bool Meets(DefinitionSet d, GeneralState g, SkillDef skill)
        {
            if (skill.Requires.Count == 0) return true;
            int met = 0;
            foreach (var r in skill.Requires) if (CorpsValidator.EffectiveStat(d, g, r.Stat) >= r.Min) met++;
            return skill.RequiresAny ? met > 0 : met == skill.Requires.Count;
        }

        private static void Add(Dictionary<string, long> books, string id)
        {
            books.TryGetValue(id, out var n);
            books[id] = n + 1;
        }

        /// <summary>데이터 검사: 등록되지 않은 조건·받는 장수 종류 (데이터 검사 테스트에서 부른다).</summary>
        public List<string> Validate(DefinitionSet d)
        {
            var errors = new List<string>();
            foreach (var s in d.Secrets)
            {
                if (!_reg.HasRecipient(s.To)) errors.Add($"숨은 보상 '{s.Id}': 등록되지 않은 받는 장수 '{s.To}'.");
                foreach (var p in s.Conditions)
                    if (!_reg.HasCondition(p.Type)) errors.Add($"숨은 보상 '{s.Id}': 등록되지 않은 조건 '{p.Type}'.");
                if (s.To == "casterOf" && s.CasterSkill == null) errors.Add($"숨은 보상 '{s.Id}': casterOf에는 casterSkill이 필요합니다.");
            }
            return errors;
        }

        /// <summary>도감: 찾은 것은 id, 못 찾은 것은 "???"(null)와 힌트 키.</summary>
        public static List<CodexEntry> Codex(DefinitionSet d, CampaignState state)
        {
            var list = new List<CodexEntry>();
            foreach (var s in d.Secrets)
            {
                bool found = state.FoundSecrets.Contains(s.Id);
                state.SecretFinds.TryGetValue(s.Id, out var find);
                list.Add(new CodexEntry { SecretId = found ? s.Id : null, Found = found, HintKey = found ? null : s.HintKey, Category = s.Category, Find = found ? find : null });
            }
            return list;
        }

        /// <summary>장수 도감 (13 시안): 데이터의 모든 장수 (닉네임 장수 틀 빼고), 가졌는지 · 얻는 곳.</summary>
        public static List<GeneralCodexEntry> GeneralCodex(DefinitionSet d, CampaignState state)
        {
            var list = new List<GeneralCodexEntry>();
            foreach (var g in d.Generals.Values)
            {
                if (AvatarGeneral.IsAvatar(g.Id)) continue;
                var e = new GeneralCodexEntry { GeneralId = g.Id, Grade = g.Grade, Owned = state.FindGeneral(g.Id) != null };
                foreach (var b in d.Gacha.Banners.Values) if (Contains(b.Pool, g.Id)) { e.Sources.Add("gacha"); break; }
                if (d.StartingKit != null && Contains(d.StartingKit.Generals, g.Id)) e.Sources.Add("start");
                if (e.Sources.Count == 0) e.Sources.Add("story");
                list.Add(e);
            }
            return list;
        }

        private static bool Contains(IReadOnlyList<string> list, string id)
        {
            if (list == null) return false;
            foreach (var x in list) if (x == id) return true;
            return false;
        }
    }

    // ---- 조건 ----

    internal sealed class LossRatioAtMost : ISecretCondition
    {
        public string Type => "lossRatioAtMost";
        public bool IsMet(SecretContext c, SkillPart p)
        {
            long start = 0, lost = 0;
            foreach (var cr in c.Result.Corps)
                if (cr.Team == StoryFlow.PlayerTeam) { start += cr.StartTroops; lost += cr.Losses; }
            return start > 0 && Fix.FromInt(lost) <= p.Fix("value") * start;
        }
    }

    internal sealed class WeatherIs : ISecretCondition
    {
        public string Type => "weatherIs";
        public bool IsMet(SecretContext c, SkillPart p) => c.Plan.Setup.Weather == p.String("value");
    }

    internal sealed class TimeOfDayIs : ISecretCondition
    {
        public string Type => "timeOfDayIs";
        public bool IsMet(SecretContext c, SkillPart p) => c.Plan.Setup.TimeOfDay == p.String("value");
    }

    internal sealed class NoPlayerTroopTag : ISecretCondition
    {
        public string Type => "noPlayerTroopTag";
        public bool IsMet(SecretContext c, SkillPart p)
        {
            string tag = p.String("tag");
            foreach (var cs in c.Plan.Setup.Corps)
                if (cs.Team == StoryFlow.PlayerTeam)
                    foreach (var u in cs.Units)
                        if (c.Defs.TroopTypes[u.TroopType].HasTag(tag)) return false;
            return true;
        }
    }

    /// <summary>아군 장수가 일기토에서 상대를 쓰러뜨림.</summary>
    internal sealed class DuelWonByFall : ISecretCondition
    {
        public string Type => "duelWonByFall";
        public bool IsMet(SecretContext c, SkillPart p) => DuelWinnerRecipient.Winner(c) != null;
    }

    /// <summary>아군이 놓은 불에 피해를 입은 적 군단 수.</summary>
    internal sealed class FireHitEnemyCorpsAtLeast : ISecretCondition
    {
        public string Type => "fireHitEnemyCorpsAtLeast";
        public bool IsMet(SecretContext c, SkillPart p)
        {
            var hit = new HashSet<string>();
            foreach (var e in c.Result.Events)
                if (e.Kind == EventKinds.FireHit && e.Data == StoryFlow.PlayerTeam && c.Result.Find(e.CorpsId)?.Team == StoryFlow.EnemyTeam) hit.Add(e.CorpsId);
            return hit.Count >= p.Fix("atLeast").FloorToLong();
        }
    }

    // ---- 받는 장수 ----

    internal sealed class CommanderRecipient : ISecretRecipient
    {
        public string Type => "commander";
        public string Pick(SecretContext c, SecretDef s)
        {
            foreach (var cs in c.Plan.Setup.Corps)
                if (cs.Team == StoryFlow.PlayerTeam && cs.Role == CorpsRoles.Commander) return cs.General.Id;
            return null;
        }
    }

    internal sealed class DuelWinnerRecipient : ISecretRecipient
    {
        public string Type => "duelWinner";
        public string Pick(SecretContext c, SecretDef s) => Winner(c);

        public static string Winner(SecretContext c)
        {
            foreach (var r in c.Result.Contests)
            {
                if (r.ContestId != "duel" || r.End != ContestEnds.Fall || r.Winner == null) continue;
                var w = r.Winner == "A" ? r.A : r.B;
                if (c.State.FindGeneral(w.Id) != null) return w.Id;
            }
            return null;
        }
    }

    /// <summary>그 스킬을 쓴 아군 장수 (전투 이벤트 기록).</summary>
    internal sealed class CasterOfRecipient : ISecretRecipient
    {
        public string Type => "casterOf";
        public string Pick(SecretContext c, SecretDef s)
        {
            foreach (var e in c.Result.Events)
            {
                if (e.Kind != EventKinds.SkillUsed || e.Data != s.CasterSkill) continue;
                foreach (var cs in c.Plan.Setup.Corps)
                    if (cs.Id == e.CorpsId && cs.Team == StoryFlow.PlayerTeam) return cs.General.Id;
            }
            return null;
        }
    }
}
