using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonStrikers.Arenas
{
    /// <summary>Every kind of element a player can put into a custom arena.</summary>
    public enum ArenaObjectType
    {
        /// <summary>Unreadable / unknown type found in a damaged or newer file.</summary>
        Unknown = 0,
        Wall = 1,
        Platform = 2,
        /// <summary>The ball. Its position is the ball's kick-off (spawn) position.</summary>
        Ball = 3,
        /// <summary>Blue player's spawn position.</summary>
        BlueSpawn = 4,
        /// <summary>Red player's spawn position.</summary>
        RedSpawn = 5,
        /// <summary>Portal trigger. Linked portals teleport to each other; local "up" is the exit direction.</summary>
        Portal = 6,
        /// <summary>The goal Blue defends: Red scores here (GoalZone.scoringTeam = "Red").</summary>
        BlueGoal = 7,
        /// <summary>The goal Red defends: Blue scores here (GoalZone.scoringTeam = "Blue").</summary>
        RedGoal = 8,
        /// <summary>Circular slow zone (local slow-down + goal slow motion), like the zones in front of the stock goals.</summary>
        SlowZone = 9
    }

    /// <summary>
    /// One element of a custom arena. Positions/sizes are world units of the game scenes
    /// (orthographic camera size 5), rotation is degrees counter-clockwise.
    /// Serialized with <see cref="JsonUtility"/>; the type is stored as a readable string.
    /// </summary>
    [Serializable]
    public class ArenaObjectData : ISerializationCallbackReceiver
    {
        public int id;
        public string type;
        public float x;
        public float y;
        public float width;
        public float height;
        public float rotation;

        /// <summary>Portals only: id of the connected portal, 0 when unpaired.</summary>
        public int linkedPortalId;

        [NonSerialized] private ArenaObjectType kind;

        public ArenaObjectType Kind
        {
            get => kind;
            set
            {
                kind = value;
                type = value.ToString();
            }
        }

        public Vector2 Position
        {
            get => new Vector2(x, y);
            set
            {
                x = value.x;
                y = value.y;
            }
        }

        public Vector2 Size
        {
            get => new Vector2(width, height);
            set
            {
                width = value.x;
                height = value.y;
            }
        }

        public bool IsPortal => kind == ArenaObjectType.Portal;
        public bool IsSolid => kind == ArenaObjectType.Wall || kind == ArenaObjectType.Platform;
        public bool IsGoal => kind == ArenaObjectType.BlueGoal || kind == ArenaObjectType.RedGoal;
        public bool IsSpawnLike => ArenaRules.IsUniqueType(kind);

        public ArenaObjectData Clone()
        {
            ArenaObjectData copy = (ArenaObjectData)MemberwiseClone();
            return copy;
        }

        public void OnBeforeSerialize()
        {
            if (kind != ArenaObjectType.Unknown || string.IsNullOrEmpty(type))
                type = kind.ToString();
        }

        public void OnAfterDeserialize()
        {
            kind = ParseType(type);
        }

        public static ArenaObjectType ParseType(string value)
        {
            if (string.IsNullOrEmpty(value))
                return ArenaObjectType.Unknown;

            if (Enum.TryParse(value, false, out ArenaObjectType parsed) &&
                Enum.IsDefined(typeof(ArenaObjectType), parsed) &&
                parsed.ToString() == value)
            {
                return parsed;
            }

            return ArenaObjectType.Unknown;
        }
    }

    /// <summary>A whole custom arena as stored on disk.</summary>
    [Serializable]
    public class ArenaData
    {
        public const int CurrentFormatVersion = 1;
        public const string DefaultName = "Untitled Arena";
        public const int MaxNameLength = 60;

        public int formatVersion = CurrentFormatVersion;
        public string id = "";
        public string name = DefaultName;
        public string createdUtc = "";
        public string modifiedUtc = "";
        public int nextObjectId = 1;
        public List<ArenaObjectData> objects = new List<ArenaObjectData>();

        public ArenaData Clone()
        {
            ArenaData copy = (ArenaData)MemberwiseClone();
            copy.objects = new List<ArenaObjectData>(objects != null ? objects.Count : 0);

            if (objects != null)
            {
                foreach (ArenaObjectData obj in objects)
                    copy.objects.Add(obj != null ? obj.Clone() : null);
            }

            return copy;
        }

        public ArenaObjectData Find(int objectId)
        {
            if (objects == null || objectId == 0)
                return null;

            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] != null && objects[i].id == objectId)
                    return objects[i];
            }

            return null;
        }

        public int IndexOf(int objectId)
        {
            if (objects == null)
                return -1;

            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] != null && objects[i].id == objectId)
                    return i;
            }

            return -1;
        }

        public int Count(ArenaObjectType kind)
        {
            int count = 0;
            if (objects == null)
                return 0;

            foreach (ArenaObjectData obj in objects)
            {
                if (obj != null && obj.Kind == kind)
                    count++;
            }

            return count;
        }

        public ArenaObjectData FirstOf(ArenaObjectType kind)
        {
            if (objects == null)
                return null;

            foreach (ArenaObjectData obj in objects)
            {
                if (obj != null && obj.Kind == kind)
                    return obj;
            }

            return null;
        }

        /// <summary>Hands out a fresh, never used object id.</summary>
        public int AllocateId()
        {
            int highest = 0;
            if (objects != null)
            {
                foreach (ArenaObjectData obj in objects)
                {
                    if (obj != null && obj.id > highest)
                        highest = obj.id;
                }
            }

            if (nextObjectId <= highest)
                nextObjectId = highest + 1;
            if (nextObjectId < 1)
                nextObjectId = 1;

            return nextObjectId++;
        }

        public string DisplayName => string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
    }

    /// <summary>Creates objects with sensible default sizes and the ready-made layouts.</summary>
    public static class ArenaFactory
    {
        public static ArenaObjectData Create(ArenaObjectType kind, int id, Vector2 position)
        {
            Vector2 size = ArenaRules.DefaultSize(kind);
            return new ArenaObjectData
            {
                id = id,
                Kind = kind,
                x = position.x,
                y = position.y,
                width = size.x,
                height = size.y,
                rotation = 0f,
                linkedPortalId = 0
            };
        }

        static ArenaObjectData Add(ArenaData data, ArenaObjectType kind, float x, float y, float w, float h, float rotation = 0f)
        {
            ArenaObjectData obj = Create(kind, data.AllocateId(), new Vector2(x, y));
            if (!ArenaRules.IsUniqueType(kind))
            {
                obj.width = w;
                obj.height = h;
            }

            obj.rotation = rotation;
            data.objects.Add(obj);
            return obj;
        }

        static ArenaData NewArena(string name)
        {
            string now = DateTime.UtcNow.ToString("o");
            return new ArenaData
            {
                formatVersion = ArenaData.CurrentFormatVersion,
                id = "",
                name = name,
                createdUtc = now,
                modifiedUtc = now,
                nextObjectId = 1,
                objects = new List<ArenaObjectData>()
            };
        }

        /// <summary>
        /// Closed box (floor, ceiling, side walls), the ball and both spawns, using the same
        /// positions as the stock arenas (ball at the centre, spawns at x = -6 / +6).
        /// </summary>
        public static ArenaData CreateEmpty(string name = ArenaData.DefaultName)
        {
            ArenaData data = NewArena(name);
            AddOuterBox(data);
            AddSpawns(data);
            return data;
        }

        /// <summary>
        /// A complete, valid starter arena inspired by Arena_01: outer walls with a goal pocket on each
        /// side, three platforms, a slow zone in front of each goal and one connected portal pair.
        /// </summary>
        public static ArenaData CreateStarter(string name = ArenaData.DefaultName)
        {
            ArenaData data = NewArena(name);
            AddOuterBox(data);

            // Goal pockets: a bar above each goal mouth, the goal trigger inside the pocket.
            Add(data, ArenaObjectType.Wall, -8.45f, -1.2f, 1.3f, 0.25f);
            Add(data, ArenaObjectType.Wall, 8.45f, -1.2f, 1.3f, 0.25f);
            Add(data, ArenaObjectType.BlueGoal, -8.75f, -2.95f, 0.5f, 3.2f);
            Add(data, ArenaObjectType.RedGoal, 8.75f, -2.95f, 0.5f, 3.2f);

            // Platforms (same layout as Arena_01).
            Add(data, ArenaObjectType.Platform, -2.2f, 0.4f, 2.7f, 0.25f);
            Add(data, ArenaObjectType.Platform, 2.2f, 0.4f, 2.7f, 0.25f);
            Add(data, ArenaObjectType.Platform, 0f, -2.55f, 6.3f, 0.25f);

            // Slow zones in front of the goals, like every stock arena.
            Add(data, ArenaObjectType.SlowZone, -7.6f, -3.2f, 3.2f, 3.2f);
            Add(data, ArenaObjectType.SlowZone, 7.6f, -3.2f, 3.2f, 3.2f);

            // A connected portal pair high on the side walls: enter left, come out on the right (and back).
            ArenaObjectData left = Add(data, ArenaObjectType.Portal, -8.55f, 3.1f, 2.0f, 0.8f, -90f);
            ArenaObjectData right = Add(data, ArenaObjectType.Portal, 8.55f, 3.1f, 2.0f, 0.8f, 90f);
            left.linkedPortalId = right.id;
            right.linkedPortalId = left.id;

            AddSpawns(data);
            return data;
        }

        static void AddOuterBox(ArenaData data)
        {
            Add(data, ArenaObjectType.Wall, 0f, -4.8f, 18.65f, 0.25f);
            Add(data, ArenaObjectType.Wall, 0f, 4.85f, 18.65f, 0.25f);
            Add(data, ArenaObjectType.Wall, -9.2f, 0.025f, 0.25f, 9.9f);
            Add(data, ArenaObjectType.Wall, 9.2f, 0.025f, 0.25f, 9.9f);
        }

        static void AddSpawns(ArenaData data)
        {
            Add(data, ArenaObjectType.Ball, 0f, 0f, 0f, 0f);
            Add(data, ArenaObjectType.BlueSpawn, -6f, 0f, 0f, 0f);
            Add(data, ArenaObjectType.RedSpawn, 6f, 0f, 0f, 0f);
        }
    }
}
