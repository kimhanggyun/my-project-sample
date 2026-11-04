using System;
using System.Globalization;

namespace Game.Foundation.Math
{
    /// <summary>
    /// 결정적 10진 고정소수점 수 (소수 4자리, long 기반: 값 × 10,000).
    /// 전투·성장 규칙 계산은 기기마다 결과가 달라질 수 있는 float/double 대신 이 타입을 쓴다.
    /// 10진이라 데이터의 0.4, 0.25, 72.5 같은 값이 정확히 표현된다.
    /// 곱셈은 내림(floor), 나눗셈은 0 쪽으로 버림. 넘침은 예외로 드러낸다.
    /// </summary>
    public readonly struct Fix : IEquatable<Fix>, IComparable<Fix>
    {
        public const int Decimals = 4;
        public const long One = 10000;

        public readonly long Raw;

        private Fix(long raw) { Raw = raw; }

        public static readonly Fix Zero = new Fix(0);
        public static readonly Fix OneValue = new Fix(One);
        public static readonly Fix Hundred = FromInt(100);

        public static Fix FromRaw(long raw) => new Fix(raw);
        public static Fix FromInt(long value) => new Fix(checked(value * One));

        /// <summary>분수 num/den. 0 쪽으로 버림.</summary>
        public static Fix FromRatio(long numerator, long denominator)
        {
            if (denominator == 0) throw new DivideByZeroException();
            return new Fix(checked(numerator * One) / denominator);
        }

        /// <summary>데이터 로딩용. 십진 문자열을 직접 해석하므로 기기와 무관하게 같은 값이 나온다.</summary>
        public static Fix Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return FromDecimal(decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        /// <summary>소수 4자리를 넘는 값은 반올림(0.5는 0에서 먼 쪽).</summary>
        public static Fix FromDecimal(decimal value)
        {
            decimal scaled = decimal.Round(value * One, 0, MidpointRounding.AwayFromZero);
            return new Fix(decimal.ToInt64(scaled));
        }

        public static Fix operator +(Fix a, Fix b) => new Fix(checked(a.Raw + b.Raw));
        public static Fix operator -(Fix a, Fix b) => new Fix(checked(a.Raw - b.Raw));
        public static Fix operator -(Fix a) => new Fix(checked(-a.Raw));
        public static Fix operator *(Fix a, Fix b) => new Fix(FloorDiv(checked(a.Raw * b.Raw), One));
        public static Fix operator *(Fix a, long b) => new Fix(checked(a.Raw * b));
        public static Fix operator /(Fix a, Fix b)
        {
            if (b.Raw == 0) throw new DivideByZeroException();
            return new Fix(checked(a.Raw * One) / b.Raw);
        }
        public static Fix operator /(Fix a, long b)
        {
            if (b == 0) throw new DivideByZeroException();
            return new Fix(a.Raw / b);
        }

        /// <summary>정수 ÷ 고정소수점, 내림. 큰 정수에서도 넘치지 않게 decimal로 계산한다 (decimal은 기기와 무관).</summary>
        public static long DivideFloor(long numerator, Fix divisor)
        {
            if (divisor.Raw == 0) throw new DivideByZeroException();
            decimal q = (decimal)numerator * One / divisor.Raw;
            return decimal.ToInt64(decimal.Floor(q));
        }

        public static bool operator ==(Fix a, Fix b) => a.Raw == b.Raw;
        public static bool operator !=(Fix a, Fix b) => a.Raw != b.Raw;
        public static bool operator <(Fix a, Fix b) => a.Raw < b.Raw;
        public static bool operator >(Fix a, Fix b) => a.Raw > b.Raw;
        public static bool operator <=(Fix a, Fix b) => a.Raw <= b.Raw;
        public static bool operator >=(Fix a, Fix b) => a.Raw >= b.Raw;

        public static Fix Min(Fix a, Fix b) => a.Raw <= b.Raw ? a : b;
        public static Fix Max(Fix a, Fix b) => a.Raw >= b.Raw ? a : b;
        public static Fix Clamp(Fix v, Fix min, Fix max) => Max(min, Min(max, v));

        /// <summary>내림 (음수는 더 작은 쪽).</summary>
        public long FloorToLong() => FloorDiv(Raw, One);

        /// <summary>올림.</summary>
        public long CeilToLong() => -FloorDiv(-Raw, One);

        /// <summary>반올림 (0.5는 양의 무한대 쪽).</summary>
        public long RoundToLong() => FloorDiv(checked(Raw + One / 2), One);

        /// <summary>화면 표시용. 규칙 계산에 쓰지 않는다.</summary>
        public double ToDisplayDouble() => (double)Raw / One;

        public bool Equals(Fix other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is Fix f && f.Raw == Raw;
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(Fix other) => Raw.CompareTo(other.Raw);

        public override string ToString() =>
            ((decimal)Raw / One).ToString("0.####", CultureInfo.InvariantCulture);

        private static long FloorDiv(long a, long b)
        {
            long q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }
    }
}
