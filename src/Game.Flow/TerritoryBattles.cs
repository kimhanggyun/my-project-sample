using Game.Data.Defs;
using Game.Data.Json;
using Game.Foundation.Geo;
using Game.Territory;
using Newtonsoft.Json.Linq;

namespace Game.Flow
{
    /// <summary>
    /// 영토 전투 (territory.md 5장, verification R-46): 지역 데이터의 수비군으로 스텝을 만든다 (스텝 id = "territory." + 지역 id).
    /// 전투 모듈·출진 준비·결과 반영은 스토리와 같고, 해금·클리어·첫 보상·탐색 기억만 없다.
    /// </summary>
    public static class TerritoryBattles
    {
        public const string Prefix = "territory.";

        public static bool IsTerritoryStep(string stepId) => stepId != null && stepId.StartsWith(Prefix, System.StringComparison.Ordinal);
        public static string StepIdOf(string regionId) => Prefix + regionId;
        public static string RegionOf(string stepId) => IsTerritoryStep(stepId) ? stepId.Substring(Prefix.Length) : null;

        /// <summary>지역 → 스텝 (없는 지역이면 null).</summary>
        public static StepDef StepFor(TerritoryDefs defs, string regionId)
        {
            if (defs == null || regionId == null || !defs.Regions.TryGetValue(regionId, out var region)) return null;
            var o = defs.DefenseFor(region);
            o["id"] = StepIdOf(regionId);
            o["kind"] = StepDef.TerritoryKind;
            o["nameKey"] = region.NameKey;
            return StepDef.From(o);
        }

        /// <summary>
        /// 영토 출진 검사 (기기·서버 같은 코드): 지역이 있고, 혼자 차지할 수 있는 단계(필요 군단 1)이며, 위치에서 공격할 수 있는 지역이어야 한다.
        /// 되면 null, 아니면 까닭.
        /// </summary>
        public static string CheckSortie(TerritoryDefs defs, string regionId, string location)
        {
            if (defs == null || regionId == null || !defs.Regions.TryGetValue(regionId, out var region)) return $"없는 지역 '{regionId}'.";
            if (defs.RequiredLegions(region) > 1) return $"'{regionId}'은 연합(군단 {defs.RequiredLegions(region)}개)이 있어야 공격할 수 있습니다.";
            if (defs.Levels[region.Level].SeasonOnly) return $"'{regionId}'은 시즌 행사에서만 공격할 수 있습니다.";
            if (!GeoPoint.TryParse(location, out var point)) return "위치가 없습니다.";
            foreach (var r in new RegionLocator(defs).Attackable(point)) if (r.Id == regionId) return null;
            return $"지금 위치에서는 '{regionId}'을 공격할 수 없습니다.";
        }

        /// <summary>맞은 제출이 영토를 차지하는가: 영토 전투이고 아군(공격자)이 이겼다 (R-46: 이기면 주인이 공격자로 바뀐다).</summary>
        public static bool Captures(Game.Online.BattleSubmission s, Game.Online.BattleVerdict v) =>
            v != null && v.Valid && s != null && IsTerritoryStep(s.StepId) && v.Winner == StoryFlow.PlayerTeam;

        /// <summary>데이터에 영토가 있으면 읽는다 (없으면 null).</summary>
        public static TerritoryDefs TryLoad(IDataSource source) =>
            source != null && source.Exists(TerritoryDefs.File) ? TerritoryDefs.Load(source) : null;
    }
}
