using System.Collections.Generic;
using Game.Battle.Model.Contests;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Campaign.Captivity;
using Game.Campaign.Progress;
using Game.Campaign.Roster;
using Game.Campaign.Troops;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Data.Rules;
using Game.Economy;
using Game.Foundation.Math;
using Game.Foundation.Random;
using Game.Foundation.Time;
using Game.Online;
using Game.Progression;

namespace Game.Flow
{
    /// <summary>출진 요청 (출진 준비 화면의 선택).</summary>
    public sealed class SortieRequest
    {
        public string StepId { get; set; }
        /// <summary>출전할 군단 편성. 비면 저장된 편성(CampaignState.Compositions) 그대로.</summary>
        public List<CorpsComposition> Compositions { get; set; }
        /// <summary>가져갈 군량 합계. 없으면 자동 계산 (R-7: 출전 병력 기준 3분 분량).</summary>
        public long? Supply { get; set; }
        /// <summary>전투 시드 (서버 도입 후 서버가 정함).</summary>
        public ulong Seed { get; set; }
        /// <summary>대결 id → 플레이어 선택 기록 (없거나 모자라면 AI가 대신 고름).</summary>
        public Dictionary<string, List<string>> ContestChoices { get; set; } = new Dictionary<string, List<string>>();
        /// <summary>위임 출진: 모든 아군 군단을 AI가 지휘 (자동 전투). 전투 중 delegate 명령으로 군단마다 바꿀 수 있다.</summary>
        public bool Delegate { get; set; }
        /// <summary>이 장수들의 군단만 위임 (Delegate가 false일 때).</summary>
        public List<string> DelegateGenerals { get; set; } = new List<string>();
        /// <summary>고용한 장수·부대 (R-43, <see cref="HireRules.Resolve"/>). 편성(Compositions)에서 장수 정의 id·"hire.등록id" 부대로 쓴다.</summary>
        public HiredForces Hired { get; set; }
        /// <summary>영토 전투일 때 공격자의 위치 "위도,경도" (공격할 수 있는 지역인지, R-46).</summary>
        public string Location { get; set; }
        /// <summary>영토 전투에서 주인이 등록한 수비 편성 (없으면 지역의 AI 수비군, R-48).</summary>
        public TerritoryDefense Defense { get; set; }
    }

    /// <summary>출진 준비 결과. 문제가 없으면 Setup으로 전투를 시작한다.</summary>
    public sealed class SortiePlan
    {
        public StepDef Step { get; internal set; }
        public BattleSetup Setup { get; internal set; }
        /// <summary>아군 군단 id → 편성 (결과 반영용).</summary>
        public Dictionary<string, CorpsComposition> PlayerCorps { get; } = new Dictionary<string, CorpsComposition>();
        public long SupplyTaken { get; internal set; }
        public long AutoSupply { get; internal set; }
        public List<string> Errors { get; } = new List<string>();
        public bool Ok => Errors.Count == 0;
        /// <summary>출진 준비를 한 시각 (시간 서비스 기준 unix 초). 서버는 이 시각으로 다시 준비한다 (R-42).</summary>
        public long PreparedAtUnixSeconds { get; internal set; }
        /// <summary>고용 (없으면 null). 비용은 출진 확정 때 낸다.</summary>
        public HiredForces Hired { get; internal set; }
        /// <summary>이 출진에 쓴 편성 (제출 메시지에 싣는다, 고용이 있을 때 서버가 같은 편성을 쓰도록).</summary>
        public List<CorpsComposition> Compositions { get; internal set; }
        /// <summary>영토 전투의 위치 (제출 메시지에 싣는다).</summary>
        public string Location { get; internal set; }
        /// <summary>영토 전투에서 상대한 주인의 수비 편성 (없으면 null).</summary>
        public TerritoryDefense Defense { get; internal set; }
    }

    public sealed class UnitReport
    {
        public string UnitId { get; internal set; }
        public long Losses { get; internal set; }
        public long Wounded { get; internal set; }
        public long Dead { get; internal set; }
        public Fix CohesionBefore { get; internal set; }
        public Fix CohesionAfter { get; internal set; }
    }

    /// <summary>결과 화면에 보여줄 것 (story.md 5장).</summary>
    public sealed class BattleReport
    {
        public bool Won { get; internal set; }
        public bool FirstClear { get; internal set; }
        public List<UnitReport> Units { get; } = new List<UnitReport>();
        public long SupplyReturned { get; internal set; }
        public long SupplyCaptured { get; internal set; }
        public Dictionary<string, string> Injuries { get; } = new Dictionary<string, string>();
        public List<string> CapturedOwn { get; } = new List<string>();
        public List<string> NewPrisoners { get; } = new List<string>();
        public List<string> Rescued { get; } = new List<string>();
        public List<string> UnlockedFormations { get; } = new List<string>();
        public List<string> UnlockedSkills { get; } = new List<string>();
        /// <summary>합류한 장수 (첫 클리어 보상). 이미 있던 장수면 JoinedAsShard에.</summary>
        public List<string> JoinedGenerals { get; } = new List<string>();
        public List<string> JoinedAsShard { get; } = new List<string>();
        public Dictionary<string, long> RewardResources { get; } = new Dictionary<string, long>();
        public List<(string generalId, GrowthReward reward)> GrowthRewards { get; } = new List<(string, GrowthReward)>();
        /// <summary>장수 id → 레벨업 결과 (오른 것만).</summary>
        public Dictionary<string, LevelUpResult> LevelUps { get; } = new Dictionary<string, LevelUpResult>();
        public List<FoundSecret> Secrets { get; } = new List<FoundSecret>();
    }

    /// <summary>
    /// 스토리 진행 흐름 (story.md 4·5장, flow.md): 스텝 + 편성 → <see cref="BattleSetup"/>, <see cref="BattleResult"/> → 진행 상태.
    /// 규칙 모듈(전투·진행·경제·성장)을 경계 객체로만 잇는다. 전투 규칙·진행 규칙은 이 클래스에 없다.
    /// </summary>
    public sealed class StoryFlow
    {
        public const string PlayerTeam = "teamA";
        public const string EnemyTeam = "teamB";

        private readonly DefinitionSet _defs;
        private readonly IDataSource _source;
        private readonly ITimeProvider _time;
        private readonly RuleFlags _campaignFlags;
        private Game.Territory.TerritoryDefs _territory;
        private bool _territoryLoaded;

        /// <summary>영토 데이터 (없으면 null, 처음 쓸 때 읽는다).</summary>
        public Game.Territory.TerritoryDefs Territory
        {
            get
            {
                if (!_territoryLoaded) { _territory = TerritoryBattles.TryLoad(_source); _territoryLoaded = true; }
                return _territory;
            }
        }

        /// <summary>스텝 id → 스텝: "territory.지역"이면 지역 수비군으로, 아니면 Steps/ 파일.</summary>
        public StepDef LoadStep(string stepId) =>
            TerritoryBattles.IsTerritoryStep(stepId) ? TerritoryBattles.StepFor(Territory, TerritoryBattles.RegionOf(stepId)) : StepDef.Load(_source, stepId);

        /// <summary>
        /// 스텝에서 정해지는 전투 규칙 (데이터·맵·보정·스위치)을 채운다. 녹화 재생이 기록에 없는 부분을 지금 데이터로 다시 채울 때 쓴다 (replay.md 2·4장).
        /// <see cref="Prepare"/>와 같은 값이어야 한다 (녹화 테스트가 같은 결과인지 본다).
        /// </summary>
        public void ApplyStepRules(BattleSetup setup, StepDef step)
        {
            setup.Defs = _defs;
            setup.Map = MapDef.Load(_source, step.Map);
            setup.Balance = _defs.ResolveBalance(step.BalanceOverrides);
            setup.Combat = _defs.ResolveCombat(step.CombatOverrides);
            setup.Flags = _defs.ResolveFlags(step.RuleOverrides);
        }

        public const string NavalBattleType = "naval";

        public StoryFlow(DefinitionSet defs, IDataSource source, ITimeProvider time)
        {
            _defs = defs;
            _source = source;
            _time = time;
            _campaignFlags = defs.ResolveFlags();
        }

        public CaptivityService Captivity => new CaptivityService(_defs, _campaignFlags, _time);
        public InjuryService Injuries => new InjuryService(_defs, _campaignFlags, _time);

        /// <summary>시간 경과 처리: 장수 부상 회복, 부상병 자연 회복(R-61), 구출 기한 지난 장수 석방. 화면을 열 때마다 부른다.</summary>
        public void Tick(CampaignState state)
        {
            Injuries.Recover(state);
            long now = _time.UtcNow.ToUnixTimeSeconds();
            foreach (var u in state.Units) CasualtyService.RecoverDue(u, now);
            Captivity.ReleaseExpired(state);
        }

        public bool IsUnlocked(CampaignState state, string stepId)
        {
            // 훈련 전투 (R-85): 그 태그의 부대가 있으면 열린다 (스토리 순서 밖, 반복 가능)
            if (_defs.StepIndex.Drills.TryGetValue(stepId, out var tag))
            {
                foreach (var u in state.Units)
                    if (_defs.TroopTypes.TryGetValue(u.TroopType, out var t) && t.HasTag(tag)) return true;
                return false;
            }
            return StepProgress.IsUnlocked(_defs.StepIndex, state, stepId, Captivity.CampsWithCaptives(state));
        }

        /// <summary>출진 준비: 상태는 바꾸지 않는다. 문제(편성·군량·해금)는 Errors로 돌려준다.</summary>
        /// <summary>전투 소부대 id에서 병종 묶음 id를 나누는 표시 (묶음 id + "@" + 진형 칸).</summary>
        public const string SubUnitMark = "@";

        /// <summary>전투 소부대 id → 병종 묶음 id (표시가 없으면 그대로: 고용 부대 등).</summary>
        public static string PoolIdOf(string subUnitId)
        {
            int i = subUnitId == null ? -1 : subUnitId.IndexOf(SubUnitMark, System.StringComparison.Ordinal);
            return i < 0 ? subUnitId : subUnitId.Substring(0, i);
        }

        /// <summary>스텝 데이터의 팀 이름("player"/"enemy") → 전투 팀 id.</summary>
        private static string StepTeam(string t) => t == "enemy" ? EnemyTeam : PlayerTeam;

        public SortiePlan Prepare(CampaignState state, SortieRequest request)
        {
            var plan = new SortiePlan { PreparedAtUnixSeconds = _time.UtcNow.ToUnixTimeSeconds(), Location = request.Location };
            var step = LoadStep(request.StepId);
            if (step == null) throw new System.ArgumentException("없는 스텝 '" + request.StepId + "'.");
            var map = MapDef.Load(_source, step.Map);
            plan.Step = step;
            // 고용이 있으면 내 세이브 사본에 고용 장수·부대를 더한 것으로 편성 검사·군단 구성 (원본 세이브는 그대로, 결과는 내 것에만 반영)
            var hired = request.Hired != null && !request.Hired.IsEmpty ? request.Hired : null;
            if (hired != null) { state = WithHires(state, hired, plan.Errors); plan.Hired = hired; }
            if (step.IsTerritory)
            {
                var err = TerritoryBattles.CheckSortie(Territory, TerritoryBattles.RegionOf(step.Id), request.Location, _defs, state);
                if (err != null) plan.Errors.Add(err);
            }
            else if (!IsUnlocked(state, step.Id)) plan.Errors.Add($"스텝 '{step.Id}'가 아직 열리지 않았습니다.");

            // 전장 유형에 맞는 편성 (R-35), 계급의 군단·칸 수까지·병력이 줄었으면 비율대로 (army-v2.md 4장)
            var comps = Army.ForSortie(_defs, state, request.Compositions ?? state.CompositionsFor(step.BattleType));
            plan.Compositions = comps;
            int maxCorps = step.MaxCorps ?? _defs.Campaign.DefaultMaxCorps;
            if (comps.Count == 0) plan.Errors.Add("출전할 군단이 없습니다.");
            if (comps.Count > maxCorps) plan.Errors.Add($"출전 군단 {comps.Count}개가 이 스텝의 한도 {maxCorps}개를 넘습니다.");
            plan.Errors.AddRange(CorpsValidator.Validate(_defs, state, comps));

            var flags = _defs.ResolveFlags(step.RuleOverrides);
            var balance = _defs.ResolveBalance(step.BalanceOverrides);
            var setup = new BattleSetup
            {
                Defs = _defs,
                Map = map,
                Balance = balance,
                Combat = _defs.ResolveCombat(step.CombatOverrides),
                Flags = flags,
                Seed = request.Seed,
                MaxMinutes = step.MaxMinutes,
                DefeatConditions = new List<string>(step.DefeatConditions),
                HoldOut = step.HoldOutTeam == null ? null : new Game.Battle.Model.Victory.HoldOutSetup { Team = StepTeam(step.HoldOutTeam), Seconds = step.HoldOutSeconds },
                Weather = step.Weather,
                TimeOfDay = step.TimeOfDay,
                BattleType = step.BattleType,
                Wind = new WindSetup { Direction = step.WindDirection, Strength = step.WindStrength },
            };
            setup.InitialExplored[PlayerTeam] = StepProgress.Explored(state, step.Id, map.Width * map.Height);
            foreach (var ob in step.Objects) // 맵 오브젝트·점령 구역 (R-78)
                setup.Objects.Add(new Game.Battle.Model.Victory.MapObjectSetup
                {
                    Id = ob.Id, Type = ob.Type, X = ob.X, Y = ob.Y, Radius = ob.Radius, Value = ob.Value,
                    Team = ob.Team == null ? null : StepTeam(ob.Team), Seconds = ob.Seconds,
                    FloodX = ob.FloodX, FloodY = ob.FloodY, FloodRadius = ob.FloodRadius,
                });

            // 아군: 편성에서 (부상병은 부대에 따로 있으므로 병력만), 군량은 하루 소비 비율로 나눔
            var daily = new List<long>();
            int n = 0;
            foreach (var comp in comps)
            {
                var g = state.FindGeneral(comp.GeneralId);
                if (g == null) continue;
                string id = "P" + (++n);
                var cs = new CorpsSetup
                {
                    Id = id,
                    Team = PlayerTeam,
                    // 총대장 = ★ 군단 (없으면 첫 군단). 역할(선봉·측면·후위)은 포진판 줄에서, 없으면 편성의 역할 (battle.md 4.3.2)
                    Role = (comps.Exists(x => x.Commander) ? comp.Commander : n == 1) ? CorpsRoles.Commander : null,
                    AdvanceRole = AdvanceRoles.IsValid(Army.RoleOf(comp)) ? Army.RoleOf(comp) : null,
                    SlotRow = comp.BoardRow,
                    SlotCol = comp.BoardCol,
                    Controller = Controllers.Player,
                    General = Snapshot(g),
                    Formation = comp.Formation,
                    KnownFormations = new List<string>(state.LearnedFormations),
                    Delegated = request.Delegate || request.DelegateGenerals.Contains(comp.GeneralId),
                };
                long d = 0;
                // 병종 칸 → 진형 칸 6개 자동 배치 (army-v2.md 5장). 전투 소부대 id = 병종 묶음 id + "@" + 칸 (결과를 묶음으로 모은다)
                var picks = new List<(string unitId, string troopType, long troops)>();
                foreach (var p in comp.Picks) { var pu = state.FindUnit(p.UnitId); if (pu != null) picks.Add((p.UnitId, pu.TroopType, p.Troops)); }
                foreach (var st in Army.Layout(_defs, picks))
                {
                    var u = state.FindUnit(st.UnitId);
                    cs.Units.Add(new UnitSetup { UnitId = u.UnitId + SubUnitMark + st.Slot, Slot = st.Slot, TroopType = u.TroopType, Troops = st.Troops, Cohesion = u.Cohesion, Training = u.Training });
                    if (_defs.TroopTypes.TryGetValue(u.TroopType, out var tt)) d = checked(d + st.Troops * tt.SupplyPerDay);
                }
                daily.Add(d);
                setup.Corps.Add(cs);
                plan.PlayerCorps[id] = comp;
            }
            if (!HasCommander(setup)) foreach (var c in setup.Corps) { c.Role = CorpsRoles.Commander; break; }

            long totalDaily = 0;
            foreach (var d in daily) totalDaily += d;
            plan.AutoSupply = SupplyCalculator.AutoSortieSupply(balance, totalDaily);
            long take = request.Supply ?? plan.AutoSupply;
            plan.SupplyTaken = take;
            if (take < 0) plan.Errors.Add("군량은 음수일 수 없습니다.");
            if (new Wallet(state.Wallet).Get(ResourceIds.Supply) < take) plan.Errors.Add($"군량이 모자랍니다 (필요 {take}, 보유 {new Wallet(state.Wallet).Get(ResourceIds.Supply)}).");
            if (hired != null)
                foreach (var kv in hired.Costs)
                {
                    long need = kv.Key == ResourceIds.Supply ? take + kv.Value : kv.Value;
                    if (take >= 0 && new Wallet(state.Wallet).Get(kv.Key) < need) plan.Errors.Add($"고용 비용이 모자랍니다 ({kv.Key} 필요 {need}).");
                }
            SplitSupply(setup, daily, take);

            // 적: 영토 주인의 수비 편성이 있으면 그것으로 (R-48), 아니면 스텝 데이터
            bool ownerDefense = step.IsTerritory && request.Defense != null && request.Defense.Corps != null && request.Defense.Corps.Count > 0;
            if (ownerDefense)
            {
                plan.Defense = request.Defense;
                AddDefense(setup, request.Defense, balance, plan.Errors);
            }
            foreach (var e in ownerDefense ? new List<StepEnemyCorpsDef>() : new List<StepEnemyCorpsDef>(step.Enemy))
            {
                var gd = _defs.Generals[e.General];
                var cs = new CorpsSetup
                {
                    Id = e.Id,
                    Team = EnemyTeam,
                    Role = e.Role,
                    Controller = Controllers.Ai,
                    AiStance = e.Ai,
                    AiDifficulty = _defs.Ai.Shift(e.Difficulty, state.Difficulty), // 전체 난이도 설정 (R-75)
                    General = GeneralSnapshot.From(gd),
                    Formation = e.Formation,
                    Supply = e.Supply,
                    KnownFormations = new List<string>(e.KnownFormations),
                    X = e.X, Y = e.Y, Facing = e.Facing,
                    Reserve = e.Reserve,
                };
                long enemyDaily = 0;
                foreach (var u in e.Units)
                {
                    cs.Units.Add(new UnitSetup { UnitId = e.Id + "_" + u.Slot, Slot = u.Slot, TroopType = u.TroopType, Troops = u.Troops });
                    enemyDaily = checked(enemyDaily + u.Troops * _defs.TroopTypes[u.TroopType].SupplyPerDay);
                }
                // 적 군량을 분 분량으로 정했으면 그 군단 병력 기준으로 계산 (R-6 식, 올림)
                if (e.SupplyMinutes.HasValue)
                    cs.Supply = (e.SupplyMinutes.Value * (enemyDaily * balance.SupplyDaysPerMinute)).CeilToLong();
                setup.Corps.Add(cs);
            }

            foreach (var ev in step.Events)
            {
                var es = new EventSetup
                {
                    Id = ev.Id,
                    Once = ev.Once,
                    Trigger = new TriggerSetup { Type = ev.TriggerType, Tick = ev.TriggerTick, CorpsId = ev.TriggerCorps, Team = TeamOf(ev.TriggerTeam), Ratio = ev.TriggerRatio },
                };
                foreach (var a in ev.Actions) es.Actions.Add(new ActionSetup { Type = a.Type, Key = a.Key, CorpsId = a.Corps, Stance = a.Stance });
                setup.Events.Add(es);
            }

            // 수상전 (R-85): 배(naval)만 물에 뜬다 → 수군이 아닌 부대가 있으면 출진 불가
            if (step.BattleType == NavalBattleType)
                foreach (var c in setup.Corps)
                {
                    if (c.Team != PlayerTeam) continue;
                    foreach (var u in c.Units)
                        if (!_defs.TroopTypes[u.TroopType].HasTag(TerrainRuleDef.NavalTag))
                        {
                            plan.Errors.Add($"수상전에는 수군 부대만 나갈 수 있습니다 ({_defs.TroopTypes[u.TroopType].Id}). 편성 탭에서 [수상전] 편성을 만드세요.");
                            goto naval_done;
                        }
                }
            naval_done:
            PlanContests(step, setup, request);
            plan.Setup = setup;
            return plan;
        }

        /// <summary>수비 편성 → 적 군단 (AI 수비 태세, 군량 10분 분량). 장수는 성장·배운 스킬까지 그대로.</summary>
        private void AddDefense(BattleSetup setup, TerritoryDefense defense, Game.Data.Rules.BalanceData balance, List<string> errors)
        {
            int k = 0;
            foreach (var dc in defense.Corps)
            {
                GeneralState g = null;
                try { g = OnlineJson.Read<GeneralState>(dc.GeneralJson); } catch (System.Exception) { }
                if (g == null || !_defs.Generals.ContainsKey(g.GeneralId ?? "") || dc.Formation == null) { errors.Add("수비 편성을 읽을 수 없습니다."); return; }
                string id = "D" + (++k);
                var cs = new CorpsSetup
                {
                    Id = id,
                    Team = EnemyTeam,
                    Role = k == 1 ? CorpsRoles.Commander : null,
                    Controller = Controllers.Ai,
                    AiStance = Game.Battle.Model.Ai.AiStanceIds.Defensive,
                    AiDifficulty = "normal",
                    General = Snapshot(g),
                    Formation = dc.Formation,
                    KnownFormations = new List<string> { dc.Formation },
                };
                long daily = 0;
                foreach (var du in dc.Units)
                {
                    UnitState u = null;
                    try { u = OnlineJson.Read<UnitState>(du.UnitJson); } catch (System.Exception) { }
                    if (u == null || !_defs.TroopTypes.TryGetValue(u.TroopType ?? "", out var tt) || !SlotIds.IsValid(du.Slot)) { errors.Add("수비 편성의 부대를 읽을 수 없습니다."); return; }
                    cs.Units.Add(new UnitSetup { UnitId = id + "_" + du.Slot, Slot = du.Slot, TroopType = u.TroopType, Troops = u.Troops, Cohesion = u.Cohesion, Training = u.Training });
                    daily = checked(daily + u.Troops * tt.SupplyPerDay);
                }
                cs.Supply = (Fix.FromInt(10) * (daily * balance.SupplyDaysPerMinute)).CeilToLong();
                setup.Corps.Add(cs);
            }
        }

        /// <summary>출진 확정: 보유 군량에서 가져갈 군량을 뺀다.</summary>
        public bool Confirm(CampaignState state, SortiePlan plan)
        {
            if (!plan.Ok) return false;
            var wallet = new Wallet(state.Wallet);
            if (plan.Hired != null)
            {
                // 군량 + 고용 비용을 한꺼번에: 하나라도 모자라면 아무것도 내지 않는다
                foreach (var kv in plan.Hired.Costs)
                    if (wallet.Get(kv.Key) < (kv.Key == ResourceIds.Supply ? plan.SupplyTaken + kv.Value : kv.Value)) return false;
                foreach (var kv in plan.Hired.Costs) if (!wallet.TrySpend(kv.Key, kv.Value)) return false;
            }
            return wallet.TrySpend(ResourceIds.Supply, plan.SupplyTaken);
        }

        /// <summary>세이브 사본 + 고용 장수·부대. 이미 가진 장수와 같은 장수는 고용할 수 없다.</summary>
        private static CampaignState WithHires(CampaignState state, HiredForces hired, List<string> errors)
        {
            var view = SaveJson.Read(SaveJson.Write(state)) ?? state;
            foreach (var g in hired.Generals)
            {
                if (view.FindGeneral(g.GeneralId) != null) { errors.Add($"이미 가진 장수 '{g.GeneralId}'는 고용할 수 없습니다."); continue; }
                view.Generals.Add(g);
            }
            foreach (var u in hired.Units)
            {
                if (!HireRules.IsHiredUnit(u.UnitId) || view.FindUnit(u.UnitId) != null) { errors.Add($"고용 부대 id '{u.UnitId}'가 잘못되었습니다."); continue; }
                view.Units.Add(u);
            }
            return view;
        }

        /// <summary>
        /// 전투 결과 반영 (story.md 5장, flow.md 3장): 손실(부상·사망), 남은 군량 반환 + 승리 시 적 군량 포획(R-8), 부대 결속,
        /// 장수 부상·포로, 적 포로, 대결 전적·성장 보상, 탐색 기억, 스텝 클리어·보상, 구출.
        /// </summary>
        public BattleReport ApplyResult(CampaignState state, SortiePlan plan, BattleResult result)
        {
            var report = new BattleReport { Won = result.Winner == PlayerTeam };
            var step = plan.Step;
            var b = plan.Setup.Balance;
            var wallet = new Wallet(state.Wallet);

            foreach (var cr in result.Corps)
            {
                if (cr.Team == PlayerTeam)
                {
                    foreach (var ur in cr.Units)
                    {
                        var unit = state.FindUnit(PoolIdOf(ur.UnitId));
                        if (unit == null) continue;
                        var (wounded, dead) = CasualtyService.Apply(b, plan.Setup.Flags, unit, ur.Losses);
                        if (wounded > 0) CasualtyService.ScheduleRecovery(b, unit, _time.UtcNow.ToUnixTimeSeconds());
                        // 같은 병종 묶음의 칸들은 보고 한 줄로 모으고, 결속은 묶음마다 한 번만
                        var rep = report.Units.Find(x => x.UnitId == unit.UnitId);
                        if (rep == null)
                        {
                            var before = unit.Cohesion;
                            unit.Cohesion = Cohesion.After(_defs.Campaign, _campaignFlags, unit.Cohesion, report.Won);
                            report.Units.Add(new UnitReport { UnitId = unit.UnitId, Losses = ur.Losses, Wounded = wounded, Dead = dead, CohesionBefore = before, CohesionAfter = unit.Cohesion });
                        }
                        else { rep.Losses += ur.Losses; rep.Wounded += wounded; rep.Dead += dead; }
                    }
                    // 남은 지참 군량은 돌려받는다. 궤멸·포로 군단의 군량은 잃는다.
                    if (cr.Status != CorpsStatus.Destroyed && cr.Status != CorpsStatus.Captured) report.SupplyReturned += cr.RemainingSupply;
                }
                else if (report.Won && cr.Status != CorpsStatus.Active && cr.Status != CorpsStatus.Retreating)
                {
                    // R-8: 퇴각한 적은 생존 병사 몫만 가져가고 나머지 몰수, 궤멸·포로는 전량 몰수
                    bool lost = cr.Status == CorpsStatus.Destroyed || cr.Status == CorpsStatus.Captured;
                    var (_, confiscated) = SupplyCalculator.RetreatSplit(cr.RemainingSupply, cr.RemainingTroops, cr.StartTroops, lost);
                    report.SupplyCaptured += (_defs.Campaign.SupplyCaptureRate * confiscated).FloorToLong();
                }
            }
            wallet.Add(ResourceIds.Supply, report.SupplyReturned + report.SupplyCaptured);

            var injuries = Injuries;
            // 부상은 내 군단의 장수만 (수비 편성에는 같은 장수가 있을 수 있다)
            var mine = new HashSet<string>();
            foreach (var c in plan.Setup.Corps) if (c.Team == PlayerTeam && c.General != null) mine.Add(c.General.Id);
            foreach (var kv in result.GeneralInjuries)
            {
                if (!mine.Contains(kv.Key) || state.FindGeneral(kv.Key) == null) continue;
                injuries.Apply(state, kv.Key, kv.Value);
                report.Injuries[kv.Key] = kv.Value;
            }

            var captivity = Captivity;
            foreach (var cap in result.Captures)
            {
                if (cap.CapturedByTeam == PlayerTeam && plan.Defense != null) continue; // 다른 유저의 수비 편성 장수는 포로로 데려오지 않는다 (스냅샷)
                if (cap.CapturedByTeam == PlayerTeam)
                {
                    captivity.AddPrisoner(state, cap.GeneralId, cap.BrainwashBonus);
                    if (state.FindPrisoner(cap.GeneralId) != null) report.NewPrisoners.Add(cap.GeneralId);
                }
                else if (state.FindGeneral(cap.GeneralId) != null)
                {
                    captivity.CaptureOwn(state, cap.GeneralId, step.Camp ?? step.Id);
                    report.CapturedOwn.Add(cap.GeneralId);
                }
            }

            RecordContests(state, result, report);
            BattleRecords.Record(state, plan.PlayerCorps, result);

            // 영토 전투는 탐색 기억·스텝 클리어·첫 보상이 없다 (차지는 서버가 기록, R-46)
            if (!step.IsTerritory && result.Explored.TryGetValue(PlayerTeam, out var explored)) StepProgress.MergeExplored(state, step.Id, explored);

            if (report.Won) GrantBattleExp(state, plan, step, report);
            report.Secrets.AddRange(new SecretEngine().Evaluate(new SecretContext { Defs = _defs, State = state, Step = step, Plan = plan, Result = result, Won = report.Won }));

            if (report.Won && !step.IsTerritory)
            {
                if (step.RescueCamp != null) report.Rescued.AddRange(captivity.Rescue(state, step.RescueCamp));
                report.FirstClear = StepProgress.MarkCleared(state, step.Id);
                if (report.FirstClear) GrantRewards(state, step, report);
            }
            return report;
        }

        private void RecordContests(CampaignState state, BattleResult result, BattleReport report)
        {
            foreach (var c in result.Contests)
            {
                foreach (var (side, fighter) in new[] { ("A", c.A), ("B", c.B) })
                {
                    var g = state.FindGeneral(fighter.Id);
                    if (g == null) continue;
                    foreach (var r in ContestRecords.Record(_defs, _campaignFlags, g, c.ContestId, ContestRecords.OutcomeOf(c, side)))
                        report.GrowthRewards.Add((g.GeneralId, r));
                }
            }
        }

        /// <summary>승리 시 출전 장수 경험치 (스텝 경험치 × 전투 배율, 아주 더디게, battleLevelUp). 포로가 된 장수는 제외.</summary>
        private void GrantBattleExp(CampaignState state, SortiePlan plan, StepDef step, BattleReport report)
        {
            if (!_campaignFlags.IsEnabled(LevelEngine.BattleLevelUpFlag)) return;
            long exp = LevelEngine.BattleExp(_defs.Levels, step.RewardExp);
            foreach (var comp in plan.PlayerCorps.Values)
            {
                var g = state.FindGeneral(comp.GeneralId);
                if (g == null || g.Status == GeneralStatus.Captured) continue;
                var r = LevelEngine.AddExp(_defs.Levels, _defs.Generals[g.GeneralId].GrowthRates, g.Level, g.Exp, exp, LimitBreakService.MaxLevelOf(_defs, g));
                g.Level = r.Level;
                g.Exp = r.Exp;
                foreach (var kv in r.StatGains)
                {
                    g.StatGrowth.TryGetValue(kv.Key, out var v);
                    g.StatGrowth[kv.Key] = v + kv.Value;
                }
                if (r.LevelsGained > 0) report.LevelUps[g.GeneralId] = r;
            }
        }

        private void GrantRewards(CampaignState state, StepDef step, BattleReport report)
        {
            foreach (var f in step.RewardFormations)
                if (!state.LearnedFormations.Contains(f)) { state.LearnedFormations.Add(f); report.UnlockedFormations.Add(f); }
            foreach (var s in step.RewardSkills) report.UnlockedSkills.Add(s); // 스킬 해금 = 스킬북 획득처 (상점·보상), 장수에게 쓰는 것은 키우기 탭
            var wallet = new Wallet(state.Wallet);
            foreach (var kv in step.RewardResources) { wallet.Add(kv.Key, kv.Value); report.RewardResources[kv.Key] = kv.Value; }
            // 장수 합류 (A3): 없으면 새 장수(기본 스킬·특성), 있으면 조각 1개 (모집 중복과 같다)
            foreach (var id in step.RewardGenerals)
            {
                if (!_defs.Generals.TryGetValue(id, out var def)) continue;
                if (state.FindGeneral(id) != null)
                {
                    state.GeneralShards.TryGetValue(id, out var n);
                    state.GeneralShards[id] = n + 1;
                    report.JoinedAsShard.Add(id);
                    continue;
                }
                state.Generals.Add(GeneralState.Join(def));
                report.JoinedGenerals.Add(id);
            }
        }

        /// <summary>전투에 들어갈 장수 능력치: 정의 + 성장분, 경상이면 배율 (CorpsValidator.EffectiveStat).</summary>
        private GeneralSnapshot Snapshot(GeneralState g)
        {
            var def = _defs.Generals[g.GeneralId];
            return new GeneralSnapshot
            {
                Id = g.GeneralId,
                Leadership = CorpsValidator.EffectiveStat(_defs, g, "leadership"),
                Might = CorpsValidator.EffectiveStat(_defs, g, "might"),
                Intellect = CorpsValidator.EffectiveStat(_defs, g, "intellect"),
                Temperament = def.Temperament,
                Skills = new List<string>(g.LearnedSkills),
                Traits = new List<string>(g.LearnedTraits),
            };
        }

        /// <summary>군량을 군단별 하루 소비 비율로 나눈다 (내림, 나머지는 첫 군단).</summary>
        private static void SplitSupply(BattleSetup setup, List<long> daily, long total)
        {
            long sum = 0;
            foreach (var d in daily) sum += d;
            long given = 0;
            int i = 0;
            foreach (var c in setup.Corps)
            {
                long share = sum == 0 ? 0 : (long)((decimal)total * daily[i] / sum);
                c.Supply = share;
                given += share;
                i++;
            }
            if (setup.Corps.Count > 0) setup.Corps[0].Supply += total - given;
        }

        /// <summary>스텝 데이터의 팀 이름 ("player"/"enemy") → 전투 팀 id.</summary>
        private static string TeamOf(string t) => t == "player" ? PlayerTeam : t == "enemy" ? EnemyTeam : t;

        private static bool HasCommander(BattleSetup s)
        {
            foreach (var c in s.Corps) if (c.Role == CorpsRoles.Commander) return true;
            return false;
        }

        /// <summary>전투 직전 대결: 양쪽 대장끼리, 능력치 차이가 기준 이내면 확률로 (스위치·스텝 허용). 판정 난수는 전투 시드에서 따로 만든다.</summary>
        private void PlanContests(StepDef step, BattleSetup setup, SortieRequest request)
        {
            CorpsSetup mine = null, theirs = null;
            foreach (var c in setup.Corps)
            {
                if (c.Role != CorpsRoles.Commander) continue;
                if (c.Team == PlayerTeam && mine == null) mine = c;
                if (c.Team == EnemyTeam && theirs == null) theirs = c;
            }
            var rng = new SeededRandom(unchecked(request.Seed ^ 0x5DEECE66DUL));
            foreach (var (id, allowed) in new[] { ("duel", step.DuelAllowed), ("debate", step.DebateAllowed) })
            {
                if (!allowed || !_defs.Contests.TryGetValue(id, out var def) || !setup.Flags.IsEnabled(def.Flag)) continue;
                if (def.TriggerWhen == ContestDef.WhenEncounter) { setup.EncounterContests.Add(id); continue; } // 전투 중 마주칠 때 (R-89)
                if (mine == null || theirs == null) continue;
                if (!ContestTrigger.Occurs(def, mine.General.Stat(def.Stat), theirs.General.Stat(def.Stat), mine.General.Temperament, theirs.General.Temperament, rng)) continue;
                request.ContestChoices.TryGetValue(id, out var choices);
                setup.Contests.Add(new ContestSetup { ContestId = id, CorpsA = mine.Id, CorpsB = theirs.Id, ChoicesA = choices });
            }
        }
    }
}
