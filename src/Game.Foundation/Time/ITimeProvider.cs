using System;

namespace Game.Foundation.Time
{
    /// <summary>
    /// 현재 시각을 얻는 유일한 통로. 지금은 기기 시계, 서버 도입 후 서버 시간으로 교체한다.
    /// </summary>
    public interface ITimeProvider
    {
        DateTimeOffset UtcNow { get; }
    }

    public sealed class SystemTimeProvider : ITimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    /// <summary>테스트·개발 메뉴용. 시간을 직접 움직인다 (구출 기한·대기시간 확인).</summary>
    public sealed class ManualTimeProvider : ITimeProvider
    {
        public ManualTimeProvider(DateTimeOffset start) { UtcNow = start; }
        public DateTimeOffset UtcNow { get; private set; }
        public void Advance(TimeSpan span) { UtcNow = UtcNow + span; }
        public void Set(DateTimeOffset value) { UtcNow = value; }
    }
}
