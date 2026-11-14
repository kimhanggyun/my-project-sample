using System.Linq;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Campaign.Save;
using Game.Flow;
using Game.Foundation.Save;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>상황별 편성 (formation.md 4장, story.md 출진 준비, verification R-35).</summary>
    public class FormationPresetTests
    {
        [Fact(DisplayName = "R-35 출진은 스텝 전장 유형의 상황별 편성으로, 없으면 기본 편성으로")]
        public void SortiePicksPreset()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = Kit.Veteran();
            var plain = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 1 });
            Assert.Equal(state.Compositions.Count, plain.PlayerCorps.Count);

            // 야전 편성: 첫 군단만
            var ed = new FormationEditor(Kit.D, state, "field");
            Assert.False(ed.HasPreset);
            Assert.True(ed.Changed()); // 없던 상황별 편성을 새로 만들 수 있다
            while (ed.Draft.Count > 1) ed.RemoveCorps(ed.Draft.Count - 1);
            Assert.True(ed.Apply());
            Assert.True(ed.HasPreset);
            Assert.Equal(3, state.Compositions.Count); // 기본 편성은 그대로

            var field = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 1 }); // step01은 야전
            Assert.Single(field.PlayerCorps);
            Assert.Same(state.CompositionPresets["field"], state.CompositionsFor("field"));
            Assert.Same(state.Compositions, state.CompositionsFor("siege"));

            Assert.True(new FormationEditor(Kit.D, state, "field").RemovePreset());
            Assert.Equal(3, flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 1 }).PlayerCorps.Count);
        }

        [Fact(DisplayName = "기본 편성은 지울 수 없고, 빈 편성은 저장되지 않는다")]
        public void Guards()
        {
            var state = Kit.Veteran();
            Assert.False(new FormationEditor(Kit.D, state).RemovePreset());
            var ed = new FormationEditor(Kit.D, state, "siege");
            while (ed.Draft.Count > 0) ed.RemoveCorps(0);
            Assert.False(ed.Apply());
            Assert.False(state.CompositionPresets.ContainsKey("siege"));
        }

        [Fact(DisplayName = "세이브 v4: 상황별 편성이 저장·복원되고, v3 세이브는 빈 상황별 편성으로 불러온다")]
        public void SaveAndMigrate()
        {
            var store = new InMemorySaveStore();
            var saves = new CampaignSaveService(store);
            var state = Kit.Veteran();
            var ed = new FormationEditor(Kit.D, state, "field");
            ed.RemoveCorps(2);
            Assert.True(ed.Apply());
            saves.Save(state);
            var back = saves.Load();
            Assert.Equal(2, back.CompositionPresets["field"].Count);
            Assert.Equal(state.CompositionPresets["field"][0].GeneralId, back.CompositionPresets["field"][0].GeneralId);

            var old = JObject.Parse(store.Read(CampaignSaveService.DefaultKey));
            old["version"] = 3;
            ((JObject)old["campaign"]).Remove("CompositionPresets");
            store.Write(CampaignSaveService.DefaultKey, old.ToString());
            var migrated = saves.Load();
            Assert.Empty(migrated.CompositionPresets);
            Assert.Equal(state.Compositions.Count, migrated.Compositions.Count);
        }

        [Fact(DisplayName = "상황 목록은 데이터 (campaign.json formationSituations), 이름 문구가 있다")]
        public void SituationsData()
        {
            Assert.Contains("field", Kit.D.Campaign.FormationSituations);
            foreach (var s in Kit.D.Campaign.FormationSituations) Assert.True(Kit.D.Strings.ContainsKey("situation." + s), s);
        }
    }
}
