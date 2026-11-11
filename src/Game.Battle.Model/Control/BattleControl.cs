using System.Collections.Generic;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Battle.Model.Skills;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Battle.Model.Control
{
    /// <summary>스킬 버튼 하나에 보여줄 것.</summary>
    public sealed class SkillOption
    {
        public string SkillId { get; internal set; }
        public string NameKey { get; internal set; }
        /// <summary><see cref="SkillDef.TargetType"/>: "self"면 누르자마자 쓴다, 나머지는 대상 지정.</summary>
        public string TargetType { get; internal set; }
        public Fix Cost { get; internal set; }
        public int UsesLeft { get; internal set; }
        /// <summary>점수·횟수가 되는가 (조건·대상은 쓸 때 다시 확인한다).</summary>
        public bool Affordable { get; internal set; }
    }

    /// <summary>전장을 눌렀을 때 일어난 일 (화면이 알림·표시에 쓴다).</summary>
    public enum TapResult
    {
        /// <summary>아무 일 없음 (전장 밖, 선택 없는 빈 땅, 보이지 않는 적).</summary>
        None,
        /// <summary>아군 군단을 골랐다.</summary>
        Selected,
        /// <summary>적 군단을 공격 대상으로 명령했다.</summary>
        Attack,
        /// <summary>그 지점으로 이동 명령.</summary>
        Move,
        /// <summary>지정 중이던 스킬을 썼다 (사거리 밖이면 다가간 뒤).</summary>
        Skill,
        /// <summary>명령을 받지 않았다 (퇴각 중, 도발, 조건·대상 불가 …).</summary>
        Rejected,
    }

    /// <summary>
    /// 전투 화면의 입력 계층 (battle.md 4.1·4.3): 선택 상태를 한 곳에서 관리하고, 누름·버튼을 <see cref="BattleCommand"/>로 바꿔
    /// <see cref="BattleSimulation.Issue"/>에 넣는다. 받을지 말지는 전투 규칙이 정한다 (여기서는 규칙을 계산하지 않는다).
    /// 엔진과 무관하므로 3D 화면·글자 화면·테스트가 같은 것을 쓴다. 좌표는 전장 칸 단위.
    /// </summary>
    public sealed class BattleControl
    {
        private readonly BattleSimulation _sim;

        public BattleControl(BattleSimulation sim, string team)
        {
            _sim = sim;
            Team = team;
        }

        /// <summary>이 기기 플레이어의 팀.</summary>
        public string Team { get; }
        public BattleContext Context => _sim.Context;

        /// <summary>안개 끄고 보기 (녹화 재생 전용, replay.md 3장): 모든 군단이 보인다. 전투 규칙(시야·AI)은 바뀌지 않는다.</summary>
        public bool RevealAll { get; set; }

        /// <summary>이 화면에 보이는 군단인가 (내 팀 시야, 또는 안개 끄고 보기).</summary>
        public bool Sees(CorpsState c) => c != null && (RevealAll || Context.IsVisibleTo(Team, c));

        /// <summary>선택된 아군 군단 (없으면 null).</summary>
        public CorpsState Selected { get; private set; }
        /// <summary>대상 지정 중인 스킬 id (없으면 null).</summary>
        public string TargetingSkill { get; private set; }
        /// <summary>빈 땅을 눌렀을 때 쓰는 이동 방식 (<see cref="MoveModes"/>).</summary>
        public string MoveMode { get; private set; } = MoveModes.Normal;
        /// <summary>예약 모드: 켜져 있으면 이동·공격·휴식·협공 명령을 지금 명령 뒤에 넣는다 (경유, battle.md 4.3.5).</summary>
        public bool Queueing { get; private set; }
        public void SetQueueing(bool on) => Queueing = on;

        /// <summary>선택 군단의 예약 수.</summary>
        public int QueueCount => Selected?.Queue.Count ?? 0;

        /// <summary>군단을 고르는 여유 반경 (칸). 화면이 터치 크기에 맞게 바꾼다.</summary>
        public Fix PickRadius { get; set; } = Fix.FromInt(2);

        /// <summary>이 플레이어가 지휘하는 군단 (예비 포함, 순서는 전투 순서).</summary>
        public List<CorpsState> OwnCorps()
        {
            var list = new List<CorpsState>();
            foreach (var c in Context.Corps)
                if (c.Team == Team && c.Setup.Controller == Controllers.Player) list.Add(c);
            return list;
        }

        public bool CanCommand(CorpsState c) => c != null && c.Team == Team && c.Setup.Controller == Controllers.Player && c.Status == CorpsStatus.Active;

        public bool Select(string corpsId)
        {
            var c = Context.FindCorps(corpsId);
            if (c == null || c.Team != Team || c.Setup.Controller != Controllers.Player) return false;
            Selected = c;
            TargetingSkill = null;
            return true;
        }

        public void ClearSelection()
        {
            Selected = null;
            TargetingSkill = null;
        }

        /// <summary>선택이 전투에서 빠졌으면 다음 지휘 가능 군단으로 (없으면 해제). 매 틱 뒤에 부른다.</summary>
        public void Refresh()
        {
            if (Selected != null && Selected.InBattle) return;
            Selected = null;
            TargetingSkill = null;
            foreach (var c in OwnCorps())
                if (CanCommand(c)) { Selected = c; return; }
        }

        public void SetMoveMode(string mode)
        {
            if (mode == MoveModes.Normal || mode == MoveModes.Forced || mode == MoveModes.AttackMove) MoveMode = mode;
        }

        // ---- 버튼 명령 ----

        /// <summary>전군 진군 (하단 고정 버튼, 4.3.2): 지휘 가능한 모든 군단. 받은 군단 수.</summary>
        /// <summary>
        /// 전군 진군 (R-60): 군단마다 자기 줄을 지키며 우리 진영의 앞 방향으로 곧게 (전장 끝까지, 공격 이동: 가는 길의 적과 싸운다).
        /// 가장 가까운 적으로 모이지 않는다.
        /// </summary>
        public int AdvanceAll()
        {
            int n = 0;
            foreach (var c in OwnCorps())
                if (CanCommand(c) && AdvanceByRole(c)) n++;
            return n;
        }

        /// <summary>진군 (R-60·R-63): 고른 군단이 편성 역할대로 앞으로 곧게.</summary>
        public bool Advance() => CanCommand(Selected) && AdvanceByRole(Selected);

        /// <summary>
        /// 역할별 곧은 진군 (R-63, 2026-11-11 고침): 선봉·측면·역할 없음 = 자기 줄 그대로 앞으로 곧게 (공격 이동).
        /// 후위 = 앞으로 곧게 가다가 적이 rearStandoffCells 안에 들어오면 멈춰 싸운다 (지원 이동, 쫓지 않음).
        /// </summary>
        private bool AdvanceByRole(CorpsState c)
        {
            var far = Fix.FromInt(Context.Field.Width + Context.Field.Height);
            string mode = c.Setup.AdvanceRole == AdvanceRoles.Rear ? MoveModes.Support : MoveModes.AttackMove;
            return Straight(c, Context.Forward(c), far, mode);
        }

        /// <summary>
        /// 후퇴 (R-60): 고른 군단이 뒤로 곧게 fallBackCells칸 (강행: 멈추지 않는다). 전장을 떠나는 퇴각(<see cref="Retreat"/>)과 달리
        /// 계속 명령할 수 있어 다시 진군하면 앞으로 나간다 (위장 후퇴로 끌어들이기).
        /// </summary>
        public bool FallBack() => CanCommand(Selected) && Straight(Selected, Vec.Zero - Forward(Selected), Context.Combat.FallBackCells, MoveModes.Forced);

        private Vec Forward(CorpsState c) => Context.Forward(c);

        /// <summary>지금 위치에서 dir로 cells칸 (전장 안으로 자름). 지금 자리와 같아지면(벽에 붙음) 명령하지 않는다.</summary>
        private bool Straight(CorpsState c, Vec dir, Fix cells, string mode)
        {
            var p = Clamp(c.Anchor + dir * cells);
            if (p.X == c.Anchor.X && p.Y == c.Anchor.Y) return false;
            return Issue(c, CommandKinds.MoveTo, cmd => { cmd.X = p.X; cmd.Y = p.Y; cmd.Mode = mode; cmd.Queued = Queueing; });
        }

        private Vec Clamp(Vec p) => Context.ClampToField(p);

        public bool Hold() => Issue(Selected, CommandKinds.Hold);

        /// <summary>휴식 (사기·책략 회복이 빠름, 싸우면 깨짐). 예약 모드면 예약.</summary>
        public bool Rest() => Issue(Selected, CommandKinds.Hold, c => { c.Mode = HoldModes.Rest; c.Queued = Queueing; });

        /// <summary>협공 대기 (함께 협공 대기하는 군단이 모두 자리를 잡으면 함께 공격). 대상이 없으면 가장 가까운 적. 예약 모드면 예약.</summary>
        public bool Coordinate(string targetCorpsId = null) =>
            Issue(Selected, CommandKinds.Hold, c => { c.Mode = HoldModes.Coordinate; c.TargetCorpsId = targetCorpsId; c.Queued = Queueing; });

        /// <summary>협공 대기를 계속 (물음에 [계속 대기], 기다린 시간을 다시 센다).</summary>
        public bool KeepCoordinating(string corpsId)
        {
            var c = Context.FindCorps(corpsId);
            if (c == null || !c.Coordinating) return false;
            string target = c.Order.TargetCorpsId;
            return Issue(c, CommandKinds.Hold, x => { x.Mode = HoldModes.Coordinate; x.TargetCorpsId = target; });
        }

        /// <summary>협공을 기다리지 않고 바로 공격 (물음에 [공격]).</summary>
        public bool AttackNow(string corpsId)
        {
            var c = Context.FindCorps(corpsId);
            if (c == null) return false;
            string target = c.Order.TargetCorpsId;
            var t = target == null ? null : Context.FindCorps(target);
            return t != null && t.InBattle && Context.IsVisibleTo(Team, t)
                ? Issue(c, CommandKinds.Attack, x => x.TargetCorpsId = target)
                : Issue(c, CommandKinds.Advance);
        }
        public bool Retreat() => Issue(Selected, CommandKinds.Retreat);

        public bool MoveTo(Vec point, string mode = null) =>
            Issue(Selected, CommandKinds.MoveTo, c => { c.X = point.X; c.Y = point.Y; c.Mode = mode ?? MoveMode; c.Queued = Queueing; });

        public bool Attack(string targetCorpsId) =>
            Issue(Selected, CommandKinds.Attack, c => { c.TargetCorpsId = targetCorpsId; c.Queued = Queueing; });

        public bool ChangeFormation(string formationId) =>
            Issue(Selected, CommandKinds.ChangeFormation, c => c.FormationId = formationId);

        public bool SetDelegate(bool on) =>
            Issue(Selected, CommandKinds.Delegate, c => c.Mode = on ? "on" : "off");

        /// <summary>선택 군단이 바꿀 수 있는 진형 (편성에서 배운 진형, 없으면 지금 진형만).</summary>
        public List<string> Formations()
        {
            var list = new List<string>();
            if (Selected == null) return list;
            foreach (var f in Selected.Setup.KnownFormations)
                if (Context.Defs.Formations.ContainsKey(f) && !list.Contains(f)) list.Add(f);
            if (!list.Contains(Selected.Formation.Id)) list.Insert(0, Selected.Formation.Id);
            return list;
        }

        /// <summary>선택 군단의 다음 진형으로 (진형 버튼 한 번 누름).</summary>
        public bool NextFormation()
        {
            var list = Formations();
            if (list.Count < 2) return false;
            int i = list.IndexOf((Selected.PendingFormation ?? Selected.Formation).Id); // 바꾸는 중이면 그 다음 진형
            return ChangeFormation(list[(i + 1) % list.Count]);
        }

        // ---- 스킬 (4.2) ----

        public List<SkillOption> Skills()
        {
            var list = new List<SkillOption>();
            if (Selected == null || Selected.KnownSkills == null) return list;
            foreach (var s in Selected.KnownSkills)
                list.Add(new SkillOption
                {
                    SkillId = s.Id,
                    NameKey = s.NameKey,
                    TargetType = s.TargetType,
                    Cost = Combat.TraitEffects.SkillCost(Context, Selected, s.Id, s.Cost),
                    UsesLeft = Selected.SkillPoints.UsesLeft(s.Id),
                    Affordable = Selected.SkillPoints.CanUse(s.Id, Combat.TraitEffects.SkillCost(Context, Selected, s.Id, s.Cost), true),
                });
            return list;
        }

        /// <summary>
        /// 스킬 버튼: 자신 대상이면 바로 쓰고 true. 대상이 필요하면 지정 모드로 들어가고 true (다음 누름이 대상).
        /// 같은 스킬을 다시 누르면 지정 모드를 끝낸다 (false).
        /// </summary>
        public bool PressSkill(string skillId)
        {
            if (!CanCommand(Selected) || !Context.Defs.Skills.TryGetValue(skillId ?? "", out var skill)) return false;
            if (TargetingSkill == skillId) { TargetingSkill = null; return false; }
            if (skill.TargetType == SkillTargets.Self)
            {
                TargetingSkill = null;
                return Issue(Selected, CommandKinds.UseSkill, c => c.SkillId = skillId);
            }
            TargetingSkill = skillId;
            return true;
        }

        public void CancelTargeting() => TargetingSkill = null;

        // ---- 전장 누름 (4.1) ----

        /// <summary>
        /// 전장의 한 점을 눌렀다. 지정 중인 스킬이 있으면 그 대상, 아니면 아군 = 선택, 보이는 적 = (선택이 있으면) 공격, 빈 땅 = (선택이 있으면) 이동.
        /// </summary>
        public TapResult Tap(Vec point)
        {
            var map = Context.Field.Map;
            if (!map.InBounds((int)point.X.FloorToLong(), (int)point.Y.FloorToLong())) return TapResult.None;
            var hit = Pick(point);

            if (TargetingSkill != null && CanCommand(Selected))
            {
                var skill = Context.Defs.Skills[TargetingSkill];
                string id = TargetingSkill;
                bool ok;
                if (skill.TargetType == SkillTargets.EnemyCorps || skill.TargetType == SkillTargets.AllyCorps)
                {
                    if (hit == null) return TapResult.None; // 군단을 골라야 한다: 지정 모드 유지
                    ok = Issue(Selected, CommandKinds.UseSkill, c => { c.SkillId = id; c.TargetCorpsId = hit.Id; });
                }
                else ok = Issue(Selected, CommandKinds.UseSkill, c => { c.SkillId = id; c.X = point.X; c.Y = point.Y; });
                TargetingSkill = null;
                return ok ? TapResult.Skill : TapResult.Rejected;
            }

            if (hit != null && hit.Team == Team)
            {
                if (hit.Setup.Controller != Controllers.Player) return TapResult.None;
                Select(hit.Id);
                return TapResult.Selected;
            }
            if (Selected == null) return TapResult.None;
            if (hit != null) return Attack(hit.Id) ? TapResult.Attack : TapResult.Rejected;
            return MoveTo(point) ? TapResult.Move : TapResult.Rejected;
        }

        /// <summary>
        /// 그 점에서 고를 군단: 여유 반경 안에서 가장 가까운 부대원의 군단, 같은 거리면 아군 우선 (4.1). 보이지 않는 적·전투 밖 군단은 고르지 않는다.
        /// </summary>
        public CorpsState Pick(Vec point)
        {
            CorpsState best = null;
            Fix bestD = Fix.Zero;
            Fix r2 = PickRadius * PickRadius;
            foreach (var c in Context.Corps)
            {
                if (!c.InBattle || !Context.IsVisibleTo(Team, c)) continue;
                foreach (var u in c.Units)
                {
                    if (!u.Alive) continue;
                    Fix d = Vec.DistanceSquared(u.Position, point);
                    if (d > r2) continue;
                    bool better = best == null || d < bestD || (d == bestD && c.Team == Team && best.Team != Team);
                    if (better) { best = c; bestD = d; }
                }
            }
            return best;
        }

        private bool Issue(CorpsState corps, string kind, System.Action<BattleCommand> fill = null)
        {
            if (corps == null) return false;
            var cmd = new BattleCommand { CorpsId = corps.Id, Kind = kind };
            fill?.Invoke(cmd);
            return _sim.Issue(cmd);
        }
    }
}
