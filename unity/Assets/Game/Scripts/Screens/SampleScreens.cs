using System.Collections.Generic;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;
using Game.Flow;
using Game.Unity.Battle3D;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>샘플전투·맵 탭 공통 (R-87).</summary>
    public static class SampleUi
    {
        public static string MapName(GameContext ctx, string mapId) => ctx.T("map." + mapId);

        /// <summary>그 맵을 쓰는 스텝 중 출진할 수 있는 첫 스텝으로 샘플전투를 연다. 안 되면 첫 오류를 돌려준다.</summary>
        public static string Start(GameApp app, SampleMapEntry map)
        {
            var ctx = app.Context;
            string firstError = null;
            foreach (var stepId in map.Steps)
            {
                var plan = SampleBattles.Prepare(ctx.Defs, ctx.Flow, ctx.State, stepId, ctx.NewSeed());
                if (plan.Ok) { app.Show(BattleScreen.Sample(plan, stepId)); return null; }
                if (firstError == null && plan.Errors.Count > 0) firstError = plan.Errors[0];
            }
            return firstError ?? ctx.T("ui.sample.cannot");
        }

        /// <summary>맵 미리보기 그림 (칸 = 픽셀, 비율 유지).</summary>
        public static RawImage Picture(Transform parent, MapDef map, float height)
        {
            var rt = Ui.Rect(parent, "MapPicture");
            float width = height * map.Width / Mathf.Max(1, map.Height);
            Ui.Size(rt, height, width).flexibleWidth = 0;
            rt.sizeDelta = new Vector2(width, height);
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = FieldTexture.Preview(map);
            img.raycastTarget = false;
            return img;
        }
    }

    /// <summary>샘플전투 탭 (R-87, 개발 줄): 맵을 골라 바로 싸운다. 보상 없음, 세이브에 남지 않음.</summary>
    public sealed class SampleBattleTabView : ITabView
    {
        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var list = Ui.ScrollList(root, "List");
            Ui.Fill((RectTransform)list.parent.parent);
            Ui.Label(list, ctx.T("ui.sample.note"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
            var error = Ui.Label(list, "", Ui.FontSmall, TextAnchor.MiddleLeft, Palette.Bad);
            bool siege = false;
            foreach (var m in SampleBattles.Catalog(ctx.Defs, ctx.Flow))
            {
                var map = m;
                if (m.BattleType == "siege") siege = true;
                var card = Ui.Card(list, m.MapId);
                Ui.Label(card, MapName(ctx, m) + "  " + Ui.Colored(ctx.T("battleType." + m.BattleType), Palette.TextDim), Ui.FontBody);
                var row = Ui.Row(card, "Buttons", 96);
                Ui.Size(Ui.Button(row, ctx.T("ui.sample.fight"), () => error.text = SampleUi.Start(app, map) ?? "", 96, Palette.ButtonActive), flexibleWidth: 1);
                Ui.Size(Ui.Button(row, ctx.T("ui.sample.viewMap"), () => app.Show(new MapDetailScreen(map.MapId, "sampleBattle")), 96), flexibleWidth: 1);
            }
            if (!siege) Ui.Label(list, ctx.T("ui.sample.noSiege"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
        }

        private static string MapName(GameContext ctx, SampleMapEntry m) => SampleUi.MapName(ctx, m.MapId);
    }

    /// <summary>맵 탭 (R-87, 개발 줄): 맵마다 위에서 본 그림·전장 유형·쓰는 스텝. 누르면 자세히.</summary>
    public sealed class MapTabView : ITabView
    {
        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var list = Ui.ScrollList(root, "List");
            Ui.Fill((RectTransform)list.parent.parent);
            foreach (var m in SampleBattles.Catalog(ctx.Defs, ctx.Flow))
            {
                var entry = m;
                var def = MapDef.Load(ctx.Data, m.MapId);
                var row = Ui.Row(list, m.MapId, 200, 20);
                SampleUi.Picture(row, def, 180);
                var names = new List<string>();
                foreach (var s in m.Steps) names.Add(ctx.T(ctx.Flow.LoadStep(s).NameKey));
                var b = Ui.Button(row, SampleUi.MapName(ctx, m.MapId) + "  " + Ui.Colored(ctx.T("battleType." + m.BattleType), Palette.TextDim) + "\n"
                    + Ui.Colored(ctx.T("ui.map.usedBy", string.Join(", ", names)), Palette.TextDim), () => app.Show(new MapDetailScreen(entry.MapId, "map")), 180, Palette.Card, 28);
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                Ui.Size(b, flexibleWidth: 1);
            }
            Ui.Label(list, ctx.T("ui.sample.noSiege"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
        }
    }

    /// <summary>맵 자세히 (R-87): 큰 그림, 범례(그 맵에 있는 지형), 쓰는 스텝, [샘플전투].</summary>
    public sealed class MapDetailScreen : GameScreen
    {
        private readonly string _mapId, _backTab;
        public MapDetailScreen(string mapId, string backTab) { _mapId = mapId; _backTab = backTab; }

        public override void Build()
        {
            Ui.Column(Root, 0);
            var list = Ui.ScrollList(Root, "List");
            Ui.Size(list.parent.parent, flexibleHeight: 1);
            var def = MapDef.Load(Ctx.Data, _mapId);
            SampleMapEntry entry = null;
            foreach (var m in SampleBattles.Catalog(Ctx.Defs, Ctx.Flow)) if (m.MapId == _mapId) entry = m;
            Ui.Size(Ui.Label(list, SampleUi.MapName(Ctx, _mapId) + "  " + Ui.Colored(T("battleType." + def.BattleType), Palette.TextDim), Ui.FontTitle, TextAnchor.MiddleCenter), 100);
            Ui.Label(list, T("ui.map.size", def.Width, def.Height) + "  " + T("ui.map.orientation"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
            var pic = Ui.Rect(list, "PictureRow");
            Ui.Size(pic, 720);
            var holder = pic.gameObject.AddComponent<HorizontalLayoutGroup>();
            holder.childAlignment = TextAnchor.MiddleCenter;
            holder.childControlWidth = holder.childControlHeight = false;
            holder.childForceExpandWidth = holder.childForceExpandHeight = false;
            SampleUi.Picture(pic, def, 720);

            // 범례: 이 맵에 있는 지형만
            var legend = Ui.Card(list, "Legend");
            Ui.Label(legend, T("ui.map.legend"));
            var seen = new List<string>();
            for (int y = 0; y < def.Height; y++)
                for (int x = 0; x < def.Width; x++)
                {
                    string t = def.TerrainAt(x, y);
                    if (!seen.Contains(t)) seen.Add(t);
                }
            foreach (var t in seen)
            {
                var row = Ui.Row(legend, t, 48, 16);
                var sw = Ui.Rect(row, "Swatch");
                Ui.Background(sw, FieldTexture.ColorOf(t));
                Ui.Size(sw, 40, 40).flexibleWidth = 0;
                Ui.Size(Ui.Label(row, T("terrain." + t), Ui.FontSmall), flexibleWidth: 1);
            }

            if (entry != null)
            {
                var steps = Ui.Card(list, "Steps");
                Ui.Label(steps, T("ui.map.steps"));
                foreach (var s in entry.Steps) Ui.Label(steps, "· " + T(Ctx.Flow.LoadStep(s).NameKey), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
                var error = Ui.Label(list, "", Ui.FontSmall, TextAnchor.MiddleLeft, Palette.Bad);
                Ui.Button(list, T("ui.sample.fight"), () => error.text = SampleUi.Start(App, entry) ?? "", 110, Palette.ButtonActive);
            }
            Ui.Button(Root, T("ui.common.back"), () => App.ShowMain(_backTab), 110);
        }
    }

    /// <summary>샘플전투 결과 (R-87): 승패·시간·끝난 까닭만. 보상·반영 없음.</summary>
    public sealed class SampleResultScreen : GameScreen
    {
        private readonly string _stepId;
        private readonly BattleResult _result;
        public SampleResultScreen(string stepId, BattleResult result) { _stepId = stepId; _result = result; }

        public override void Build()
        {
            Ui.Column(Root, 24, 48).childAlignment = TextAnchor.MiddleCenter;
            bool won = _result.Winner == StoryFlow.PlayerTeam;
            Sfx.Play(won ? "cheer" : "gong");
            Ui.Size(Ui.Label(Root, T(Ctx.Flow.LoadStep(_stepId).NameKey) + "  " + Ui.Colored(T("ui.sample.tag"), Palette.TextDim), Ui.FontBody, TextAnchor.MiddleCenter), 80);
            Ui.Size(Ui.Label(Root, won ? Ui.Colored(T("ui.result.win"), Palette.Good) : Ui.Colored(T("ui.result.lose"), Palette.Bad), Ui.FontTitle, TextAnchor.MiddleCenter), 130);
            Ui.Size(Ui.Label(Root, T("ui.result.time", _result.ElapsedSeconds.FloorToLong(), T("ui.result.endReason." + _result.EndReason)), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim), 56);
            Ui.Label(Root, T("ui.sample.noReward"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
            var error = Ui.Label(Root, "", Ui.FontSmall, TextAnchor.MiddleCenter, Palette.Bad);
            Ui.Button(Root, T("ui.sample.again"), () =>
            {
                var plan = SampleBattles.Prepare(Ctx.Defs, Ctx.Flow, Ctx.State, _stepId, Ctx.NewSeed());
                if (plan.Ok) App.Show(BattleScreen.Sample(plan, _stepId));
                else error.text = plan.Errors.Count > 0 ? plan.Errors[0] : "";
            }, 110, Palette.ButtonActive);
            Ui.Button(Root, T("ui.sample.backToList"), () => App.ShowMain("sampleBattle"), 110);
        }
    }
}
