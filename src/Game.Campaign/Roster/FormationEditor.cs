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

        public int MaxCorps => _defs.Campaign.DefaultMaxCorps;

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

        /// <summary>칸에 부대 넣기 (null이면 비우기). 다른 칸·군단에 있던 부대면 거기서 빼서 옮긴다 (한 부대는 한 곳에만, R-5).</summary>
        public void Assign(int index, string slot, string unitId)
        {
            if (!SlotIds.IsValid(slot)) return;
            if (unitId != null)
                foreach (var other in Draft)
                {
                    string found = null;
                    foreach (var kv in other.Slots) if (kv.Value == unitId) { found = kv.Key; break; }
                    if (found != null) other.Slots.Remove(found);
                }
            if (unitId == null) Draft[index].Slots.Remove(slot);
            else Draft[index].Slots[slot] = unitId;
        }

        // ---- 표시용 ----

        public long Troops(int index)
        {
            long total = 0;
            foreach (var kv in Draft[index].Slots)
            {
                var u = _state.FindUnit(kv.Value);
                if (u != null) total += u.Troops;
            }
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

        /// <summary>그 부대가 들어 있는 군단 번호 (없으면 -1).</summary>
        public int CorpsOf(string unitId)
        {
            for (int i = 0; i < Draft.Count; i++)
                foreach (var kv in Draft[i].Slots) if (kv.Value == unitId) return i;
            return -1;
        }

        private static CorpsComposition Copy(CorpsComposition c) =>
            new CorpsComposition { GeneralId = c.GeneralId, Formation = c.Formation, Role = c.Role, Slots = new Dictionary<string, string>(c.Slots) };

        private static bool Same(CorpsComposition a, CorpsComposition b)
        {
            if (a.GeneralId != b.GeneralId || a.Formation != b.Formation || a.Role != b.Role || a.Slots.Count != b.Slots.Count) return false;
            foreach (var kv in a.Slots) if (!b.Slots.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
            return true;
        }
    }
}
