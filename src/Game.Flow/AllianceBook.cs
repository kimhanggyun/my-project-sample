using System;
using System.Collections.Generic;
using Game.Online;
using Game.Territory;

namespace Game.Flow
{
    /// <summary>
    /// 연합 장부 (territory.md 6장, verification R-49): 만들기·가입·탈퇴. 저장소와 무관한 계산 (참고 서버·가짜 서버 공통).
    /// 한 계정은 한 연합에만, 인원 상한까지. 이름은 앞뒤 공백을 빼고 길이 범위 안, 다른 연합과 같으면 안 된다(대소문자 무시).
    /// 연합장이 나가면 다음 사람이 연합장, 마지막 사람이 나가면 연합이 없어진다. 지금은 신청 없이 바로 가입 (승인은 다음 단계).
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
            return new AllianceResponse { Ok = true, Reason = AllianceReasons.Ok, Alliance = Copy(made) };
        }

        public static AllianceResponse Join(TerritoryDefs defs, Dictionary<string, Alliance> alliances, string accountId, string allianceId)
        {
            if (Of(alliances, accountId) != null) return Fail(AllianceReasons.AlreadyMember, alliances, accountId);
            if (allianceId == null || !alliances.TryGetValue(allianceId, out var a)) return Fail(AllianceReasons.NotFound, alliances, accountId);
            if (a.Members.Count >= defs.AllianceMemberMax) return Fail(AllianceReasons.Full, alliances, accountId);
            a.Members.Add(accountId);
            return new AllianceResponse { Ok = true, Reason = AllianceReasons.Ok, Alliance = Copy(a) };
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

        private static Alliance Copy(Alliance a) => new Alliance { Id = a.Id, Name = a.Name, LeaderAccountId = a.LeaderAccountId, Members = new List<string>(a.Members), CreatedAtUnixSeconds = a.CreatedAtUnixSeconds };
    }
}
