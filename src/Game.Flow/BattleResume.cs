using System;
using System.Collections.Generic;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Simulation;
using Game.Campaign.Roster;
using Game.Foundation.Save;
using Game.Online;

namespace Game.Flow
{
    /// <summary>
    /// 하다 만 전투 (R-55): 앱이 전투 중에 꺼져도 다음 실행에 이어서 한다. 녹화(R-54)와 같은 방식 —
    /// 전투 설정(스텝 규칙 뺀 것) + 지금까지의 명령 기록 + 멈춘 틱, 그리고 결과 반영에 필요한 출진 정보.
    /// 이어 할 때 같은 틱까지 명령을 넣으며 다시 돌리므로, 끝까지 하면 꺼지지 않은 전투와 같은 결과·같은 명령 기록이 된다 (서버 확인도 그대로).
    /// </summary>
    public sealed class SuspendedBattle
    {
        public const string SaveKey = "battle.suspended";
        public const int CurrentFormat = 1;
        public int Format { get; set; } = CurrentFormat;
        public long SavedAtUnixSeconds { get; set; }
        /// <summary>멈춘 틱 (이 틱의 Step 전).</summary>
        public long Tick { get; set; }
        public string StepId { get; set; }
        public BattleSetup Setup { get; set; }
        public List<BattleCommand> Commands { get; set; } = new List<BattleCommand>();
        public Dictionary<string, CorpsComposition> PlayerCorps { get; set; } = new Dictionary<string, CorpsComposition>();
        public long SupplyTaken { get; set; }
        public long AutoSupply { get; set; }
        public long PreparedAtUnixSeconds { get; set; }
        public List<CorpsComposition> Compositions { get; set; }
        public string Location { get; set; }
        public TerritoryDefense Defense { get; set; }
    }

    public sealed class ResumedBattle
    {
        public SortiePlan Plan { get; internal set; }
        /// <summary>멈춘 틱까지 돌려 둔 전투. 이어서 Step하면 된다.</summary>
        public BattleSimulation Simulation { get; internal set; }
    }

    public static class BattleResume
    {
        /// <summary>
        /// 이어 하기를 남길 수 있는 출진인가. 고용이 있는 출진은 아직 남기지 않는다 (고용 비용·결과를 다시 맞추는 일은 이어서).
        /// </summary>
        public static bool Supports(SortiePlan plan) => plan?.Setup != null && (plan.Hired == null || plan.Hired.IsEmpty);

        /// <summary>지금 전투 상태를 남길 기록으로 (전투가 끝났으면 null).</summary>
        public static SuspendedBattle Capture(SortiePlan plan, BattleSimulation sim, long nowUnixSeconds)
        {
            if (!Supports(plan) || sim == null || sim.IsOver) return null;
            var s = new SuspendedBattle
            {
                SavedAtUnixSeconds = nowUnixSeconds,
                Tick = sim.Tick,
                StepId = plan.Step.Id,
                Setup = ReplayService.BareSetup(plan.Setup),
                Commands = new List<BattleCommand>(sim.CommandLog),
                PlayerCorps = new Dictionary<string, CorpsComposition>(plan.PlayerCorps),
                SupplyTaken = plan.SupplyTaken,
                AutoSupply = plan.AutoSupply,
                PreparedAtUnixSeconds = plan.PreparedAtUnixSeconds,
                Compositions = plan.Compositions,
                Location = plan.Location,
                Defense = plan.Defense,
            };
            return Copy(s);
        }

        public static SuspendedBattle Copy(SuspendedBattle s) => ReplayJson.Read<SuspendedBattle>(ReplayJson.Write(s));

        /// <summary>같은 틱까지 다시 돌려 이어 할 전투를 만든다. 스텝이 없거나 형식이 다르거나 명령이 맞지 않으면 null.</summary>
        public static ResumedBattle Restore(StoryFlow flow, SuspendedBattle saved)
        {
            if (saved?.Setup == null || saved.StepId == null || saved.Format != SuspendedBattle.CurrentFormat) return null;
            try
            {
                var s = Copy(saved);
                var step = flow.LoadStep(s.StepId);
                flow.ApplyStepRules(s.Setup, step);
                var plan = new SortiePlan
                {
                    Step = step,
                    Setup = s.Setup,
                    SupplyTaken = s.SupplyTaken,
                    AutoSupply = s.AutoSupply,
                    PreparedAtUnixSeconds = s.PreparedAtUnixSeconds,
                    Compositions = s.Compositions,
                    Location = s.Location,
                    Defense = s.Defense,
                };
                foreach (var kv in s.PlayerCorps) plan.PlayerCorps[kv.Key] = kv.Value;

                // 출진 때처럼 같은 설정으로 전투를 만든다 (전투는 설정을 바꾸지 않는다)
                var sim = new BattleSimulation(plan.Setup);
                var commands = s.Commands;
                int next = 0;
                while (!sim.IsOver && sim.Tick < s.Tick)
                {
                    while (next < commands.Count && commands[next].Tick <= sim.Tick)
                        if (!sim.Issue(commands[next++])) return null; // 기록과 다르게 흘렀다
                    sim.Step();
                }
                if (sim.IsOver || sim.Tick != s.Tick || next != CountBefore(commands, s.Tick)) return null;
                return new ResumedBattle { Plan = plan, Simulation = sim };
            }
            catch (Exception) { return null; }
        }

        private static int CountBefore(List<BattleCommand> commands, long tick)
        {
            int n = 0;
            foreach (var c in commands) if (c.Tick < tick) n++;
            return n;
        }

        public static SuspendedBattle Read(ISaveStore store)
        {
            try { return store.Exists(SuspendedBattle.SaveKey) ? ReplayJson.Read<SuspendedBattle>(store.Read(SuspendedBattle.SaveKey)) : null; }
            catch (Exception) { return null; }
        }

        public static void Write(ISaveStore store, SuspendedBattle s)
        {
            if (s == null) Clear(store);
            else store.Write(SuspendedBattle.SaveKey, ReplayJson.Write(s));
        }

        public static void Clear(ISaveStore store)
        {
            if (store.Exists(SuspendedBattle.SaveKey)) store.Delete(SuspendedBattle.SaveKey);
        }
    }
}
