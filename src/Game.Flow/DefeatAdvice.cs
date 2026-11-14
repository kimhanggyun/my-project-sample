using System.Collections.Generic;
using Game.Battle.Model.Simulation;
using Game.Data.Json;
using Game.Foundation.Math;
using Newtonsoft.Json.Linq;

namespace Game.Flow
{
    /// <summary>패배 팁 하나: 이유 id (문구 ui.result.tip.{id}) + 바로가기 (ui.result.go.{id}, 화면이 탭으로 연다).</summary>
    public sealed class DefeatTip
    {
        public string Id { get; internal set; }
        public List<string> Shortcuts { get; } = new List<string>();
    }

    /// <summary>
    /// 패배 팁 고르기 (20 시안, verification R-104): 진 전투의 결과 · 출진에서 한 가지 이유를 고른다.
    /// 데이터 Rules/defeatTips.json: order = 보는 차례, troopsRatio = 내 처음 병력이 적의 이 비율 아래면 "병력", heavyLossPct · enemyLightLossPct = 크게 잃고 적은 덜 잃으면 "상성 · 진형",
    /// shortcuts = 이유마다 바로가기. 파일이 없으면 기본값. 이기면 팁 없음.
    /// </summary>
    public sealed class DefeatAdvice
    {
        public const string File = "Rules/defeatTips.json";
        public const string Supply = "supply", Commander = "commander", Troops = "troops", Losses = "losses", Time = "time", Other = "other";

        public IReadOnlyList<string> Order { get; private set; } = new List<string> { Supply, Commander, Troops, Losses, Time };
        public Fix TroopsRatio { get; private set; } = Fix.FromRatio(4, 5);
        public Fix HeavyLossPct { get; private set; } = Fix.FromInt(60);
        public Fix EnemyLightLossPct { get; private set; } = Fix.FromInt(30);
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Shortcuts { get; private set; } = new Dictionary<string, IReadOnlyList<string>>
        {
            [Supply] = new List<string> { "supply", "shop" },
            [Commander] = new List<string> { "formation", "general" },
            [Troops] = new List<string> { "troop", "formation" },
            [Losses] = new List<string> { "formation", "shared" },
            [Time] = new List<string> { "formation", "shared" },
            [Other] = new List<string> { "shared", "formation" },
        };

        public static DefeatAdvice Load(IDataSource source)
        {
            var a = new DefeatAdvice();
            if (source == null || !source.Exists(File)) return a;
            var o = JsonUtil.ParseObject(source.ReadText(File));
            if (o["order"] is JArray) a.Order = JsonUtil.StringList(o, "order");
            a.TroopsRatio = JsonUtil.FixOr(o, "troopsRatio", a.TroopsRatio, File);
            a.HeavyLossPct = JsonUtil.FixOr(o, "heavyLossPct", a.HeavyLossPct, File);
            a.EnemyLightLossPct = JsonUtil.FixOr(o, "enemyLightLossPct", a.EnemyLightLossPct, File);
            if (o["shortcuts"] is JObject sc)
            {
                var d = new Dictionary<string, IReadOnlyList<string>>();
                foreach (var kv in sc) d[kv.Key] = JsonUtil.StringList(sc, kv.Key);
                if (!d.ContainsKey(Other)) d[Other] = a.Shortcuts[Other];
                a.Shortcuts = d;
            }
            return a;
        }

        /// <summary>진 전투의 팁 (이겼으면 null). 차례대로 맞는 첫 이유, 없으면 other.</summary>
        public DefeatTip Pick(SortiePlan plan, BattleResult result, bool won)
        {
            if (won || result == null) return null;
            long myStart = 0, myLeft = 0, enStart = 0, enLeft = 0;
            bool supplyOut = false, commanderOut = false;
            foreach (var c in result.Corps)
            {
                if (c.Team == StoryFlow.PlayerTeam)
                {
                    myStart += c.StartTroops; myLeft += c.RemainingTroops;
                    if (c.StartSupply > 0 && c.RemainingSupply <= 0) supplyOut = true;
                }
                else { enStart += c.StartTroops; enLeft += c.RemainingTroops; }
            }
            if (plan?.Setup != null)
                foreach (var cs in plan.Setup.Corps)
                    if (cs.Team == StoryFlow.PlayerTeam && cs.Role == CorpsRoles.Commander)
                    {
                        var r = result.Find(cs.Id);
                        if (r != null && r.Status != CorpsStatus.Active) commanderOut = true;
                    }
            Fix myLoss = myStart <= 0 ? Fix.Zero : Fix.FromInt(myStart - myLeft) * Fix.Hundred / Fix.FromInt(myStart);
            Fix enLoss = enStart <= 0 ? Fix.Zero : Fix.FromInt(enStart - enLeft) * Fix.Hundred / Fix.FromInt(enStart);
            foreach (var id in Order)
            {
                bool hit = id == Supply ? supplyOut
                    : id == Commander ? commanderOut
                    : id == Troops ? enStart > 0 && Fix.FromInt(myStart) < Fix.FromInt(enStart) * TroopsRatio
                    : id == Losses ? myLoss >= HeavyLossPct && enLoss < EnemyLightLossPct
                    : id == Time && result.EndReason == "timeLimit";
                if (hit) return Make(id);
            }
            return Make(Other);
        }

        private DefeatTip Make(string id)
        {
            var t = new DefeatTip { Id = id };
            if (Shortcuts.TryGetValue(id, out var list)) t.Shortcuts.AddRange(list);
            return t;
        }
    }
}
