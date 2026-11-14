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
    /// 영토 깃발 (territory-v2.md 2장, verification R-96 · R-97): 깃발 = 장수 1명 + 병력, 깃발 둘레 원(반지름 = 계정 계급)이 땅.
    /// 저장소와 무관한 계산 — 참고 서버 · 가짜 서버 · 기기가 같이 쓴다. 깃발은 영토 주인 장부(TerritoryOwner)에 "flag:" 키로 둔다.
    /// </summary>
    public static class TerritoryFlags
    {
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

            var g = state?.FindGeneral(req.GeneralId ?? "");
            if (g == null) return new PlantCheck { Reason = PlantReasons.NoGeneral, RadiusM = radius };
            if (!GeneralStatus.CanDeploy(g.Status)) return new PlantCheck { Reason = PlantReasons.GeneralBusy, RadiusM = radius };
            foreach (var o in owners.Values)
                if (FlagDefs.IsFlagKey(o.RegionId) && o.OwnerAccountId == accountId && o.GeneralId == req.GeneralId) return new PlantCheck { Reason = PlantReasons.GeneralBusy, RadiusM = radius };
            if (CorpsOf(state, req.GeneralId) == null) return new PlantCheck { Reason = PlantReasons.NotInFormation, RadiusM = radius };

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
            var check = Check(territory, defs, state, accountId, allianceOf, owners, req, now);
            if (!check.Ok) return new PlantResponse { Ok = false, Reason = check.Reason, AttackKey = check.AttackKey };
            var defense = TerritoryDefenses.SnapshotGeneral(defs, state, req.GeneralId, out _);
            if (defense == null) return new PlantResponse { Ok = false, Reason = PlantReasons.NotInFormation };
            defense.Version = 1;
            defense.RegisteredAtUnixSeconds = now;
            var flag = new TerritoryOwner
            {
                RegionId = check.FlagKey, OwnerAccountId = accountId, SinceUnixSeconds = now, CollectedUnixSeconds = now,
                GeneralId = req.GeneralId, RadiusM = check.RadiusM, Defense = defense,
            };
            owners[check.FlagKey] = flag;
            return new PlantResponse { Ok = true, Reason = PlantReasons.Ok, Flag = flag };
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
            }
            return null;
        }

        /// <summary>
        /// 공성전이 끝난 뒤 (R-97): 어느 쪽이 이기든 그 자리는 보호 시간 동안 다시 공격받지 않는다.
        /// 공격자가 이기면 이전 주인에게 쌓인 수입을 우편으로 보내고, 그 자리에 공격자의 깃발 (장수 = 공격 군단 첫 장수, 수비 = 그 장수 군단 스냅샷, 반지름 = 공격자 계급).
        /// </summary>
        public static void AfterSiege(TerritoryDefs territory, DefinitionSet defs, Dictionary<string, TerritoryOwner> owners, string flagKey, bool attackerWon,
            string attackerId, CampaignState attackerState, string attackerGeneralId, long at, Action<string, Mail> sendMail)
        {
            if (territory?.Flags == null || !owners.TryGetValue(flagKey ?? "", out var old)) return;
            long until = at + territory.Flags.ProtectSeconds;
            if (!attackerWon) { old.ProtectedUntilUnixSeconds = until; return; }
            TerritoryLedger.Capture(territory, owners, flagKey, attackerId, at, sendMail);
            var flag = owners[flagKey];
            flag.GeneralId = attackerGeneralId;
            flag.RadiusM = RadiusOf(defs, attackerState);
            flag.ProtectedUntilUnixSeconds = until;
            var d = TerritoryDefenses.SnapshotGeneral(defs, attackerState, attackerGeneralId, out _);
            if (d != null) { d.Version = 1; d.RegisteredAtUnixSeconds = at; }
            flag.Defense = d;
        }
    }
}
