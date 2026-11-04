using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Battle.Model.Damage
{
    /// <summary>지형별 병종 전투 보정 (battle.md 2.16.1). 병종 태그마다 표의 값을 더한다.</summary>
    public static class TerrainCombat
    {
        public static Fix ModifierPp(DefinitionSet defs, string terrainId, TroopTypeDef troop)
        {
            if (!defs.TerrainCombat.TryGetValue(terrainId, out var row)) return Fix.Zero;
            Fix sum = Fix.Zero;
            foreach (var tag in troop.Tags)
                if (row.TryGetValue(tag, out var pp)) sum += pp;
            return sum;
        }
    }
}
