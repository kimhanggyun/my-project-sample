using System.Collections.Generic;
using Game.Battle.Model.Crowd;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Online;
using Game.Territory;

namespace Game.Flow
{
    /// <summary>
    /// 영지 깃발전 (R-163 · R-166 · R-167, 2026-11-28 사용자): 깃발 공성전을 병사 단위 깃발전으로 싸운다.
    /// 출진 준비(StoryFlow.Prepare)가 만든 군단 · 장수 · 병력을 그대로 쓰고 (CrowdBridge), 전장 = 깃발의 맵, 수비 = 주인의 수비판.
    /// 기기 · 서버 같은 코드 — 서버는 명령 기록으로 다시 돌려 확인한다.
    /// </summary>
    public static class FlagBattles
    {
        /// <summary>깃발의 전장 맵 (모르는 맵이면 기본).</summary>
        public static string MapOf(FlagDefs fd, TerritoryOwner flag) => fd != null && fd.IsMap(flag?.MapId) ? flag.MapId : fd?.DefaultMap ?? "crowd_plain";

        public static CrowdTerrain Terrain(DefinitionSet defs, IDataSource source, CrowdRules rules, string mapId)
        {
            if (string.IsNullOrEmpty(mapId)) return null;
            var map = MapDef.Load(source, mapId);
            return new CrowdTerrain(map, defs.TerrainRules, defs.TerrainCombat, CrowdTerrain.CellSizeFor(map, rules.MapFieldWidth));
        }

        /// <summary>출진 준비 → 깃발전 설정 (수비판 자리 · 자세 · 매복 · 함정).</summary>
        public static CrowdSetup Setup(DefinitionSet defs, IDataSource source, SortiePlan plan, string mapId)
        {
            var rules = CrowdRules.Load(source);
            var board = plan.Defense?.Board;
            List<CrowdTrapSpec> traps = null;
            if (board?.Traps != null && board.Traps.Count > 0)
            {
                traps = new List<CrowdTrapSpec>();
                foreach (var t in board.Traps) traps.Add(new CrowdTrapSpec { Team = CrowdBattle.Enemy, Kind = t.Kind, X = t.X, Y = t.Y });
            }
            return CrowdBridge.FromOld(plan.Setup, rules, StoryFlow.PlayerTeam, Terrain(defs, source, rules, mapId), board?.Places, traps);
        }

        /// <summary>새 깃발전 (스킬 데이터 포함).</summary>
        public static CrowdBattle New(DefinitionSet defs, IDataSource source, CrowdSetup setup)
        {
            var b = new CrowdBattle(CrowdRules.Load(source), setup, defs.TroopTypes, defs.TroopCounters);
            b.UseSkillData(defs.Skills);
            return b;
        }

        /// <summary>명령 기록으로 다시 돌린다 (서버 확인, R-166). maxTicks = 기기가 낸 틱 수.</summary>
        public static CrowdBattle Replay(DefinitionSet defs, IDataSource source, CrowdSetup setup, IReadOnlyList<CrowdCommand> commands, long maxTicks)
        {
            int cap = (int)System.Math.Max(0, System.Math.Min(int.MaxValue, maxTicks));
            return CrowdBattle.Replay(CrowdRules.Load(source), setup, defs.TroopTypes, defs.TroopCounters, defs.Skills, commands, cap);
        }

        /// <summary>끝난 깃발전 → 결과 반영용 결과.</summary>
        public static Game.Battle.Model.Simulation.BattleResult Result(CrowdBattle b, SortiePlan plan) =>
            CrowdBridge.ToResult(b, plan.Setup, StoryFlow.PlayerTeam, StoryFlow.EnemyTeam);

        /// <summary>깃발전 제출 (기기): 출진 준비 + 명령 기록 + 기기 결과.</summary>
        public static BattleSubmission SubmissionOf(SortiePlan plan, CrowdBattle b, long baseRevision, string flagAction)
        {
            var s = new BattleSubmission
            {
                BaseRevision = baseRevision,
                StepId = plan.Step.Id,
                Seed = plan.Setup.Seed,
                Supply = plan.SupplyTaken,
                ClaimedHash = CrowdBridge.StateHash(b).ToString("x16"),
                ClaimedTicks = b.Tick,
                PlayedAtUnixSeconds = plan.PreparedAtUnixSeconds,
                Location = plan.Location,
                DefenseVersion = plan.Defense?.Version ?? 0,
                CrowdCommands = new List<CrowdCommand>(b.Commands),
                FlagAction = FlagActions.IsRemove(flagAction) ? FlagActions.Remove : FlagActions.Take,
            };
            if (plan.Hired != null && !plan.Hired.IsEmpty)
            {
                s.Hires = new List<HireUse>(plan.Hired.Uses);
                s.Compositions = PlayerActionRunner.ToActionCorps(plan.Compositions);
            }
            // 서버가 같은 출진 준비를 하도록 위임 · 대결 선택도 (CloudSync.SubmissionOf와 같음)
            bool all = true;
            foreach (var c in plan.Setup.Corps)
                if (c.Team == StoryFlow.PlayerTeam)
                {
                    if (c.Delegated) s.DelegateGenerals.Add(c.General.Id);
                    else all = false;
                }
            if (all && s.DelegateGenerals.Count > 0) { s.Delegate = true; s.DelegateGenerals.Clear(); }
            foreach (var c in plan.Setup.Contests)
                s.ContestChoices[c.ContestId] = c.ChoicesA == null ? new List<string>() : new List<string>(c.ChoicesA);
            return s;
        }
    }
}
