using System;
using System.Collections.Generic;
using Game.Foundation.Geo;
using Game.Online;
using Game.Territory;

namespace Game.Server
{
    /// <summary>
    /// 영토 장부에서 이번 요청에 필요한 깃발 · 지역만 고르는 범위 (db.md 4장, Firestore 1단계).
    /// 저장소는 범위에 드는 것만 읽어 작은 사전으로 넘기고, 규칙이 고친 · 더한 · 지운 것만 다시 쓴다.
    /// 여러 범위는 합집합 (<see cref="Or"/>). 폴더 저장소는 메모리에서 거르고, DB 저장소는 질의로 바꾼다.
    /// </summary>
    public sealed class TerritoryScope
    {
        private readonly List<string> _keys = new List<string>();
        private readonly List<string> _owners = new List<string>();
        private readonly List<(FlagDefs fd, GeoPoint cell, long radiusM)> _near = new List<(FlagDefs, GeoPoint, long)>();
        private long? _collectedBefore;
        private bool _all;

        public IReadOnlyList<string> KeyList => _keys;
        public IReadOnlyList<string> OwnerList => _owners;
        public bool IsAll => _all;
        /// <summary>주변 범위 (칸 가운데, 반지름 m) — DB 질의는 위도 · 경도 상자로.</summary>
        public IReadOnlyList<(FlagDefs fd, GeoPoint cell, long radiusM)> NearList => _near;
        /// <summary>깃발이고 받은 시각이 이 시각 이하 (수입 가득 훑기).</summary>
        public long? CollectedBefore => _collectedBefore;

        /// <summary>전부 (관리자 · 지도 전체 보기 — DB 단계에서 지도는 화면 범위로 바꾼다).</summary>
        public static TerritoryScope All() => new TerritoryScope { _all = true };

        /// <summary>이 키들 (null은 건너뜀).</summary>
        public static TerritoryScope Keys(params string[] keys)
        {
            var s = new TerritoryScope();
            foreach (var k in keys) if (!string.IsNullOrEmpty(k)) s._keys.Add(k);
            return s;
        }

        /// <summary>이 계정의 깃발 · 지역.</summary>
        public static TerritoryScope Owner(string accountId)
        {
            var s = new TerritoryScope();
            if (accountId != null) s._owners.Add(accountId);
            return s;
        }

        /// <summary>그 점이 든 칸에서 반지름 안의 깃발.</summary>
        public static TerritoryScope Near(FlagDefs fd, GeoPoint at, long radiusM)
        {
            var s = new TerritoryScope();
            if (fd != null) s._near.Add((fd, fd.CellOf(at), radiusM));
            return s;
        }

        public static TerritoryScope IncomeDue(long collectedAtOrBeforeUnixSeconds) => new TerritoryScope { _collectedBefore = collectedAtOrBeforeUnixSeconds };

        /// <summary>합집합.</summary>
        public TerritoryScope Or(TerritoryScope other)
        {
            var s = new TerritoryScope { _all = _all || other._all };
            s._keys.AddRange(_keys); s._keys.AddRange(other._keys);
            s._owners.AddRange(_owners); s._owners.AddRange(other._owners);
            s._near.AddRange(_near); s._near.AddRange(other._near);
            s._collectedBefore = _collectedBefore == null ? other._collectedBefore : other._collectedBefore == null ? _collectedBefore : Math.Max(_collectedBefore.Value, other._collectedBefore.Value);
            return s;
        }

        public bool Matches(string key, TerritoryOwner o)
        {
            if (_all) return true;
            if (_keys.Contains(key)) return true;
            if (o == null) return false;
            if (o.OwnerAccountId != null && _owners.Contains(o.OwnerAccountId)) return true;
            bool flag = FlagDefs.TryParseKey(key, out var cell);
            if (flag && _collectedBefore != null && o.CollectedUnixSeconds <= _collectedBefore.Value) return true;
            if (flag) foreach (var (fd, c, r) in _near) if (fd.Within(c, cell, r)) return true;
            return false;
        }
    }
}
