namespace Game.Battle.Model.Simulation
{
    /// <summary>
    /// 용병 배신 (hiring-v2.md 4장, verification R-99): <see cref="CorpsSetup.BetrayToTeam"/>이 있는 군단은
    /// 지금 편의 아무 군단이 처음 교전한 틱이 끝날 때 그 편으로 돌아서고, AI가 지휘한다 (공격 성향). 한 번만.
    /// 시드 · 명령 기록이 같으면 같은 틱에 일어난다 (재현 가능).
    /// </summary>
    internal static class BetrayalSystem
    {
        public static void Step(BattleContext ctx)
        {
            foreach (var traitor in ctx.Corps)
            {
                var to = traitor.Setup.BetrayToTeam;
                if (to == null || traitor.Betrayed || traitor.Status != CorpsStatus.Active || to == traitor.Team) continue;
                bool contact = false;
                foreach (var c in ctx.Corps)
                {
                    if (c.Team == traitor.Team && c.Status == CorpsStatus.Active && c.Engaged) { contact = true; break; }
                }
                if (!contact) continue;
                string from = traitor.Team;
                traitor.TeamOverride = to;
                traitor.AiStance = Ai.AiStanceIds.Aggressive;
                if (traitor.AiDifficulty == null) traitor.AiDifficulty = "normal";
                traitor.Delegated = false;
                traitor.OrderByPlayer = false;
                traitor.Queue.Clear();
                traitor.Betrayed = true;
                Events.EventLog.Add(ctx, Events.EventKinds.Betrayed, traitor.Id, from);
            }
        }
    }
}
