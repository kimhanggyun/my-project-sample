using System.Collections.Generic;
using Game.Battle.Model.Contests;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;

namespace Game.Flow
{
    /// <summary>
    /// 전투 직전 대결(일기토·논쟁)을 플레이어가 한 라운드씩 고르게 한다 (battle.md 2.7).
    /// 대결은 전투를 만들 때 시드 난수로 이미 계산되므로, 고른 행동 기록(ChoicesA)을 넣고 다시 계산해 지금까지의 라운드를 보여준다.
    /// k번째까지의 라운드는 앞의 k개 선택에만 달렸으므로 (기록이 남아 있으면 난수를 쓰지 않는다) 다시 계산해도 앞 라운드는 그대로이고,
    /// 마지막 기록이 그대로 전투에 들어가 리플레이와 같은 결과가 나온다 (R-29).
    /// </summary>
    /// <summary>대결 화면이 쓰는 한 판 (전투 직전 대결 <see cref="ContestPlay"/>, 전투 중 일기토 <see cref="BattleDuelPlay"/>). A = 플레이어 쪽.</summary>
    public interface IContestPlay
    {
        ContestDef Def { get; }
        string GeneralA { get; }
        string GeneralB { get; }
        /// <summary>시간 안에 못 골랐을 때 무작위의 시드.</summary>
        ulong Seed { get; }
        IReadOnlyList<string> Choices { get; }
        bool IsOver { get; }
        ContestResult Result { get; }
        List<ContestRound> Rounds();
        List<string> Available();
        bool Choose(string actionId);
        string ChooseRandom(ulong seed);
        void AutoFinish();
    }

    public sealed class ContestPlay : IContestPlay
    {
        private readonly BattleSetup _setup;
        private readonly int _index;
        private ContestResult _full;

        /// <param name="index">setup.Contests 번호. 앞 번호의 대결은 기록이 끝나 있어야 한다.</param>
        public ContestPlay(BattleSetup setup, int index)
        {
            _setup = setup;
            _index = index;
            var cs = setup.Contests[index];
            Def = setup.Defs.Contests[cs.ContestId];
            cs.ChoicesA = new List<string>();
            Recalculate();
        }

        public ContestDef Def { get; }
        public ContestSetup Setup => _setup.Contests[_index];
        public string GeneralA => GeneralOf(Setup.CorpsA);
        public string GeneralB => GeneralOf(Setup.CorpsB);
        public ulong Seed => _setup.Seed;

        private string GeneralOf(string corpsId)
        {
            foreach (var c in _setup.Corps) if (c.Id == corpsId) return c.General?.Id;
            return null;
        }
        /// <summary>내가 고른 행동 (전투에 그대로 들어간다).</summary>
        public IReadOnlyList<string> Choices => Setup.ChoicesA;
        public bool IsOver { get; private set; }
        /// <summary>끝났으면 결과, 아니면 null.</summary>
        public ContestResult Result => IsOver ? _full : null;
        public ContestFighter A => _full.A;
        public ContestFighter B => _full.B;

        /// <summary>지금까지 보여줄 라운드 (내가 고른 만큼).</summary>
        public List<ContestRound> Rounds()
        {
            var list = new List<ContestRound>();
            int n = IsOver ? _full.Rounds.Count : System.Math.Min(Choices.Count, _full.Rounds.Count);
            for (int i = 0; i < n; i++) list.Add(_full.Rounds[i]);
            return list;
        }

        /// <summary>다음 라운드에 고를 수 있는 행동.</summary>
        public List<string> Available()
        {
            if (IsOver) return new List<string>();
            var rounds = Rounds();
            return Def.AvailableFor(rounds.Count == 0 ? Game.Foundation.Math.Fix.Zero : rounds[rounds.Count - 1].ChargeA);
        }

        /// <summary>행동 하나를 고른다. 고를 수 없는 행동이면 false.</summary>
        public bool Choose(string actionId)
        {
            if (IsOver || !Available().Contains(actionId)) return false;
            Setup.ChoicesA.Add(actionId);
            Recalculate();
            return true;
        }

        /// <summary>
        /// 시간 안에 못 골랐을 때 (battle.md 2.7.1: 무작위). 시드 난수로 고르고 기록하므로 리플레이는 같다.
        /// </summary>
        public string ChooseRandom(ulong seed)
        {
            var options = Available();
            if (options.Count == 0) return null;
            var rng = new Game.Foundation.Random.SeededRandom(unchecked(seed + (ulong)Choices.Count * 0x9E3779B97F4A7C15UL));
            string pick = options[rng.NextInt(0, options.Count)];
            Choose(pick);
            return pick;
        }

        /// <summary>
        /// 남은 라운드를 AI에게 맡기고 끝낸다 ([자동]). 기록은 내가 고른 데까지만 남기고, 나머지는 전투가 같은 AI·같은 난수로 고른다
        /// (AI의 선택을 기록으로 바꿔 넣으면 난수를 쓰는 순서가 달라져 결과가 바뀐다).
        /// </summary>
        public void AutoFinish()
        {
            if (IsOver) return;
            IsOver = true;
        }

        private void Recalculate()
        {
            _full = new BattleSimulation(_setup).PreBattleContests[_index];
            IsOver = _full.Rounds.Count <= Choices.Count;
        }
    }
}
