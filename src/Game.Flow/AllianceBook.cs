using System;
using System.Collections.Generic;
using Game.Online;
using Game.Territory;

namespace Game.Flow
{
    /// <summary>
    /// 연합 장부 (territory.md 6장, verification R-49): 만들기·가입·탈퇴. 저장소와 무관한 계산 (참고 서버·가짜 서버 공통).
    /// 한 계정은 한 연합에만, 인원 상한까지. 이름은 앞뒤 공백을 빼고 길이 범위 안, 다른 연합과 같으면 안 된다(대소문자 무시).
    /// 연합장이 나가면 다음 사람이 연합장, 마지막 사람이 나가면 연합이 없어진다.
    /// 가입 방식 (R-50): 열림이면 바로 가입, 닫힘이면 신청 → 연합장이 수락·거절. 연합장은 내보낼 수 있다. 어딘가 들어가면 다른 곳의 신청은 지운다.
    /// </summary>
    public static class AllianceBook
    {
        public static Alliance Of(Dictionary<string, Alliance> alliances, string accountId)
        {
            if (accountId == null) return null;
            foreach (var a in alliances.Values) if (a.Members.Contains(accountId)) return a;
            return null;
        }

        public static AllianceResponse Create(TerritoryDefs defs, Dictionary<string, Alliance> alliances, string accountId, string name, string newId, long now)
        {
            if (Of(alliances, accountId) != null) return Fail(AllianceReasons.AlreadyMember, alliances, accountId);
            string n = (name ?? "").Trim();
            if (n.Length < defs.AllianceNameMin || n.Length > defs.AllianceNameMax) return Fail(AllianceReasons.BadName, alliances, accountId);
            foreach (var a in alliances.Values)
                if (string.Equals(a.Name, n, StringComparison.OrdinalIgnoreCase)) return Fail(AllianceReasons.NameTaken, alliances, accountId);
            var made = new Alliance { Id = newId, Name = n, LeaderAccountId = accountId, Members = { accountId }, CreatedAtUnixSeconds = now };
            alliances[newId] = made;
            DropRequests(alliances, accountId);
            return new AllianceResponse { Ok = true, Reason = AllianceReasons.Ok, Alliance = Copy(made) };
        }

        public static AllianceResponse Join(TerritoryDefs defs, Dictionary<string, Alliance> alliances, string accountId, string allianceId)
        {
            if (Of(alliances, accountId) != null) return Fail(AllianceReasons.AlreadyMember, alliances, accountId);
            if (allianceId == null || !alliances.TryGetValue(allianceId, out var a)) return Fail(AllianceReasons.NotFound, alliances, accountId);
            if (a.Members.Count >= defs.AllianceMemberMax) return Fail(AllianceReasons.Full, alliances, accountId);
            if (!a.Open)
            {
                if (!a.Requests.Contains(accountId)) a.Requests.Add(accountId);
                return new AllianceResponse { Ok = true, Reason = AllianceReasons.Requested };
            }
            a.Members.Add(accountId);
            DropRequests(alliances, accountId);
            return new AllianceResponse { Ok = true, Reason = AllianceReasons.Ok, Alliance = Copy(a) };
        }

        /// <summary>연합장의 관리 (R-50): 열기·닫기, 신청 수락(인원 상한·이미 다른 연합이면 안 됨)·거절, 내보내기(자기 자신은 안 됨).</summary>
        public static AllianceResponse Manage(TerritoryDefs defs, Dictionary<string, Alliance> alliances, string leaderId, AllianceManageRequest req)
        {
            var a = Of(alliances, leaderId);
            if (a == null) return Fail(AllianceReasons.NotMember, alliances, leaderId);
            if (a.LeaderAccountId != leaderId) return Fail(AllianceReasons.NotLeader, alliances, leaderId);
            string who = req?.AccountId;
            switch (req?.Action)
            {
                case AllianceActions.Open: a.Open = true; break;
                case AllianceActions.Close: a.Open = false; break;
                case AllianceActions.Accept:
                    if (who == null || !a.Requests.Contains(who)) return Fail(AllianceReasons.BadRequest, alliances, leaderId);
                    if (Of(alliances, who) != null) { a.Requests.Remove(who); return Fail(AllianceReasons.AlreadyMember, alliances, leaderId); }
                    if (a.Members.Count >= defs.AllianceMemberMax) return Fail(AllianceReasons.Full, alliances, leaderId);
                    a.Members.Add(who);
                    DropRequests(alliances, who);
                    break;
                case AllianceActions.Reject:
                    if (who == null || !a.Requests.Remove(who)) return Fail(AllianceReasons.BadRequest, alliances, leaderId);
                    break;
                case AllianceActions.Kick:
                    if (who == null || who == leaderId || !a.Members.Remove(who)) return Fail(AllianceReasons.BadRequest, alliances, leaderId);
                    break;
                default: return Fail(AllianceReasons.BadRequest, alliances, leaderId);
            }
            return new AllianceResponse { Ok = true, Reason = AllianceReasons.Ok, Alliance = Copy(a) };
        }

        private static void DropRequests(Dictionary<string, Alliance> alliances, string accountId)
        {
            foreach (var a in alliances.Values) a.Requests.Remove(accountId);
        }

        public static AllianceResponse Leave(Dictionary<string, Alliance> alliances, string accountId)
        {
            var a = Of(alliances, accountId);
            if (a == null) return Fail(AllianceReasons.NotMember, alliances, accountId);
            a.Members.Remove(accountId);
            if (a.Members.Count == 0) alliances.Remove(a.Id);
            else if (a.LeaderAccountId == accountId) a.LeaderAccountId = a.Members[0];
            return new AllianceResponse { Ok = true, Reason = AllianceReasons.Ok };
        }

        public static AllianceListResponse List(TerritoryDefs defs, Dictionary<string, Alliance> alliances, string accountId)
        {
            var r = new AllianceListResponse { MineId = Of(alliances, accountId)?.Id, MemberMax = defs.AllianceMemberMax };
            foreach (var a in alliances.Values) r.Alliances.Add(Copy(a));
            r.Alliances.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
            return r;
        }

        private static AllianceResponse Fail(string reason, Dictionary<string, Alliance> alliances, string accountId)
        {
            var mine = Of(alliances, accountId);
            return new AllianceResponse { Ok = false, Reason = reason, Alliance = mine == null ? null : Copy(mine) };
        }

        private static Alliance Copy(Alliance a) => new Alliance
        {
            Id = a.Id, Name = a.Name, LeaderAccountId = a.LeaderAccountId, Members = new List<string>(a.Members), CreatedAtUnixSeconds = a.CreatedAtUnixSeconds,
            Open = a.Open, Requests = new List<string>(a.Requests ?? new List<string>()),
        };
    }
}
