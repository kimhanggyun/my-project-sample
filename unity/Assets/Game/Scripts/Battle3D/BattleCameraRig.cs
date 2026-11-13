using UnityEngine;

namespace Game.Unity.Battle3D
{
    /// <summary>
    /// 고정 쿼터뷰 카메라 (battle.md 4장, 확정): 각도 고정·회전 없음, 끌기로 이동, 확대·축소, 군단으로 이동.
    /// 보는 중심(Focus)은 전장 안으로 제한한다. 화면 표시 전용이며 규칙과 무관하다.
    /// </summary>
    public sealed class BattleCameraRig
    {
        public const float Pitch = 52f;
        public const float MinDistance = 8f;
        public const float MaxDistance = 70f;

        private readonly float _width;
        private readonly float _depth;

        public Camera Camera { get; }
        public Vector3 Focus { get; private set; }
        public float Distance { get; private set; } = 26f;

        public BattleCameraRig(Camera camera, float fieldWidth, float fieldDepth)
        {
            Camera = camera;
            _width = fieldWidth;
            _depth = fieldDepth;
            Camera.transform.rotation = Quaternion.Euler(Pitch, 0f, 0f);
            Focus = new Vector3(fieldWidth / 2f, 0f, fieldDepth / 2f);
            Apply();
        }

        public void LookAt(Vector3 point)
        {
            Focus = point;
            Apply();
        }

        /// <summary>손가락 아래 땅이 손가락을 따라오도록 옮긴다.</summary>
        public void Pan(Vector2 fromScreen, Vector2 toScreen)
        {
            if (!Ground(fromScreen, 0f, out var a) || !Ground(toScreen, 0f, out var b)) return;
            Focus += a - b;
            Apply();
        }

        /// <summary>거리를 정해서 (최소~최대 안으로).</summary>
        public void SetDistance(float distance)
        {
            Distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
            Apply();
        }

        /// <param name="factor">1보다 크면 가까이.</param>
        public void Zoom(float factor)
        {
            if (factor <= 0f) return;
            Distance = Mathf.Clamp(Distance / factor, MinDistance, MaxDistance);
            Apply();
        }

        /// <summary>화면 위치 → 높이 height인 수평면 위의 점.</summary>
        public bool Ground(Vector2 screen, float height, out Vector3 point)
        {
            var ray = Camera.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            point = default;
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = (height - ray.origin.y) / ray.direction.y;
            if (t < 0f) return false;
            point = ray.origin + ray.direction * t;
            return true;
        }

        /// <summary>화면 영역·확대가 바뀐 뒤 다시 맞춘다.</summary>
        public void Refresh() => Apply();

        private void Apply()
        {
            Focus = new Vector3(Mathf.Clamp(Focus.x, 0f, _width), 0f, Mathf.Clamp(Focus.z, 0f, _depth));
            Place();
            // 전장 끝이 화면 안쪽으로 들어오면(맵 밖 빈 공간이 보이면) 그만큼 안으로 민다.
            // 화면이 전장보다 넓으면 가운데에 둔다. 화면 위·아래 가운데 지점이 바닥에 닿는 곳으로 잰다.
            var r = Camera.pixelRect;
            if (r.height < 2f || r.width < 2f) return;
            if (!Ground(new Vector2(r.center.x, r.yMin + 1f), 0f, out var bottom) || !Ground(new Vector2(r.center.x, r.yMax - 1f), 0f, out var top)) return;
            float dz = 0f;
            if (top.z - bottom.z >= _depth) dz = _depth / 2f - (top.z + bottom.z) / 2f;
            else if (bottom.z < 0f) dz = -bottom.z;
            else if (top.z > _depth) dz = _depth - top.z;
            float dx = 0f;
            if (Ground(new Vector2(r.xMin + 1f, r.center.y), 0f, out var left) && Ground(new Vector2(r.xMax - 1f, r.center.y), 0f, out var right))
            {
                if (right.x - left.x >= _width) dx = _width / 2f - (left.x + right.x) / 2f;
                else if (left.x < 0f) dx = -left.x;
                else if (right.x > _width) dx = _width - right.x;
            }
            if (dx == 0f && dz == 0f) return;
            Focus += new Vector3(dx, 0f, dz);
            Place();
        }

        private void Place() => Camera.transform.position = Focus - Camera.transform.forward * Distance;
    }
}
