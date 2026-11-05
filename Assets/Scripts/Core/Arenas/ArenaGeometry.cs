using UnityEngine;

namespace NeonStrikers.Arenas
{
    /// <summary>Rotated rectangle in world space.</summary>
    public struct OrientedBox
    {
        public Vector2 Center;
        public Vector2 HalfSize;
        /// <summary>Unit vector of the local X axis.</summary>
        public Vector2 AxisX;
        /// <summary>Unit vector of the local Y axis ("up").</summary>
        public Vector2 AxisY;

        public OrientedBox(Vector2 center, Vector2 size, float rotationDegrees)
        {
            Center = center;
            HalfSize = new Vector2(Mathf.Abs(size.x) * 0.5f, Mathf.Abs(size.y) * 0.5f);
            float rad = rotationDegrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            AxisX = new Vector2(c, s);
            AxisY = new Vector2(-s, c);
        }

        public OrientedBox Expanded(float amount)
        {
            OrientedBox box = this;
            box.HalfSize = new Vector2(Mathf.Max(0f, HalfSize.x + amount), Mathf.Max(0f, HalfSize.y + amount));
            return box;
        }

        public Vector2 ToLocal(Vector2 world)
        {
            Vector2 d = world - Center;
            return new Vector2(Vector2.Dot(d, AxisX), Vector2.Dot(d, AxisY));
        }

        public Vector2 ToWorld(Vector2 local)
        {
            return Center + AxisX * local.x + AxisY * local.y;
        }

        public bool Contains(Vector2 point)
        {
            Vector2 local = ToLocal(point);
            return Mathf.Abs(local.x) <= HalfSize.x && Mathf.Abs(local.y) <= HalfSize.y;
        }

        public bool OverlapsCircle(Vector2 center, float radius)
        {
            Vector2 local = ToLocal(center);
            float cx = Mathf.Clamp(local.x, -HalfSize.x, HalfSize.x);
            float cy = Mathf.Clamp(local.y, -HalfSize.y, HalfSize.y);
            float dx = local.x - cx;
            float dy = local.y - cy;
            return dx * dx + dy * dy <= radius * radius;
        }

        public Vector2[] Corners()
        {
            return new[]
            {
                ToWorld(new Vector2(-HalfSize.x, -HalfSize.y)),
                ToWorld(new Vector2(HalfSize.x, -HalfSize.y)),
                ToWorld(new Vector2(HalfSize.x, HalfSize.y)),
                ToWorld(new Vector2(-HalfSize.x, HalfSize.y))
            };
        }

        public Rect Aabb()
        {
            float ex = Mathf.Abs(AxisX.x) * HalfSize.x + Mathf.Abs(AxisY.x) * HalfSize.y;
            float ey = Mathf.Abs(AxisX.y) * HalfSize.x + Mathf.Abs(AxisY.y) * HalfSize.y;
            return Rect.MinMaxRect(Center.x - ex, Center.y - ey, Center.x + ex, Center.y + ey);
        }

        /// <summary>Separating axis test.</summary>
        public bool Overlaps(OrientedBox other)
        {
            return !Separated(this, other, AxisX) && !Separated(this, other, AxisY) &&
                   !Separated(this, other, other.AxisX) && !Separated(this, other, other.AxisY);
        }

        static bool Separated(OrientedBox a, OrientedBox b, Vector2 axis)
        {
            float ra = a.HalfSize.x * Mathf.Abs(Vector2.Dot(a.AxisX, axis)) + a.HalfSize.y * Mathf.Abs(Vector2.Dot(a.AxisY, axis));
            float rb = b.HalfSize.x * Mathf.Abs(Vector2.Dot(b.AxisX, axis)) + b.HalfSize.y * Mathf.Abs(Vector2.Dot(b.AxisY, axis));
            float distance = Mathf.Abs(Vector2.Dot(b.Center - a.Center, axis));
            return distance > ra + rb;
        }
    }

    /// <summary>World-space shapes of arena objects, matching how the runtime builder creates them.</summary>
    public static class ArenaGeometry
    {
        public static OrientedBox BoxOf(ArenaObjectData obj)
        {
            switch (obj.Kind)
            {
                case ArenaObjectType.BlueSpawn:
                case ArenaObjectType.RedSpawn:
                    return PlayerBody(obj.Position);
                case ArenaObjectType.Ball:
                    return new OrientedBox(obj.Position, Vector2.one * ArenaRules.BallRadius * 2f, 0f);
                case ArenaObjectType.SlowZone:
                    return new OrientedBox(obj.Position, Vector2.one * obj.width, 0f);
                default:
                    return new OrientedBox(obj.Position, obj.Size, obj.rotation);
            }
        }

        /// <summary>The collision box of a player standing on a spawn point.</summary>
        public static OrientedBox PlayerBody(Vector2 spawnPoint)
        {
            return new OrientedBox(spawnPoint + ArenaRules.PlayerBodyOffset, ArenaRules.PlayerBodySize, 0f);
        }

        public static float CircleRadius(ArenaObjectData obj)
        {
            if (obj.Kind == ArenaObjectType.Ball)
                return ArenaRules.BallRadius;
            return Mathf.Abs(obj.width) * 0.5f;
        }

        public static bool IsCircle(ArenaObjectData obj)
        {
            return obj.Kind == ArenaObjectType.Ball || obj.Kind == ArenaObjectType.SlowZone;
        }

        /// <summary>Exit direction of a portal (its local up axis).</summary>
        public static Vector2 PortalExitDirection(ArenaObjectData portal)
        {
            float rad = portal.rotation * Mathf.Deg2Rad;
            return new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad));
        }

        /// <summary>Where the runtime places the exit transform of a portal (in front of its face).</summary>
        public static Vector2 PortalExitPoint(ArenaObjectData portal)
        {
            return portal.Position + PortalExitDirection(portal) * (Mathf.Abs(portal.height) * 0.5f + ArenaRules.PortalExitGap);
        }

        /// <summary>Where an object entering the partner portal appears (exit point + exitOffset along the exit direction).</summary>
        public static Vector2 PortalLandingPoint(ArenaObjectData portal)
        {
            return PortalExitPoint(portal) + PortalExitDirection(portal) * ArenaRules.PortalExitOffset;
        }

        public static bool ContainsPoint(ArenaObjectData obj, Vector2 point)
        {
            if (IsCircle(obj))
                return (point - obj.Position).sqrMagnitude <= CircleRadius(obj) * CircleRadius(obj);

            return BoxOf(obj).Contains(point);
        }

        /// <summary>Axis aligned bounds of the object's visible shape.</summary>
        public static Rect BoundsOf(ArenaObjectData obj)
        {
            if (IsCircle(obj))
            {
                float r = CircleRadius(obj);
                return Rect.MinMaxRect(obj.x - r, obj.y - r, obj.x + r, obj.y + r);
            }

            return BoxOf(obj).Aabb();
        }

        public static bool Intersects(Rect a, Rect b)
        {
            return a.xMin <= b.xMax && a.xMax >= b.xMin && a.yMin <= b.yMax && a.yMax >= b.yMin;
        }
    }
}
