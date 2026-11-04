using System;
using Game.Foundation.Math;

namespace Game.Foundation.Random
{
    /// <summary>
    /// 시드 기반 결정적 난수 (SplitMix64). 같은 시드면 어느 기기에서든 같은 수열.
    /// </summary>
    public sealed class SeededRandom : IRandom
    {
        private ulong _state;

        public SeededRandom(ulong seed) { _state = seed; }

        public ulong State => _state;

        public static SeededRandom FromState(ulong state) => new SeededRandom(state);

        public ulong NextUInt64()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            // 편향 없는 범위 축소 (거부 샘플링)
            ulong bound = (ulong)maxExclusive;
            ulong threshold = (0UL - bound) % bound;
            while (true)
            {
                ulong r = NextUInt64();
                if (r >= threshold) return (int)(r % bound);
            }
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return minInclusive + NextInt(maxExclusive - minInclusive);
        }

        public Fix NextFix01() => Fix.FromRaw(NextInt((int)Fix.One));

        public bool Chance(Fix probability)
        {
            if (probability <= Fix.Zero) return false;
            if (probability >= Fix.OneValue) return true;
            return NextFix01() < probability;
        }
    }
}
