using System.Collections.Generic;
using Game.Campaign.Troops;
using Game.Data.Defs;

namespace Game.Campaign.Roster
{
    /// <summary>
    /// 부대 편성 편집 (formation.md 4장): 저장된 편성의 사본을 고치고, 검사(R-5)를 통과할 때만 저장한다.
    /// 상황(전장 유형)을 주면 그 상황별 편성을 고친다. 아직 없으면 기본 편성을 복사해 시작하고, 저장하면 생긴다 (R-35).
    /// 화면은 이 객체만 부른다 (규칙은 <see cref="CorpsValidator"/>가 판정).
    /// </summary>
    public sealed class FormationEditor
    {
        /// <summary>군단 역할 (battle.md 4.3.2: 전군 진군 때 선봉은 앞장, 측면은 돌아들고, 후위는 뒤에서 따른다).</summary>
        public static readonly IReadOnlyList<string> Roles = new[] { "vanguard", "flank", "rear" };

        private readonly DefinitionSet _defs;
        private readonly CampaignState _state;

        public List<CorpsComposition> Draft { get; } = new List<CorpsComposition>();
        /// <summary>고치는 상황 id (null = 기본 편성).</summary>
        public string Situation { get; }
        /// <summary>그 상황의 편성이 저장되어 있는지 (기본 편성은 항상 true).</summary>
        public bool HasPreset => Situation == null || (_state.CompositionPresets.TryGetValue(Situation, out var p) && p != null && p.Count > 0);

        public FormationEditor(DefinitionSet defs, CampaignState state, string situation = null)
        {
            _defs = defs;
            _state = state;
            Situation = situation;
            foreach (var c in Saved()) Draft.Add(Copy(c));
        }

        /// <summary>지금 저장된 편성 (상황별 편성이 없으면 기본 편성).</summary>
        private List<CorpsComposition> Saved() => _state.CompositionsFor(Situation);

        /// <summary>계급의 출진 군단 수 (army-v2.md 2장).</summary>
        public int MaxCorps => Army.Rank(_defs, _state).Corps;
        /// <summary>계급의 군단당 병종 칸 수.</summary>
        public int MaxPicks => Army.Rank(_defs, _state).Slots;

        /// <summary>지금 사본의 문제 (없으면 저장 가능).</summary>
        public List<string> Errors() => CorpsValidator.Validate(_defs, _state, Draft);

        /// <summary>문제가 없으면 저장된 편성을 사본으로 바꾸고 true.</summary>
        public bool Apply()
        {
            if (Errors().Count > 0 || Draft.Count == 0) return false;
            var saved = new List<CorpsComposition>();
            foreach (var c in Draft) saved.Add(Copy(c));
            if (Situation == null) _state.Compositions = saved;
            else _state.CompositionPresets[Situation] = saved;
            return true;
        }

        /// <summary>상황별 편성을 지운다 (그 상황은 다시 기본 편성을 쓴다). 기본 편성은 지울 수 없다.</summary>
        public bool RemovePreset()
        {
            if (Situation == null) return false;
            return _state.CompositionPresets.Remove(Situation);
        }

        /// <summary>저장할 것이 있는지: 고쳤거나, 상황별 편성이 아직 없는데 새로 만들려는 경우.</summary>
        public bool Changed()
        {
            if (!HasPreset) return true;
            var saved = Saved();
            if (Draft.Count != saved.Count) return true;
            for (int i = 0; i < Draft.Count; i++) if (!Same(Draft[i], saved[i])) return true;
            return false;
        }

        // ---- 군단 ----

        /// <summary>군단 추가: 아직 편성되지 않은 출전 가능 장수 중 첫 번째, 진형은 배운 첫 진형. 자리가 없거나 장수가 없으면 null.</summary>
        public CorpsComposition AddCorps()
        {
            if (Draft.Count >= MaxCorps) return null;
            var free = FreeGenerals();
            if (free.Count == 0 || _state.LearnedFormations.Count == 0) return null;
            var c = new CorpsComposition { GeneralId = free[0], Formation = _state.LearnedFormations[0], Role = Roles[Draft.Count % Roles.Count] };
            Draft.Add(c);
            return c;
        }

        public void RemoveCorps(int index)
        {
            if (index >= 0 && index < Draft.Count) Draft.RemoveAt(index);
        }

        /// <summary>장수 바꾸기: 다른 군단에 있던 장수면 서로 맞바꾼다.</summary>
        public void SetGeneral(int index, string generalId)
        {
            var c = Draft[index];
            foreach (var other in Draft)
                if (other != c && other.GeneralId == generalId) other.GeneralId = c.GeneralId;
            c.GeneralId = generalId;
        }

        public void SetFormation(int index, string formationId) => Draft[index].Formation = formationId;
        public void SetRole(int index, string role) => Draft[index].Role = role;

        /// <summary>
        /// 병종 칸 정하기 (army-v2.md 4장): 그 군단의 그 병종 인원을 troops로 (0 이하면 칸을 뺀다). 칸 수가 모자라면 false.
        /// 다른 군단의 같은 병종 인원은 그대로 (합이 묶음 병력을 넘으면 검사 R-5가 막는다).
        /// </summary>
        public bool SetPick(int index, string unitId, long troops)
        {
            var c = Draft[index];
            var pick = c.Picks.Find(p => p.UnitId == unitId);
            if (troops <= 0) { if (pick != null) c.Picks.Remove(pick); return true; }
            if (pick == null)
            {
                if (Army.OwnPicks(c) >= MaxPicks) return false;
                c.Picks.Add(new TroopPick { UnitId = unitId, Troops = troops });
            }
            else pick.Troops = troops;
            return true;
        }

        /// <summary>그 병종을 다른 군단들이 이미 쓰는 인원 (index 군단 빼고).</summary>
        public long UsedElsewhere(int index, string unitId)
        {
            long n = 0;
            for (int i = 0; i < Draft.Count; i++)
                if (i != index) foreach (var p in Draft[i].Picks) if (p.UnitId == unitId) n += p.Troops;
            return n;
        }

        // ---- 표시용 ----

        public long Troops(int index)
        {
            long total = 0;
            foreach (var p in Draft[index].Picks) total += p.Troops;
            return total;
        }

        public long Cap(int index)
        {
            var g = _state.FindGeneral(Draft[index].GeneralId);
            return g == null ? 0 : TroopCapCalculator.Cap(_defs.Balance, CorpsValidator.EffectiveStat(_defs, g, "leadership"));
        }

        /// <summary>어느 군단에도 없는 출전 가능 장수.</summary>
        public List<string> FreeGenerals()
        {
            var list = new List<string>();
            foreach (var g in _state.Generals)
            {
                if (!GeneralStatus.CanDeploy(g.Status)) continue;
                bool used = false;
                foreach (var c in Draft) if (c.GeneralId == g.GeneralId) { used = true; break; }
                if (!used) list.Add(g.GeneralId);
            }
            return list;
        }

        /// <summary>그 병종을 쓰는 첫 군단 번호 (없으면 -1).</summary>
        public int CorpsOf(string unitId)
        {
            for (int i = 0; i < Draft.Count; i++)
                foreach (var p in Draft[i].Picks) if (p.UnitId == unitId) return i;
            return -1;
        }

        public static CorpsComposition Copy(CorpsComposition c)
        {
            var copy = new CorpsComposition { GeneralId = c.GeneralId, Formation = c.Formation, Role = c.Role };
            foreach (var p in c.Picks) copy.Picks.Add(new TroopPick { UnitId = p.UnitId, Troops = p.Troops, Hired = p.Hired });
            return copy;
        }

        private static bool Same(CorpsComposition a, CorpsComposition b)
        {
            if (a.GeneralId != b.GeneralId || a.Formation != b.Formation || a.Role != b.Role || a.Picks.Count != b.Picks.Count) return false;
            for (int i = 0; i < a.Picks.Count; i++) if (a.Picks[i].UnitId != b.Picks[i].UnitId || a.Picks[i].Troops != b.Picks[i].Troops) return false;
            return true;
        }
    }
}
