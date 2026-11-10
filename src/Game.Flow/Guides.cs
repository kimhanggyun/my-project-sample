using Game.Campaign;
using Game.Data.Defs;

namespace Game.Flow
{
    /// <summary>
    /// 처음 안내 (R-62): 화면이 자기 자리 id로 묻으면 아직 안 본 첫 안내를 준다. 닫으면 행동 guide.seen으로 본 기록을 남긴다.
    /// 문장·자리는 데이터(Rules/guides.json), 본 기록은 세이브(<see cref="CampaignState.SeenGuides"/>).
    /// </summary>
    public sealed class GuideService
    {
        private readonly DefinitionSet _defs;
        public GuideService(DefinitionSet defs) { _defs = defs; }

        /// <summary>이 자리에서 아직 안 본 첫 안내 (없으면 null).</summary>
        public GuideDef Next(CampaignState state, string place)
        {
            foreach (var g in _defs.Guides.Guides)
                if (g.Place == place && !state.SeenGuides.Contains(g.Id)) return g;
            return null;
        }

        /// <summary>본 것으로 남긴다. 없는 안내이거나 이미 봤으면 false (아무것도 바뀌지 않음).</summary>
        public bool MarkSeen(CampaignState state, string guideId)
        {
            if (guideId == null || _defs.Guides.Find(guideId) == null || state.SeenGuides.Contains(guideId)) return false;
            state.SeenGuides.Add(guideId);
            return true;
        }

        /// <summary>본 기록을 모두 지운다 (안내 다시 보기). 지울 것이 없으면 false.</summary>
        public bool Reset(CampaignState state)
        {
            if (state.SeenGuides.Count == 0) return false;
            state.SeenGuides.Clear();
            return true;
        }
    }
}
