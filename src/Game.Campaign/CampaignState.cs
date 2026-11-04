using System.Collections.Generic;
using Game.Campaign.Roster;

namespace Game.Campaign
{
    /// <summary>전투 밖 진행 상태 (세이브의 중심). 이 상태를 바꾸는 것은 Campaign 모듈뿐이다.</summary>
    public sealed class CampaignState
    {
        public List<GeneralState> Generals { get; set; } = new List<GeneralState>();
        public List<UnitState> Units { get; set; } = new List<UnitState>();
        public List<CorpsComposition> Compositions { get; set; } = new List<CorpsComposition>();
        public List<string> LearnedFormations { get; set; } = new List<string>();
        public List<string> ClearedSteps { get; set; } = new List<string>();
        public Dictionary<string, long> Wallet { get; set; } = new Dictionary<string, long>();
        /// <summary>아군이 가둔 적 장수.</summary>
        public List<PrisonerState> Prisoners { get; set; } = new List<PrisonerState>();
        /// <summary>스텝 id → 탐색한 칸 ("0"/"1" 문자열, 칸 순서 y × 폭 + x). 같은 스텝 재도전 시 탐색 기억 (battle.md 2.10).</summary>
        public Dictionary<string, string> ExploredBySteps { get; set; } = new Dictionary<string, string>();
        /// <summary>찾은 숨은 보상 id (도감, secrets.md 3장).</summary>
        public List<string> FoundSecrets { get; set; } = new List<string>();
        /// <summary>보관 중인 스킬북·특성서 (id → 개수). 요구 능력치가 모자라 아직 못 배운 숨은 보상도 여기로.</summary>
        public Dictionary<string, long> SkillBooks { get; set; } = new Dictionary<string, long>();
        public Dictionary<string, long> TraitBooks { get; set; } = new Dictionary<string, long>();

        public GeneralState FindGeneral(string id) => Generals.Find(g => g.GeneralId == id);
        public UnitState FindUnit(string id) => Units.Find(u => u.UnitId == id);
        public PrisonerState FindPrisoner(string id) => Prisoners.Find(p => p.GeneralId == id);
    }
}
