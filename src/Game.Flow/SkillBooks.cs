using System.Collections.Generic;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Economy;

namespace Game.Flow
{
    /// <summary>
    /// 전투 기록 → 장수 기록 카운터 (verification R-32): 아군 군단에 일어난 전투 이벤트를 그 군단 장수의 카운터
    /// "battle.&lt;이벤트 종류&gt;"에 1씩 더한다 (예: battle.ambushed = 복병 기습을 당한 횟수, battle.hiddenFound = 숨은 적을 찾아낸 횟수).
    /// 스킬 습득 조건·숨은 보상·성장 규칙이 같은 카운터를 읽는다.
    /// </summary>
    public static class BattleRecords
    {
        public const string Prefix = "battle.";

        public static string Counter(string eventKind) => Prefix + eventKind;

        /// <param name="playerCorps">전투 군단 id → 편성 (출진 준비가 만든 것).</param>
        public static void Record(CampaignState state, IReadOnlyDictionary<string, CorpsComposition> playerCorps, BattleResult result)
        {
            if (result.Events == null) return;
            foreach (var e in result.Events)
            {
                if (e.CorpsId == null || !playerCorps.TryGetValue(e.CorpsId, out var comp)) continue;
                var g = state.FindGeneral(comp.GeneralId);
                if (g == null) continue;
                string key = Counter(e.Kind);
                g.Records.TryGetValue(key, out var n);
                g.Records[key] = n + 1;
            }
        }
    }

    /// <summary>스킬북 사용 결과.</summary>
    public enum LearnCheck
    {
        Ok,
        UnknownSkill,
        UnknownGeneral,
        AlreadyKnown,
        /// <summary>그 스킬의 스킬북이 없다.</summary>
        NoBook,
        /// <summary>요구 능력치가 모자란다 (스킬북은 보관해 두었다가 나중에 배운다).</summary>
        StatTooLow,
        /// <summary>습득 최소 조건(장수 기록)을 아직 채우지 못했다.</summary>
        ConditionNotMet,
    }

    /// <summary>습득 조건 하나의 진행 (화면 표시용).</summary>
    public sealed class LearnProgress
    {
        public string Counter { get; internal set; }
        public long Have { get; internal set; }
        public long Need { get; internal set; }
        public bool Met => Have >= Need;
    }

    /// <summary>
    /// 스킬북 (generals.md 4장, verification R-31): 상점에서 돈으로 사고, 장수 키우기에서 써서 배운다.
    /// 배우려면 요구 능력치 + 스킬마다 정한 습득 최소 조건(장수 기록 카운터, 없으면 조건 없음)을 채워야 한다. 쓰면 사라진다.
    /// 숨은 보상으로 바로 얻는 스킬(secrets.md)은 습득 조건을 보지 않는다.
    /// </summary>
    public sealed class SkillBookService
    {
        private readonly DefinitionSet _defs;

        public SkillBookService(DefinitionSet defs) { _defs = defs; }

        /// <summary>상점 가격 (돈). 팔지 않으면 null (단계 가격이 없음).</summary>
        public long? Price(string skillId)
        {
            if (!_defs.Skills.TryGetValue(skillId ?? "", out var s) || s.Tier == null) return null;
            return _defs.Campaign.SkillBookPriceByTier.TryGetValue(s.Tier, out var p) ? p : (long?)null;
        }

        public long Owned(CampaignState state, string skillId) => state.SkillBooks.TryGetValue(skillId, out var n) ? n : 0;

        /// <summary>돈으로 스킬북 한 권을 산다. 돈이 모자라거나 팔지 않으면 false.</summary>
        public bool Buy(CampaignState state, string skillId)
        {
            var price = Price(skillId);
            if (price == null || !new Wallet(state.Wallet).TrySpend(ResourceIds.Gold, price.Value)) return false;
            state.SkillBooks[skillId] = Owned(state, skillId) + 1;
            return true;
        }

        public List<LearnProgress> Progress(GeneralState g, SkillDef skill)
        {
            var list = new List<LearnProgress>();
            foreach (var r in skill.LearnConditions)
                list.Add(new LearnProgress { Counter = r.Counter, Have = g.Records.TryGetValue(r.Counter, out var n) ? n : 0, Need = r.AtLeast });
            return list;
        }

        public bool ConditionsMet(GeneralState g, SkillDef skill)
        {
            if (skill.LearnConditions.Count == 0) return true;
            int met = 0;
            foreach (var p in Progress(g, skill)) if (p.Met) met++;
            return skill.LearnAny ? met > 0 : met == skill.LearnConditions.Count;
        }

        public LearnCheck Check(CampaignState state, string generalId, string skillId)
        {
            if (!_defs.Skills.TryGetValue(skillId ?? "", out var skill)) return LearnCheck.UnknownSkill;
            var g = state.FindGeneral(generalId);
            if (g == null) return LearnCheck.UnknownGeneral;
            if (Knows(g, skill.Id)) return LearnCheck.AlreadyKnown;
            if (Owned(state, skill.Id) <= 0) return LearnCheck.NoBook;
            if (!MeetsStats(g, skill)) return LearnCheck.StatTooLow;
            if (!ConditionsMet(g, skill)) return LearnCheck.ConditionNotMet;
            return LearnCheck.Ok;
        }

        /// <summary>스킬북을 써서 배운다. 성공하면 책 한 권이 사라진다.</summary>
        public LearnCheck Learn(CampaignState state, string generalId, string skillId)
        {
            var check = Check(state, generalId, skillId);
            if (check != LearnCheck.Ok) return check;
            state.FindGeneral(generalId).LearnedSkills.Add(skillId);
            long left = Owned(state, skillId) - 1;
            if (left > 0) state.SkillBooks[skillId] = left; else state.SkillBooks.Remove(skillId);
            return LearnCheck.Ok;
        }

        /// <summary>기본 스킬 + 배운 스킬.</summary>
        public bool Knows(GeneralState g, string skillId) =>
            g.LearnedSkills.Contains(skillId) || (_defs.Generals.TryGetValue(g.GeneralId, out var d) && Contains(d.InnateSkills, skillId));

        private bool MeetsStats(GeneralState g, SkillDef skill)
        {
            if (skill.Requires.Count == 0) return true;
            int met = 0;
            foreach (var r in skill.Requires) if (CorpsValidator.EffectiveStat(_defs, g, r.Stat) >= r.Min) met++;
            return skill.RequiresAny ? met > 0 : met == skill.Requires.Count;
        }

        private static bool Contains(IReadOnlyList<string> list, string id)
        {
            foreach (var x in list) if (x == id) return true;
            return false;
        }
    }
}
