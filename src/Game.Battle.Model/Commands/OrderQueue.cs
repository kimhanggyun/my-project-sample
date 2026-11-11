using Game.Battle.Model.Simulation;
using Game.Foundation.Math;

namespace Game.Battle.Model.Commands
{
    /// <summary>
    /// 명령 예약 (battle.md 4.3.5·4.3.6, verification R-36): 지금 명령이 끝나면(도착·대상 사라짐·지점 스킬 사용) 예약을 차례로 꺼낸다.
    /// 꺼낸 명령이 지금 받을 수 없으면(대상이 사라짐 등) 건너뛰고 다음으로. 예약이 없으면 대기.
    /// </summary>
    internal static class OrderQueue
    {
        public static bool IsIdle(CorpsState c) => c.Order.Kind == OrderKinds.Hold && c.PendingSkill == null;

        /// <summary>지금 명령을 끝내고 다음 예약으로.</summary>
        public static void Finish(BattleContext ctx, CorpsState corps)
        {
            corps.AssignOrder(new CorpsOrder());
            while (corps.Queue.Count > 0)
            {
                var cmd = corps.Queue[0];
                corps.Queue.RemoveAt(0);
                if (ctx.Commands == null || !ctx.Commands.TryGet(cmd.Kind, out var h)) continue;
                if (!h.IsValid(ctx, corps, cmd))
                {
                    // 받을 수 없는 예약은 건너뛰고 알린다 (4.3.6: 도착했는데 스킬 조건 불충족 등)
                    Events.EventLog.Add(ctx, Events.EventKinds.QueuedSkipped, corps.Id, cmd.SkillId ?? cmd.Kind);
                    continue;
                }
                var before = corps.Order;
                h.Apply(ctx, corps, cmd);
                if (ReferenceEquals(before, corps.Order) && corps.PendingSkill == null) continue; // 진형 바꾸기처럼 명령이 그대로면 다음 예약도
                corps.OrderByPlayer = true;
                return;
            }
        }
    }

    /// <summary>
    /// 협공 대기 (battle.md 4.3.4, verification R-37): 한 팀에서 협공 대기 중이거나 예약에 협공 대기가 있는 군단이 모두 자리를 잡으면(지금 명령이 협공 대기) 함께 공격한다.
    /// 오래 기다리면(coordinateWaitSec) 한 번 묻고(이벤트 coordinateWaiting), 응답 시간(coordinateResponseSec)이 더 지나면 그 군단만 공격한다.
    /// 공격 대상: 정한 적이 아직 싸우고 있으면 그 적, 아니면 진군(가장 가까운 적, 역할대로).
    /// </summary>
    internal static class CoordinationSystem
    {
        public static void Step(BattleContext ctx)
        {
            var c = ctx.Combat;
            long wait = (c.CoordinateWaitSec / c.TickSeconds).CeilToLong();
            long respond = (c.CoordinateResponseSec / c.TickSeconds).CeilToLong();
            foreach (var team in ctx.Teams)
            {
                int pending = 0, ready = 0;
                foreach (var corps in ctx.Corps)
                {
                    if (corps.Team != team || corps.Status != CorpsStatus.Active) continue;
                    bool queued = false;
                    foreach (var q in corps.Queue) if (q.Kind == CommandKinds.Hold && q.Mode == HoldModes.Coordinate) { queued = true; break; }
                    if (corps.Coordinating) { pending++; ready++; }
                    else if (queued) pending++;
                }
                if (pending == 0) continue;
                if (ready == pending)
                {
                    foreach (var corps in ctx.Corps)
                        if (corps.Team == team && corps.Status == CorpsStatus.Active && corps.Coordinating) Go(ctx, corps);
                    continue;
                }
                foreach (var corps in ctx.Corps)
                {
                    if (corps.Team != team || corps.Status != CorpsStatus.Active || !corps.Coordinating) continue;
                    long waited = ctx.Tick - corps.CoordinateSinceTick;
                    if (!corps.CoordinateAsked && waited >= wait)
                    {
                        corps.CoordinateAsked = true;
                        Events.EventLog.Add(ctx, Events.EventKinds.CoordinateWaiting, corps.Id);
                    }
                    else if (corps.CoordinateAsked && waited >= wait + respond) Go(ctx, corps);
                }
            }
        }

        private static void Go(BattleContext ctx, CorpsState corps)
        {
            var t = corps.Order.TargetCorpsId == null ? null : ctx.FindCorps(corps.Order.TargetCorpsId);
            bool byPlayer = corps.OrderByPlayer;
            if (t != null && t.InBattle && ctx.IsVisibleTo(corps.Team, t))
                corps.AssignOrder(new CorpsOrder { Kind = OrderKinds.Attack, TargetCorpsId = t.Id });
            else
                corps.AssignOrder(new CorpsOrder { Kind = OrderKinds.Advance, Mode = MoveModes.AttackMove, ByRole = true });
            corps.OrderByPlayer = byPlayer;
        }
    }
}
