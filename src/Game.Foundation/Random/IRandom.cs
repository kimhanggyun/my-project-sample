namespace Game.Foundation.Random
{
    /// <summary>
    /// 규칙 코드가 쓰는 유일한 난수 통로. 엔진·언어 기본 난수를 직접 쓰지 않는다.
    /// </summary>
    public interface IRandom
    {
        /// <summary>[0, maxExclusive) 정수.</summary>
        int NextInt(int maxExclusive);

        /// <summary>[minInclusive, maxExclusive) 정수.</summary>
        int NextInt(int minInclusive, int maxExclusive);

        /// <summary>[0, 1) 고정소수점.</summary>
        Math.Fix NextFix01();

        /// <summary>확률 p(0~1)로 true.</summary>
        bool Chance(Math.Fix probability);

        /// <summary>현재 상태를 저장·복원할 수 있게 노출 (리플레이·세이브).</summary>
        ulong State { get; }
    }
}
