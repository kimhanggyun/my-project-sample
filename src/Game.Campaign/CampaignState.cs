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

        public GeneralState FindGeneral(string id) => Generals.Find(g => g.GeneralId == id);
        public UnitState FindUnit(string id) => Units.Find(u => u.UnitId == id);
    }
}
