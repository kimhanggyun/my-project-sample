using System.Collections.Generic;
using Game.Battle.Model.Contests;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;

namespace Game.Flow
{
    /// <summary>
    /// 전투 중 일기토를 플레이어가 한 라운드씩 고르게 한다 (R-71). 플레이어가 건 쪽(A)이든 받은 쪽(B, R-79)이든
    /// 화면에는 늘 플레이어를 A(왼쪽)로 보여 준다 (받은 쪽이면 라운드·결과를 뒤집은 사본).
    /// 계산은 전투와 같은 <see cref="InBattleDuels.Simulate"/> (이 대결만의 시드)라, 화면이 보여 준 결과가 명령 duelChoices로 그대로 전투에 들어간다.
    /// </summary>
    public sealed class BattleDuelPlay : IContestPlay
    {
        private readonly BattleSimulation _sim;
        private readonly DuelState _duel;
        private readonly List<string> _choices = new List<string>();
        private ContestResult _full;

        public BattleDuelPlay(BattleSimulation sim, DuelState duel)
        {
            _sim = sim;
            _duel = duel;
            Def = sim.Context.Defs.Contests[duel.ContestId];
            PlayerIsA = sim.Context.FindCorps(duel.CorpsA).Setup.Controller == Controllers.Player;
            Recalculate();
        }

        public DuelState Duel => _duel;
        /// <summary>플레이어가 건 쪽인가. 아니면 받은 쪽 (R-79).</summary>
        public bool PlayerIsA { get; }
        private string PlayerCorps => PlayerIsA ? _duel.CorpsA : _duel.CorpsB;
        private string OpponentCorps => PlayerIsA ? _duel.CorpsB : _duel.CorpsA;
        public ContestDef Def { get; }
        public string GeneralA => _sim.Context.FindCorps(PlayerCorps).Setup.General?.Id;
        public string GeneralB => _sim.Context.FindCorps(OpponentCorps).Setup.General?.Id;
        public ulong Seed => _duel.Seed;
        public IReadOnlyList<string> Choices => _choices;
        public bool IsOver { get; private set; }
        public ContestResult Result => IsOver ? _full : null;

        public List<ContestRound> Rounds()
        {
            var list = new List<ContestRound>();
            int n = IsOver ? _full.Rounds.Count : System.Math.Min(_choices.Count, _full.Rounds.Count);
            for (int i = 0; i < n; i++) list.Add(_full.Rounds[i]);
            return list;
        }

        public List<string> Available()
        {
            if (IsOver) return new List<string>();
            var rounds = Rounds();
            return Def.AvailableFor(rounds.Count == 0 ? Game.Foundation.Math.Fix.Zero : rounds[rounds.Count - 1].ChargeA);
        }

        public bool Choose(string actionId)
        {
            if (IsOver || !Available().Contains(actionId)) return false;
            _choices.Add(actionId);
            Recalculate();
            return true;
        }

        public string ChooseRandom(ulong seed)
        {
            var options = Available();
            if (options.Count == 0) return null;
            var rng = new Game.Foundation.Random.SeededRandom(unchecked(seed + (ulong)_choices.Count * 0x9E3779B97F4A7C15UL));
            string pick = options[rng.NextInt(0, options.Count)];
            Choose(pick);
            return pick;
        }

        /// <summary>남은 라운드는 AI (기록은 고른 데까지, 전투도 같은 AI·같은 난수).</summary>
        public void AutoFinish() => IsOver = true;

        /// <summary>전투에 넣을 명령 (플레이어 쪽 선택 기록).</summary>
        public Game.Battle.Model.Commands.BattleCommand Command() => new Game.Battle.Model.Commands.BattleCommand
        {
            CorpsId = PlayerCorps,
            Kind = Game.Battle.Model.Commands.CommandKinds.DuelChoices,
            Mode = string.Join(",", _choices),
        };

        private void Recalculate()
        {
            var r = InBattleDuels.Simulate(_sim.Context.Defs, _sim.Context.Setup, _duel, _choices, PlayerIsA);
            _full = PlayerIsA ? r : r.Mirror(); // 화면은 늘 플레이어 = A
            IsOver = _full.Rounds.Count <= _choices.Count;
        }
    }
}
