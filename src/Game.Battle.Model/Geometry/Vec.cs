using System;
using Game.Foundation.Math;

namespace Game.Battle.Model.Geometry
{
    /// <summary>전장 좌표 (칸 단위, Fix). x = 오른쪽, y = 아래쪽(맵 행 번호 방향).</summary>
    public readonly struct Vec : IEquatable<Vec>
    {
        public readonly Fix X;
        public readonly Fix Y;

        public Vec(Fix x, Fix y) { X = x; Y = y; }

        public static readonly Vec Zero = new Vec(Fix.Zero, Fix.Zero);

        public static Vec operator +(Vec a, Vec b) => new Vec(a.X + b.X, a.Y + b.Y);
        public static Vec operator -(Vec a, Vec b) => new Vec(a.X - b.X, a.Y - b.Y);
        public static Vec operator *(Vec a, Fix s) => new Vec(a.X * s, a.Y * s);

        public Fix Dot(Vec o) => X * o.X + Y * o.Y;
        public Fix LengthSquared => X * X + Y * Y;
        public Fix Length => Fix.Sqrt(LengthSquared);

        public Vec Normalized()
        {
            Fix len = Length;
            return len == Fix.Zero ? Zero : new Vec(X / len, Y / len);
        }

        /// <summary>앞 방향이 이 벡터일 때의 오른쪽 방향 (화면 y가 아래쪽인 좌표계).</summary>
        public Vec RightOf() => new Vec(-Y, X);

        public static Fix DistanceSquared(Vec a, Vec b) => (a - b).LengthSquared;

        public bool Equals(Vec o) => X == o.X && Y == o.Y;
        public override bool Equals(object obj) => obj is Vec v && Equals(v);
        public override int GetHashCode() => X.GetHashCode() * 397 ^ Y.GetHashCode();
        public override string ToString() => "(" + X + ", " + Y + ")";
    }

    /// <summary>정수 칸.</summary>
    public readonly struct Cell : IEquatable<Cell>
    {
        public readonly int X;
        public readonly int Y;
        public Cell(int x, int y) { X = x; Y = y; }

        public static Cell Of(Vec p) => new Cell((int)p.X.FloorToLong(), (int)p.Y.FloorToLong());

        /// <summary>칸의 가운데 좌표.</summary>
        public Vec Center => new Vec(Fix.FromInt(X) + Half, Fix.FromInt(Y) + Half);

        private static readonly Fix Half = Fix.FromRatio(1, 2);

        public bool Equals(Cell o) => X == o.X && Y == o.Y;
        public override bool Equals(object obj) => obj is Cell c && Equals(c);
        public override int GetHashCode() => X * 7919 + Y;
        public override string ToString() => "[" + X + "," + Y + "]";
    }

    /// <summary>방향 문자열(N, S, E, W, NE, NW, SE, SW) → 단위 벡터. N은 화면 위쪽(y 감소).</summary>
    public static class Facing
    {
        public static Vec Parse(string id)
        {
            int dx = 0, dy = 0;
            foreach (char c in id ?? throw new ArgumentNullException(nameof(id)))
            {
                switch (c)
                {
                    case 'N': dy = -1; break;
                    case 'S': dy = 1; break;
                    case 'E': dx = 1; break;
                    case 'W': dx = -1; break;
                    default: throw new ArgumentException($"알 수 없는 방향 '{id}'.");
                }
            }
            if (dx == 0 && dy == 0) throw new ArgumentException($"알 수 없는 방향 '{id}'.");
            return new Vec(Fix.FromInt(dx), Fix.FromInt(dy)).Normalized();
        }
    }
}
