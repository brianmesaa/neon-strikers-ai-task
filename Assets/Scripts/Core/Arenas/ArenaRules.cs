using UnityEngine;

namespace NeonStrikers.Arenas
{
    /// <summary>
    /// Dimensions and limits shared by the editor, the validator and the runtime builder.
    /// The gameplay numbers are taken from the stock arenas of the base project
    /// (Arena_01/02/03 all use the same Ball, Player and camera set-up).
    /// </summary>
    public static class ArenaRules
    {
        /// <summary>
        /// Editable / playable area. The stock arenas use an orthographic camera of size 5 and an arena
        /// background of ~19 x 10 units centred on the origin, with walls up to x = +-9.4.
        /// </summary>
        public static readonly Rect Bounds = Rect.MinMaxRect(-9.5f, -5f, 9.5f, 5f);

        /// <summary>Horizontal half extent the camera shows on a 16:9 screen (5 * 16 / 9).</summary>
        public const float SixteenByNineHalfWidth = 8.889f;

        /// <summary>Ball: scale 0.5, CircleCollider2D radius 0.5 =&gt; 0.25 world units.</summary>
        public const float BallRadius = 0.25f;

        /// <summary>Player BoxCollider2D (size 1 x 2.1434, offset 0 / 1.2529) at scale 0.35, relative to the spawn point.</summary>
        public static readonly Vector2 PlayerBodySize = new Vector2(0.35f, 0.7502f);
        public static readonly Vector2 PlayerBodyOffset = new Vector2(0f, 0.4385f);

        /// <summary>Distance from a portal's front face to its exit point (stock portals use ~0.6).</summary>
        public const float PortalExitGap = 0.6f;

        /// <summary>PortalTeleporter.exitOffset of the stock portals: objects appear this far past the exit point.</summary>
        public const float PortalExitOffset = 0.5f;

        /// <summary>Extra thickness of the white outline collider around stock walls/platforms (approximate, per side).</summary>
        public const float SolidOutlineMargin = 0.025f;

        public const float MinSize = 0.1f;
        public const float MaxSize = 40f;
        public const int MaxObjects = 250;
        public const float MaxCoordinate = 100f;

        /// <summary>Minimum distance between the two player spawns.</summary>
        public const float MinSpawnSeparation = 0.6f;

        public static bool IsUniqueType(ArenaObjectType kind)
        {
            return kind == ArenaObjectType.Ball || kind == ArenaObjectType.BlueSpawn || kind == ArenaObjectType.RedSpawn;
        }

        public static bool IsResizable(ArenaObjectType kind)
        {
            switch (kind)
            {
                case ArenaObjectType.Wall:
                case ArenaObjectType.Platform:
                case ArenaObjectType.Portal:
                case ArenaObjectType.BlueGoal:
                case ArenaObjectType.RedGoal:
                case ArenaObjectType.SlowZone:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsRotatable(ArenaObjectType kind)
        {
            switch (kind)
            {
                case ArenaObjectType.Wall:
                case ArenaObjectType.Platform:
                case ArenaObjectType.Portal:
                case ArenaObjectType.BlueGoal:
                case ArenaObjectType.RedGoal:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Slow zones are circles: width is the diameter and height always equals width.</summary>
        public static bool IsCircular(ArenaObjectType kind)
        {
            return kind == ArenaObjectType.SlowZone;
        }

        public static Vector2 DefaultSize(ArenaObjectType kind)
        {
            switch (kind)
            {
                case ArenaObjectType.Wall: return new Vector2(4f, 0.25f);
                case ArenaObjectType.Platform: return new Vector2(2.7f, 0.25f);
                case ArenaObjectType.Portal: return new Vector2(2f, 0.8f);
                case ArenaObjectType.BlueGoal:
                case ArenaObjectType.RedGoal: return new Vector2(0.5f, 3.2f);
                case ArenaObjectType.SlowZone: return new Vector2(3.2f, 3.2f);
                case ArenaObjectType.Ball: return new Vector2(BallRadius * 2f, BallRadius * 2f);
                case ArenaObjectType.BlueSpawn:
                case ArenaObjectType.RedSpawn: return PlayerBodySize;
                default: return Vector2.one;
            }
        }

        public static string DisplayName(ArenaObjectType kind)
        {
            switch (kind)
            {
                case ArenaObjectType.Wall: return "Wall";
                case ArenaObjectType.Platform: return "Platform";
                case ArenaObjectType.Ball: return "Ball";
                case ArenaObjectType.BlueSpawn: return "Blue Spawn";
                case ArenaObjectType.RedSpawn: return "Red Spawn";
                case ArenaObjectType.Portal: return "Portal";
                case ArenaObjectType.BlueGoal: return "Blue Goal";
                case ArenaObjectType.RedGoal: return "Red Goal";
                case ArenaObjectType.SlowZone: return "Slow Zone";
                default: return "Unknown object";
            }
        }

        public static string Describe(ArenaObjectData obj)
        {
            if (obj == null)
                return "(missing object)";

            return DisplayName(obj.Kind) + " #" + obj.id;
        }

        /// <summary>GoalZone.scoringTeam for a goal type ("Red" scores in Blue's goal and vice versa).</summary>
        public static string ScoringTeam(ArenaObjectType goalKind)
        {
            if (goalKind == ArenaObjectType.BlueGoal)
                return "Red";
            if (goalKind == ArenaObjectType.RedGoal)
                return "Blue";
            return "";
        }

        public static float SnapValue(float value, float step)
        {
            if (step <= 0f)
                return value;
            return Mathf.Round(value / step) * step;
        }

        public static Vector2 Snap(Vector2 value, float step)
        {
            return new Vector2(SnapValue(value.x, step), SnapValue(value.y, step));
        }

        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
