using System;
using System.Collections.Generic;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Foundation.Geo;
using Game.Online;
using Game.Territory;

namespace Game.Flow
{
    /// <summary>
    /// 영토 깃발 (territory-v2.md 2장, verification R-96 · R-97 · R-109): 깃발 = 장수 1명 이상 + 그 군단 병력, 깃발 둘레 원(반지름 = 계정 계급)이 땅. 장수는 회수할 수 있고 모두 회수하면 깃발이 사라진다.
    /// 저장소와 무관한 계산 — 참고 서버 · 가짜 서버 · 기기가 같이 쓴다. 깃발은 영토 주인 장부(TerritoryOwner)에 "flag:" 키로 둔다.
    /// </summary>
    public static class TerritoryFlags
    {
        /// <summary>UTC 일 번호 (하루 잃는 상한, R-168).</summary>
        public static long DayOf(long unixSeconds) => unixSeconds / 86400;

        /// <summary>주인이 깃발을 하나 잃음: 그 주인의 남은 깃발마다 오늘 잃은 수 + 1 (날이 바뀌었으면 1부터).</summary>
        public static void NoteLoss(IDictionary<string, TerritoryOwner> owners, string accountId, long at)
        {
            if (accountId == null) return;
            long day = DayOf(at);
            int count = 1;
            foreach (var o in owners.Values) if (o.OwnerAccountId == accountId && FlagDefs.IsFlagKey(o.RegionId) && o.LossDay == day) count = Math.Max(count, o.LossCount + 1);
            foreach (var o in owners.Values) if (o.OwnerAccountId == accountId && FlagDefs.IsFlagKey(o.RegionId)) { o.LossDay = day; o.LossCount = count; }
        }

        /// <summary>새 깃발에 그 주인의 오늘 잃은 수를 옮긴다 (새로 꽂아 상한을 피하지 않게).</summary>
        private static void CopyLosses(IDictionary<string, TerritoryOwner> owners, string accountId, TerritoryOwner flag)
        {
            foreach (var o in owners.Values)
                if (o != flag && o.OwnerAccountId == accountId && FlagDefs.IsFlagKey(o.RegionId) && (o.LossDay > flag.LossDay || (o.LossDay == flag.LossDay && o.LossCount > flag.LossCount)))
                { flag.LossDay = o.LossDay; flag.LossCount = o.LossCount; }
        }

        /// <summary>남은 장수의 자리만 남긴 수비판 (회수 뒤).</summary>
        private static DefenseBoard Trim(DefenseBoard b, List<string> generals)
        {
            if (b == null) return null;
            var n = new DefenseBoard { Traps = new List<BoardTrap>(b.Traps ?? new List<BoardTrap>()) };
            foreach (var p in b.Places ?? new List<Game.Battle.Model.Crowd.CrowdBoardPlace>()) if (generals.Contains(p.GeneralId)) n.Places.Add(p);
            return n;
        }

        /// <summary>
        /// 수비판 저장 (R-167): 내 깃발이고 공격받는 중이 아니어야. 자리는 그 깃발 장수만 · 3 × 3 칸 · 칸 겹치지 않음 · 아는 자세,
        /// 함정은 boardTrapsMax개까지 · pit · slow, 자리는 0 ~ 200칸. 되면 수비 판 + 1 (진행 중이던 공격은 다시 준비).
        /// </summary>
        public static PlantResponse SetBoard(TerritoryDefs territory, string accountId, Dictionary<string, TerritoryOwner> owners, FlagBoardRequest req, long now)
        {
            var fd = territory?.Flags;
            if (fd == null) return new PlantResponse { Ok = false, Reason = PlantReasons.NoFlags };
            if (req?.FlagKey == null || !owners.TryGetValue(req.FlagKey, out var flag) || !FlagDefs.IsFlagKey(req.FlagKey)) return new PlantResponse { Ok = false, Reason = PlantReasons.NoFlag };
            if (flag.OwnerAccountId != accountId) return new PlantResponse { Ok = false, Reason = PlantReasons.NotMine };
            if (flag.SiegeAttackerId != null) return new PlantResponse { Ok = false, Reason = PlantReasons.UnderSiege };
            var b = req.Board ?? new DefenseBoard();
            var on = GeneralsOf(flag);
            var cells = new HashSet<int>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in b.Places ?? new List<Game.Battle.Model.Crowd.CrowdBoardPlace>())
            {
                if (p == null || !on.Contains(p.GeneralId) || !seen.Add(p.GeneralId) || p.Row < 0 || p.Row > 2 || p.Col < 0 || p.Col > 2
                    || !Game.Battle.Model.Crowd.CrowdStances.IsKnown(p.Stance) || !cells.Add(p.Row * 3 + p.Col))
                    return new PlantResponse { Ok = false, Reason = PlantReasons.BadBoard };
            }
            var traps = b.Traps ?? new List<BoardTrap>();
            if (traps.Count > fd.BoardTrapsMax) return new PlantResponse { Ok = false, Reason = PlantReasons.BadBoard };
            var lo = Game.Foundation.Math.Fix.Zero; var hi = Game.Foundation.Math.Fix.FromInt(200);
            foreach (var t in traps)
                if (t == null || (t.Kind != Game.Battle.Model.Crowd.CrowdTrapKinds.Pit && t.Kind != Game.Battle.Model.Crowd.CrowdTrapKinds.Slow) || t.X < lo || t.Y < lo || t.X > hi || t.Y > hi)
                    return new PlantResponse { Ok = false, Reason = PlantReasons.BadBoard };
            if (flag.Defense == null) return new PlantResponse { Ok = false, Reason = PlantReasons.NoFlag };
            flag.Defense.Board = new DefenseBoard { Places = new List<Game.Battle.Model.Crowd.CrowdBoardPlace>(b.Places ?? new List<Game.Battle.Model.Crowd.CrowdBoardPlace>()), Traps = new List<BoardTrap>(traps) };
            flag.Defense.Version++;
            flag.Defense.RegisteredAtUnixSeconds = now;
            return new PlantResponse { Ok = true, Reason = PlantReasons.Ok, Flag = flag };
        }

        /// <summary>
        /// 개발 · 마스터 적 깃발 (R-169): center 둘레 세 곳에 시험용 주인(bot-1 ~ 3)의 깃발. 수비 = 부른 계정의 첫 군단 사본,
        /// 맵 = 평원 · 숲길 · 고개 (있으면), 둘째는 매복 · 함정 수비판 (깃발전 시험). 이미 있는 칸은 건너뜀. 놓은 깃발들을 돌려준다.
        /// </summary>
        public static List<TerritoryOwner> DevSeed(TerritoryDefs territory, DefinitionSet defs, CampaignState state, IDictionary<string, TerritoryOwner> owners, GeoPoint center, long now)
        {
            var made = new List<TerritoryOwner>();
            var fd = territory?.Flags;
            var first = state?.CompositionsFor(null);
            if (fd == null || first == null || first.Count == 0) return made;
            string gid = first[0].GeneralId;
            var spots = new (long dLat, long dLon, string who, long radius, string map, bool board)[]
            {
                (6000, 2000, "bot-1", 2000, "crowd_plain", false), (-9000, 7000, "bot-2", 3000, "crowd_forest", true), (15000, -12000, "bot-3", 1000, "crowd_pass", false),
            };
            foreach (var s in spots)
            {
                var cell = fd.CellOf(new GeoPoint(center.LatE6 + s.dLat, center.LonE6 + s.dLon));
                string key = FlagDefs.KeyOf(cell);
                if (owners.ContainsKey(key)) continue;
                var d = TerritoryDefenses.SnapshotGeneral(defs, state, gid, out _);
                if (d == null) continue;
                d.Version = 1; d.RegisteredAtUnixSeconds = now; d.OwnerAccountId = s.who;
                if (s.board)
                    d.Board = new DefenseBoard
                    {
                        Places = { new Game.Battle.Model.Crowd.CrowdBoardPlace { GeneralId = gid, Row = 1, Col = 0, Stance = Game.Battle.Model.Crowd.CrowdStances.Guard, Ambush = true } },
                        Traps = { new BoardTrap { Kind = Game.Battle.Model.Crowd.CrowdTrapKinds.Pit, X = Game.Foundation.Math.Fix.FromInt(30), Y = Game.Foundation.Math.Fix.FromInt(62) },
                                  new BoardTrap { Kind = Game.Battle.Model.Crowd.CrowdTrapKinds.Slow, X = Game.Foundation.Math.Fix.FromInt(22), Y = Game.Foundation.Math.Fix.FromInt(66) } },
                    };
                var o = new TerritoryOwner
                {
                    RegionId = key, OwnerAccountId = s.who, GeneralId = gid, GeneralIds = new List<string> { gid }, RadiusM = s.radius, Defense = d,
                    SinceUnixSeconds = now, CollectedUnixSeconds = now, MapId = fd.IsMap(s.map) ? s.map : fd.DefaultMap, OwnerRank = state.LordLevel,
                };
                owners[key] = o;
                made.Add(o);
            }
            return made;
        }

        /// <summary>깃발에 나간 장수들 (예전 깃발은 GeneralId 하나).</summary>
        public static List<string> GeneralsOf(TerritoryOwner o)
        {
            if (o?.GeneralIds != null && o.GeneralIds.Count > 0) return new List<string>(o.GeneralIds);
            return o?.GeneralId != null ? new List<string> { o.GeneralId } : new List<string>();
        }

        /// <summary>그 계정의 깃발에 나가 있는 장수 모두 (용병 등록 검사 R-110 등).</summary>
        public static HashSet<string> GeneralsOnFlags(IReadOnlyDictionary<string, TerritoryOwner> owners, string accountId)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (owners == null || accountId == null) return set;
            foreach (var o in owners.Values)
                if (o != null && FlagDefs.IsFlagKey(o.RegionId) && o.OwnerAccountId == accountId)
                    foreach (var g in GeneralsOf(o)) set.Add(g);
            return set;
        }

        /// <summary>꽂기 요청의 장수들 (겹친 것 빼고 차례대로, 없으면 GeneralId 하나).</summary>
        public static List<string> RequestGenerals(PlantRequest req)
        {
            var list = new List<string>();
            if (req?.GeneralIds != null) foreach (var id in req.GeneralIds) if (!string.IsNullOrEmpty(id) && !list.Contains(id)) list.Add(id);
            if (list.Count == 0 && !string.IsNullOrEmpty(req?.GeneralId)) list.Add(req.GeneralId);
            return list;
        }

        /// <summary>그 계정 계급의 깃발 원 반지름 (m).</summary>
        public static long RadiusOf(DefinitionSet defs, CampaignState state) => defs.Campaign.RankFor(state?.LordLevel ?? 1).FlagRadiusM;

        /// <summary>꽂기 검사 결과.</summary>
        public sealed class PlantCheck
        {
            public string Reason { get; set; }
            /// <summary>꽂을 칸의 깃발 키 (Ok일 때).</summary>
            public string FlagKey { get; set; }
            /// <summary>siege · protected일 때 공격 대상 깃발 키.</summary>
            public string AttackKey { get; set; }
            public long RadiusM { get; set; }
            public bool Ok => Reason == PlantReasons.Ok;
        }

        /// <summary>
        /// 꽂기 검사 (R-96). 순서: 위치 → 범위(내 위치에서 반지름 안) → 장수(있음 · 출전 가능 · 다른 깃발에 없음 · 기본 편성에 군단이 있음)
        /// → 같은 칸(내 · 연합 깃발이 이미 있으면 occupied) → 남의 원 안(우리 연합이 아닌 다른 주인 원 안이면 가장 가까운 깃발과 공성전, 보호 중이면 protected).
        /// 내 · 우리 연합 원과는 겹쳐도 된다.
        /// </summary>
        /// <param name="allianceOf">계정 → 연합 id (없으면 null).</param>
        public static PlantCheck Check(TerritoryDefs territory, DefinitionSet defs, CampaignState state, string accountId, Func<string, string> allianceOf,
            IReadOnlyDictionary<string, TerritoryOwner> owners, PlantRequest req, long now)
        {
            var fd = territory?.Flags;
            if (fd == null) return new PlantCheck { Reason = PlantReasons.NoFlags };
            long radius = RadiusOf(defs, state);
            if (req == null || !GeoPoint.TryParse(req.Location, out var here)) return new PlantCheck { Reason = PlantReasons.BadLocation, RadiusM = radius };
            var target = here;
            if (!string.IsNullOrEmpty(req.Target) && !GeoPoint.TryParse(req.Target, out target)) return new PlantCheck { Reason = PlantReasons.BadLocation, RadiusM = radius };
            var cell = fd.CellOf(target);
            if (!fd.Within(here, cell, radius)) return new PlantCheck { Reason = PlantReasons.OutOfRange, RadiusM = radius };

            var gids = RequestGenerals(req);
            string why = CheckGenerals(state, accountId, owners, gids);
            if (why != null) return new PlantCheck { Reason = why, RadiusM = radius };

            string key = FlagDefs.KeyOf(cell);
            string myAlliance = allianceOf?.Invoke(accountId);
            bool Friendly(string owner) => owner == accountId || (myAlliance != null && allianceOf?.Invoke(owner) == myAlliance);
            if (owners.TryGetValue(key, out var same) && Friendly(same.OwnerAccountId)) return new PlantCheck { Reason = PlantReasons.Occupied, RadiusM = radius };

            // 남의 원 안 → 가장 가까운 깃발과 공성전 (같은 거리면 키 순서)
            TerritoryOwner nearest = null;
            long best = long.MaxValue;
            foreach (var o in owners.Values)
            {
                if (!FlagDefs.TryParseKey(o.RegionId, out var oc) || Friendly(o.OwnerAccountId)) continue;
                long d = fd.DistanceSquaredM(cell, oc);
                if (d > o.RadiusM * o.RadiusM) continue;
                if (d < best || (d == best && string.CompareOrdinal(o.RegionId, nearest.RegionId) < 0)) { best = d; nearest = o; }
            }
            if (nearest != null)
                return new PlantCheck { Reason = nearest.ProtectedUntilUnixSeconds > now ? PlantReasons.Protected : PlantReasons.Siege, AttackKey = nearest.RegionId, RadiusM = radius };
            return new PlantCheck { Reason = PlantReasons.Ok, FlagKey = key, RadiusM = radius };
        }

        /// <summary>장수 1명 이상 (R-109): 모두 있고 · 출전 가능 · 다른 내 깃발에 없고 · 기본 편성에 군단이 있어야. 되면 null, 아니면 까닭.</summary>
        private static string CheckGenerals(CampaignState state, string accountId, IReadOnlyDictionary<string, TerritoryOwner> owners, List<string> gids)
        {
            if (gids.Count == 0) return PlantReasons.NoGeneral;
            foreach (var gid in gids)
            {
                var g = state?.FindGeneral(gid);
                if (g == null) return PlantReasons.NoGeneral;
                if (!GeneralStatus.CanDeploy(g.Status)) return PlantReasons.GeneralBusy;
                foreach (var o in owners.Values)
                    if (FlagDefs.IsFlagKey(o.RegionId) && o.OwnerAccountId == accountId && GeneralsOf(o).Contains(gid)) return PlantReasons.GeneralBusy;
                if (CorpsOf(state, gid) == null) return PlantReasons.NotInFormation;
            }
            return null;
        }

        /// <summary>
        /// 추가 파견 (R-115, 2026-11-16 사용자): 내 깃발에 장수를 더 보낸다. 장수 검사는 꽂기와 같고(R-109), 위치는 보지 않는다 (회수처럼).
        /// 공격받는 중(공성전 기록이 있음)이면 거절. 수비 = 원래 장수 + 새 장수 군단 스냅샷 (판 + 1). 추가 파견 뒤 회수 = 장수 바꾸기.
        /// </summary>
        public static PlantResponse Reinforce(TerritoryDefs territory, DefinitionSet defs, CampaignState state, string accountId,
            Dictionary<string, TerritoryOwner> owners, PlantRequest req, long now)
        {
            if (territory?.Flags == null) return new PlantResponse { Ok = false, Reason = PlantReasons.NoFlags };
            if (req?.FlagKey == null || !FlagDefs.IsFlagKey(req.FlagKey) || !owners.TryGetValue(req.FlagKey, out var flag)) return new PlantResponse { Ok = false, Reason = PlantReasons.NoFlag };
            if (flag.OwnerAccountId != accountId) return new PlantResponse { Ok = false, Reason = PlantReasons.NotMine };
            if (flag.SiegeAttackerId != null) return new PlantResponse { Ok = false, Reason = PlantReasons.UnderSiege };
            var add = RequestGenerals(req);
            string why = CheckGenerals(state, accountId, owners, add);
            if (why != null) return new PlantResponse { Ok = false, Reason = why };
            var all = GeneralsOf(flag);
            all.AddRange(add);
            var d = TerritoryDefenses.SnapshotGenerals(defs, state, all, out _);
            if (d == null) return new PlantResponse { Ok = false, Reason = PlantReasons.NotInFormation };
            d.Version = (flag.Defense?.Version ?? 0) + 1;
            d.RegisteredAtUnixSeconds = now;
            d.OwnerAccountId = accountId;
            d.Board = flag.Defense?.Board; // 수비판은 그대로 (R-167)
            flag.Defense = d;
            flag.GeneralIds = all;
            flag.GeneralId = all[0];
            return new PlantResponse { Ok = true, Reason = PlantReasons.Ok, Flag = flag };
        }

        /// <summary>공성전에서 깃발에 남을 공격 장수: 제출 편성의 첫 군단 장수, 없으면 기본 편성의 첫 군단.</summary>
        public static string AttackerGeneral(BattleSubmission s, CampaignState state)
        {
            if (s?.Compositions != null && s.Compositions.Count > 0 && !string.IsNullOrEmpty(s.Compositions[0].GeneralId)) return s.Compositions[0].GeneralId;
            var comps = state?.CompositionsFor(null);
            return comps != null && comps.Count > 0 ? comps[0].GeneralId : null;
        }

        /// <summary>기본 편성에서 그 장수가 이끄는 군단 (없으면 null).</summary>
        public static Game.Campaign.Roster.CorpsComposition CorpsOf(CampaignState state, string generalId)
        {
            foreach (var c in state?.CompositionsFor(null) ?? new List<Game.Campaign.Roster.CorpsComposition>())
                if (c.GeneralId == generalId) return c;
            return null;
        }

        /// <summary>
        /// 꽂기 (R-96): 검사를 통과하면 그 칸에 깃발 (수비 = 그 장수 군단의 스냅샷, R-48 방식). 되면 깃발, 아니면 null과 까닭.
        /// </summary>
        public static PlantResponse Plant(TerritoryDefs territory, DefinitionSet defs, CampaignState state, string accountId, Func<string, string> allianceOf,
            Dictionary<string, TerritoryOwner> owners, PlantRequest req, long now)
        {
            if (!string.IsNullOrEmpty(req?.FlagKey)) return Reinforce(territory, defs, state, accountId, owners, req, now); // 추가 파견 (R-115)
            var check = Check(territory, defs, state, accountId, allianceOf, owners, req, now);
            if (!check.Ok) return new PlantResponse { Ok = false, Reason = check.Reason, AttackKey = check.AttackKey };
            var gids = RequestGenerals(req);
            var defense = TerritoryDefenses.SnapshotGenerals(defs, state, gids, out _);
            if (defense == null) return new PlantResponse { Ok = false, Reason = PlantReasons.NotInFormation };
            defense.Version = 1;
            defense.RegisteredAtUnixSeconds = now;
            defense.OwnerAccountId = accountId;
            var flag = new TerritoryOwner
            {
                RegionId = check.FlagKey, OwnerAccountId = accountId, SinceUnixSeconds = now, CollectedUnixSeconds = now,
                GeneralId = gids[0], GeneralIds = gids, RadiusM = check.RadiusM, Defense = defense,
                MapId = territory.Flags.IsMap(req.MapId) ? req.MapId : territory.Flags.DefaultMap, // 깃발전 전장 (R-168)
                OwnerRank = state?.LordLevel ?? 0,
            };
            CopyLosses(owners, accountId, flag);
            owners[check.FlagKey] = flag;
            return new PlantResponse { Ok = true, Reason = PlantReasons.Ok, Flag = flag };
        }

        /// <summary>
        /// 장수 회수 (R-109): 내 깃발에서 고른 장수들을 돌려받는다 (null · 빈 목록 = 모두). 남은 장수가 있으면 수비 = 남은 장수 군단 스냅샷(판 + 1),
        /// 모두 회수하면 깃발을 거둔다 — 쌓인 수입을 우편으로 보내고 장부에서 지운다.
        /// </summary>
        public static RecallResponse Recall(TerritoryDefs territory, DefinitionSet defs, CampaignState state, string accountId, Dictionary<string, TerritoryOwner> owners,
            RecallRequest req, long now, Action<string, Mail> sendMail)
        {
            if (req?.FlagKey == null || !FlagDefs.IsFlagKey(req.FlagKey) || !owners.TryGetValue(req.FlagKey, out var flag)) return new RecallResponse { Ok = false, Reason = RecallReasons.NoFlag };
            if (flag.OwnerAccountId != accountId) return new RecallResponse { Ok = false, Reason = RecallReasons.NotMine };
            var on = GeneralsOf(flag);
            var take = req.GeneralIds == null || req.GeneralIds.Count == 0 ? new List<string>(on) : new List<string>(req.GeneralIds);
            foreach (var id in take) if (!on.Contains(id)) return new RecallResponse { Ok = false, Reason = RecallReasons.NotOnFlag };
            var left = on.FindAll(id => !take.Contains(id));
            if (left.Count == 0)
                return new RecallResponse { Ok = true, Reason = RecallReasons.Ok, Flag = null, Mail = TerritoryLedger.Release(territory, owners, req.FlagKey, now, sendMail) };
            var d = TerritoryDefenses.SnapshotGenerals(defs, state, left, out _);
            if (d != null) { d.Version = (flag.Defense?.Version ?? 0) + 1; d.RegisteredAtUnixSeconds = now; d.OwnerAccountId = accountId; d.Board = Trim(flag.Defense?.Board, left); flag.Defense = d; }
            flag.GeneralIds = left;
            flag.GeneralId = left[0];
            return new RecallResponse { Ok = true, Reason = RecallReasons.Ok, Flag = flag };
        }

        /// <summary>
        /// 공성전 출진 검사 (기기 · 서버 같은 코드, R-97): 깃발이 있고, 내 깃발이 아니고, 보호 중이 아니며, 내 위치에서 반지름 안. 되면 null, 아니면 까닭.
        /// 장부를 모르는 기기(출진 준비)는 owners = null — 위치 · 범위만 본다.
        /// </summary>
        public static string CheckSiege(TerritoryDefs territory, DefinitionSet defs, CampaignState state, string accountId, IReadOnlyDictionary<string, TerritoryOwner> owners,
            string flagKey, string location, long now)
        {
            var fd = territory?.Flags;
            if (fd == null || !FlagDefs.TryParseKey(flagKey, out var cell)) return $"없는 깃발 '{flagKey}'.";
            if (!GeoPoint.TryParse(location, out var here)) return "위치가 없습니다.";
            if (!fd.Within(here, cell, RadiusOf(defs, state))) return "지금 위치에서는 그 깃발을 칠 수 없습니다 (계급 반지름 밖).";
            if (owners != null)
            {
                if (!owners.TryGetValue(flagKey, out var o)) return $"없는 깃발 '{flagKey}'.";
                if (o.OwnerAccountId == accountId) return "내 깃발은 칠 수 없습니다.";
                if (o.ProtectedUntilUnixSeconds > now) return "공성전 뒤 보호 중인 깃발입니다.";
                // 오프라인 수비 보호 (R-168): 주인이 오늘 깃발을 dailyLossCap개 잃었으면 그날은 그만 · 계급 차가 rankGapMax를 넘으면 못 침
                if (fd.DailyLossCap > 0 && o.LossDay == DayOf(now) && o.LossCount >= fd.DailyLossCap) return "이 주인은 오늘 깃발을 많이 잃어 내일까지 보호 중입니다.";
                if (fd.RankGapMax > 0 && o.OwnerRank > 0 && (state?.LordLevel ?? 1) - o.OwnerRank > fd.RankGapMax) return "계급 차이가 너무 커서 칠 수 없습니다.";
            }
            return null;
        }

        /// <summary>
        /// 공성전 준비 시작 (territory-v2.md 2.3, R-113): <see cref="CheckSiege"/>를 지나면 서버 시각으로 준비 시작을 기록하고 수비자에게 알림 우편.
        /// 다른 공격자가 준비 · 공격 시간 안이면 busy. 같은 공격자가 다시 부르면 기록 그대로 (알림 다시 안 보냄).
        /// </summary>
        public static SiegeResponse StartSiege(TerritoryDefs territory, DefinitionSet defs, CampaignState state, string accountId, Dictionary<string, TerritoryOwner> owners,
            SiegeRequest req, long now, Action<string, Mail> sendMail)
        {
            string err = CheckSiege(territory, defs, state, accountId, owners, req?.FlagKey, req?.Location, now);
            if (err != null) return new SiegeResponse { Reason = SiegeReasons.BadTarget, Message = err };
            var fd = territory.Flags;
            var o = owners[req.FlagKey];
            bool active = o.SiegeAttackerId != null && now <= o.SiegeStartedAtUnixSeconds + fd.SiegePrepSeconds + fd.SiegeWindowSeconds;
            if (active && o.SiegeAttackerId != accountId) return new SiegeResponse { Reason = SiegeReasons.Busy };
            if (!active)
            {
                o.SiegeAttackerId = accountId;
                o.SiegeStartedAtUnixSeconds = now;
                sendMail?.Invoke(o.OwnerAccountId, new Mail { Kind = MailKinds.SiegeAlert, RegionId = o.RegionId, FromAccountId = accountId, CreatedAtUnixSeconds = now });
            }
            long starts = o.SiegeStartedAtUnixSeconds + fd.SiegePrepSeconds;
            return new SiegeResponse { Ok = true, Reason = SiegeReasons.Ok, StartsAtUnixSeconds = starts, ExpiresAtUnixSeconds = starts + fd.SiegeWindowSeconds };
        }

        /// <summary>
        /// 공성전 제출 시 준비 검사 (서버, R-113): 준비가 없으면(siegePrepSeconds 0) 통과. 아니면 내가 준비했고, 준비 시간이 지났고, 낼 시간 안이어야 한다. 되면 null, 아니면 까닭.
        /// </summary>
        public static string CheckSiegeReady(TerritoryDefs territory, IReadOnlyDictionary<string, TerritoryOwner> owners, string flagKey, string accountId, long now)
        {
            var fd = territory?.Flags;
            if (fd == null || fd.SiegePrepSeconds <= 0) return null;
            if (owners == null || !owners.TryGetValue(flagKey ?? "", out var o) || o.SiegeAttackerId != accountId) return "공성전 준비를 먼저 해야 합니다.";
            if (now < o.SiegeStartedAtUnixSeconds + fd.SiegePrepSeconds) return "공성전 준비 중입니다.";
            if (now > o.SiegeStartedAtUnixSeconds + fd.SiegePrepSeconds + fd.SiegeWindowSeconds) return "공성전 준비 시간이 지났습니다. 다시 준비하세요.";
            return null;
        }

        /// <summary>준비 중 공격 취소 (R-113): 내가 준비 중이고 아직 준비 시간 안이면 기록을 지우고 true. 수비자의 알림은 takeAlert로 지운다.</summary>
        public static bool CancelSiege(TerritoryDefs territory, Dictionary<string, TerritoryOwner> owners, string flagKey, string accountId, long now, Action<string, string> takeAlert)
        {
            var fd = territory?.Flags;
            if (fd == null || owners == null || !owners.TryGetValue(flagKey ?? "", out var o) || o.SiegeAttackerId == null || o.SiegeAttackerId != accountId) return false;
            if (now >= o.SiegeStartedAtUnixSeconds + fd.SiegePrepSeconds && fd.SiegePrepSeconds > 0) return false; // 준비가 끝나면 취소 없음 (공성전 진행)
            EndSiege(o, takeAlert);
            return true;
        }

        /// <summary>공성전 기록을 지우고 수비자 알림을 거둔다.</summary>
        public static void EndSiege(TerritoryOwner o, Action<string, string> takeAlert)
        {
            if (o == null || o.SiegeAttackerId == null) return;
            takeAlert?.Invoke(o.OwnerAccountId, o.RegionId);
            o.SiegeAttackerId = null;
            o.SiegeStartedAtUnixSeconds = 0;
        }

        /// <summary>
        /// 공성전이 끝난 뒤 (R-97): 어느 쪽이 이기든 그 자리는 보호 시간 동안 다시 공격받지 않는다.
        /// 공격자가 이기면 이전 주인에게 쌓인 수입을 우편으로 보내고, 그 자리에 공격자의 깃발 (장수 = 공격 군단 첫 장수, 수비 = 그 장수 군단 스냅샷, 반지름 = 공격자 계급).
        /// </summary>
        public static void AfterSiege(TerritoryDefs territory, DefinitionSet defs, Dictionary<string, TerritoryOwner> owners, string flagKey, bool attackerWon,
            string attackerId, CampaignState attackerState, string attackerGeneralId, long at, Action<string, Mail> sendMail, Action<string, string> takeAlert = null, bool remove = false)
        {
            if (territory?.Flags == null || !owners.TryGetValue(flagKey ?? "", out var old)) return;
            EndSiege(old, takeAlert); // 준비 기록 · 수비자 알림 정리 (R-113)
            long until = at + territory.Flags.ProtectSeconds;
            if (!attackerWon) { old.ProtectedUntilUnixSeconds = until; return; }
            string loser = old.OwnerAccountId;
            string mapId = old.MapId;
            if (remove)
            {
                // 깃발 없애기 (R-163): 쌓인 수입은 이전 주인에게, 깃발은 장부에서 사라짐
                TerritoryLedger.Release(territory, owners, flagKey, at, sendMail);
                NoteLoss(owners, loser, at);
                return;
            }
            TerritoryLedger.Capture(territory, owners, flagKey, attackerId, at, sendMail);
            var flag = owners[flagKey];
            flag.MapId = mapId;
            flag.OwnerRank = attackerState?.LordLevel ?? 0;
            NoteLoss(owners, loser, at);
            CopyLosses(owners, attackerId, flag);
            flag.GeneralId = attackerGeneralId;
            flag.GeneralIds = attackerGeneralId == null ? null : new List<string> { attackerGeneralId };
            flag.RadiusM = RadiusOf(defs, attackerState);
            flag.ProtectedUntilUnixSeconds = until;
            var d = TerritoryDefenses.SnapshotGeneral(defs, attackerState, attackerGeneralId, out _);
            if (d != null) { d.Version = 1; d.RegisteredAtUnixSeconds = at; d.OwnerAccountId = attackerId; }
            flag.Defense = d;
        }
    }
}
