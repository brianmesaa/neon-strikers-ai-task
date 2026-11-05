using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NeonStrikers.Arenas
{
    public enum ArenaIssueSeverity
    {
        /// <summary>Advice only; never blocks anything.</summary>
        Warning = 0,

        /// <summary>The arena can be playtested (after a confirmation) but can't be added to the game flow.</summary>
        Error = 1,

        /// <summary>The arena can't even be playtested (e.g. no ball, invalid spawn point).</summary>
        Blocker = 2
    }

    public static class ArenaIssueCodes
    {
        public const string NoData = "no-data";
        public const string TooManyObjects = "too-many-objects";
        public const string InvalidNumber = "invalid-number";
        public const string InvalidSize = "invalid-size";
        public const string UnknownType = "unknown-type";
        public const string DuplicateId = "duplicate-id";
        public const string MissingBall = "missing-ball";
        public const string MultipleBalls = "multiple-balls";
        public const string MissingSpawn = "missing-spawn";
        public const string MultipleSpawns = "multiple-spawns";
        public const string SpawnOutOfBounds = "spawn-out-of-bounds";
        public const string SpawnInsideSolid = "spawn-inside-solid";
        public const string SpawnInsidePortal = "spawn-inside-portal";
        public const string BallSpawnInsideGoal = "ball-spawn-inside-goal";
        public const string SpawnsOverlap = "spawns-overlap";
        public const string BallOverlapsPlayer = "ball-overlaps-player";
        public const string MissingGoal = "missing-goal";
        public const string GoalUnreachable = "goal-unreachable";
        public const string UnpairedPortal = "unpaired-portal";
        public const string PortalLinkBroken = "portal-link-broken";
        public const string PortalExitBlocked = "portal-exit-blocked";
        public const string PortalExitOutOfBounds = "portal-exit-out-of-bounds";
        public const string ObjectOutOfBounds = "object-out-of-bounds";
        public const string BallCanEscape = "ball-can-escape";
        public const string PlayerCanEscape = "player-can-escape";
        public const string NameMissing = "name-missing";
    }

    public sealed class ArenaIssue
    {
        public ArenaIssueSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public IReadOnlyList<int> ObjectIds { get; }

        public ArenaIssue(ArenaIssueSeverity severity, string code, string message, params int[] objectIds)
        {
            Severity = severity;
            Code = code;
            Message = message;
            ObjectIds = objectIds ?? new int[0];
        }

        public override string ToString()
        {
            return Severity + " [" + Code + "] " + Message;
        }
    }

    public sealed class ArenaValidationReport
    {
        readonly List<ArenaIssue> issues = new List<ArenaIssue>();

        public IReadOnlyList<ArenaIssue> Issues => issues;

        public bool HasBlockers => issues.Any(i => i.Severity == ArenaIssueSeverity.Blocker);
        public bool HasErrors => issues.Any(i => i.Severity == ArenaIssueSeverity.Error);

        /// <summary>Playtest needs a ball and valid spawns (blockers); errors only ask for confirmation.</summary>
        public bool CanPlaytest => !HasBlockers;

        /// <summary>The game flow only accepts arenas without blockers and errors.</summary>
        public bool CanUseInGameFlow => !HasBlockers && !HasErrors;

        public int Count(ArenaIssueSeverity severity)
        {
            return issues.Count(i => i.Severity == severity);
        }

        public IEnumerable<ArenaIssue> OfSeverity(ArenaIssueSeverity severity)
        {
            return issues.Where(i => i.Severity == severity);
        }

        public bool Contains(string code)
        {
            return issues.Any(i => i.Code == code);
        }

        public bool Contains(string code, int objectId)
        {
            return issues.Any(i => i.Code == code && i.ObjectIds.Contains(objectId));
        }

        public bool Mentions(int objectId)
        {
            return issues.Any(i => i.Severity != ArenaIssueSeverity.Warning && i.ObjectIds.Contains(objectId));
        }

        public string Summary
        {
            get
            {
                int blockers = Count(ArenaIssueSeverity.Blocker);
                int errors = Count(ArenaIssueSeverity.Error);
                int warnings = Count(ArenaIssueSeverity.Warning);

                if (blockers == 0 && errors == 0)
                    return warnings == 0 ? "Ready to play" : "Ready to play (" + warnings + " tip" + (warnings == 1 ? "" : "s") + ")";

                return (blockers + errors) + " problem" + (blockers + errors == 1 ? "" : "s") + " to fix";
            }
        }

        internal void Add(ArenaIssue issue)
        {
            issues.Add(issue);
        }

        internal void Sort()
        {
            List<ArenaIssue> sorted = issues.OrderByDescending(i => (int)i.Severity).ToList();
            issues.Clear();
            issues.AddRange(sorted);
        }
    }

    /// <summary>
    /// Detects broken arenas and invalid configurations and explains how to fix them.
    /// Pure data checks, usable from the editor UI, the game flow and tests.
    /// </summary>
    public static class ArenaValidator
    {
        const float CellSize = 0.1f;
        const float PlayerClearance = 0.175f;

        public static ArenaValidationReport Validate(ArenaData data)
        {
            var report = new ArenaValidationReport();

            if (data == null || data.objects == null)
            {
                report.Add(new ArenaIssue(ArenaIssueSeverity.Blocker, ArenaIssueCodes.NoData,
                    "The arena file contains no arena data."));
                return report;
            }

            if (string.IsNullOrWhiteSpace(data.name))
            {
                report.Add(new ArenaIssue(ArenaIssueSeverity.Warning, ArenaIssueCodes.NameMissing,
                    "The arena has no name. Type one in the name box at the top, otherwise it is called '" + ArenaData.DefaultName + "'."));
            }

            if (data.objects.Count > ArenaRules.MaxObjects)
            {
                report.Add(new ArenaIssue(ArenaIssueSeverity.Error, ArenaIssueCodes.TooManyObjects,
                    "The arena has " + data.objects.Count + " objects; the limit is " + ArenaRules.MaxObjects + ". Delete some objects."));
            }

            List<ArenaObjectData> usable = CheckObjects(data, report);

            CheckUniqueObjects(usable, report, out ArenaObjectData ball, out ArenaObjectData blue, out ArenaObjectData red);
            CheckSpawns(usable, report, ball, blue, red);
            CheckPortals(usable, report);
            CheckGoalsExist(usable, report);
            CheckEnclosure(usable, report, ball, blue, red);

            report.Sort();
            return report;
        }

        // ------------------------------------------------------------------ per object

        static List<ArenaObjectData> CheckObjects(ArenaData data, ArenaValidationReport report)
        {
            var usable = new List<ArenaObjectData>();
            var seenIds = new HashSet<int>();

            foreach (ArenaObjectData obj in data.objects)
            {
                if (obj == null)
                    continue;

                if (obj.id <= 0 || !seenIds.Add(obj.id))
                {
                    report.Add(new ArenaIssue(ArenaIssueSeverity.Blocker, ArenaIssueCodes.DuplicateId,
                        "Two objects share the id " + obj.id + " (the arena file is damaged). Delete the duplicated object.", obj.id));
                    continue;
                }

                if (obj.Kind == ArenaObjectType.Unknown)
                {
                    report.Add(new ArenaIssue(ArenaIssueSeverity.Error, ArenaIssueCodes.UnknownType,
                        "Object #" + obj.id + " has an unknown type '" + obj.type + "'. Delete it.", obj.id));
                    continue;
                }

                if (!ArenaRules.IsFinite(obj.x) || !ArenaRules.IsFinite(obj.y) || !ArenaRules.IsFinite(obj.width) ||
                    !ArenaRules.IsFinite(obj.height) || !ArenaRules.IsFinite(obj.rotation))
                {
                    report.Add(new ArenaIssue(ArenaIssueSeverity.Blocker, ArenaIssueCodes.InvalidNumber,
                        ArenaRules.Describe(obj) + " has an invalid position, size or rotation. Delete it and place it again.", obj.id));
                    continue;
                }

                if (ArenaRules.IsResizable(obj.Kind) &&
                    (obj.width < ArenaRules.MinSize || obj.height < ArenaRules.MinSize ||
                     obj.width > ArenaRules.MaxSize || obj.height > ArenaRules.MaxSize))
                {
                    report.Add(new ArenaIssue(ArenaIssueSeverity.Error, ArenaIssueCodes.InvalidSize,
                        ArenaRules.Describe(obj) + " must be between " + ArenaRules.MinSize + " and " + ArenaRules.MaxSize +
                        " units wide and tall. Resize it.", obj.id));
                }

                if (!ArenaRules.IsUniqueType(obj.Kind) && !ArenaRules.Bounds.Contains(obj.Position))
                {
                    report.Add(new ArenaIssue(ArenaIssueSeverity.Error, ArenaIssueCodes.ObjectOutOfBounds,
                        ArenaRules.Describe(obj) + " is outside the arena. Drag it back inside the play area.", obj.id));
                }

                usable.Add(obj);
            }

            return usable;
        }

        // ------------------------------------------------------------------ ball + spawns

        static void CheckUniqueObjects(List<ArenaObjectData> objects, ArenaValidationReport report,
            out ArenaObjectData ball, out ArenaObjectData blue, out ArenaObjectData red)
        {
            ball = CheckUnique(objects, report, ArenaObjectType.Ball, ArenaIssueCodes.MissingBall, ArenaIssueCodes.MultipleBalls,
                "The arena has no ball. Pick 'Ball' in the Add panel and click where the ball should start.",
                "There are {0} balls; only one is allowed. Delete the extra balls.");

            blue = CheckUnique(objects, report, ArenaObjectType.BlueSpawn, ArenaIssueCodes.MissingSpawn, ArenaIssueCodes.MultipleSpawns,
                "Blue has no spawn point. Pick 'Blue Spawn' in the Add panel and click where Blue should start.",
                "There are {0} Blue spawn points; only one is allowed. Delete the extra ones.");

            red = CheckUnique(objects, report, ArenaObjectType.RedSpawn, ArenaIssueCodes.MissingSpawn, ArenaIssueCodes.MultipleSpawns,
                "Red has no spawn point. Pick 'Red Spawn' in the Add panel and click where Red should start.",
                "There are {0} Red spawn points; only one is allowed. Delete the extra ones.");
        }

        static ArenaObjectData CheckUnique(List<ArenaObjectData> objects, ArenaValidationReport report, ArenaObjectType kind,
            string missingCode, string multipleCode, string missingMessage, string multipleMessage)
        {
            List<ArenaObjectData> found = objects.Where(o => o.Kind == kind).ToList();

            if (found.Count == 0)
            {
                report.Add(new ArenaIssue(ArenaIssueSeverity.Blocker, missingCode, missingMessage));
                return null;
            }

            if (found.Count > 1)
            {
                report.Add(new ArenaIssue(ArenaIssueSeverity.Blocker, multipleCode, string.Format(multipleMessage, found.Count),
                    found.Select(o => o.id).ToArray()));
                return null;
            }

            return found[0];
        }

        static void CheckSpawns(List<ArenaObjectData> objects, ArenaValidationReport report,
            ArenaObjectData ball, ArenaObjectData blue, ArenaObjectData red)
        {
            Rect bounds = ArenaRules.Bounds;
            List<ArenaObjectData> solids = objects.Where(o => o.IsSolid).ToList();
            List<ArenaObjectData> portals = objects.Where(o => o.IsPortal).ToList();
            List<ArenaObjectData> goals = objects.Where(o => o.IsGoal).ToList();

            if (ball != null)
            {
                Vector2 p = ball.Position;
                float r = ArenaRules.BallRadius;
                if (p.x - r < bounds.xMin || p.x + r > bounds.xMax || p.y - r < bounds.yMin || p.y + r > bounds.yMax)
                {
                    report.Add(new ArenaIssue(ArenaIssueSeverity.Blocker, ArenaIssueCodes.SpawnOutOfBounds,
                        "The ball's spawn point is outside the arena. Drag the ball inside the play area.", ball.id));
                }
                else
                {
                    foreach (ArenaObjectData solid in solids)
                    {
                        if (SolidBox(solid).OverlapsCircle(p, r))
                        {
                            report.Add(new ArenaIssue(ArenaIssueSeverity.Blocker, ArenaIssueCodes.SpawnInsideSolid,
                                "The ball spawns inside " + ArenaRules.Describe(solid) + ". Move the ball to an open space.", ball.id, solid.id));
                        }
                    }

                    foreach (ArenaObjectData portal in portals)
                    {
                        if (ArenaGeometry.BoxOf(portal).OverlapsCircle(p, r))
                        {
                            report.Add(new ArenaIssue(ArenaIssueSeverity.Blocker, ArenaIssueCodes.SpawnInsidePortal,
                                "The ball spawns inside " + ArenaRules.Describe(portal) + " and would be teleported at kick-off. Move the ball or the portal.",
                                ball.id, portal.id));
                        }
                    }

                    foreach (ArenaObjectData goal in goals)
                    {
                        if (ArenaGeometry.BoxOf(goal).OverlapsCircle(p, r))
                        {
                            report.Add(new ArenaIssue(ArenaIssueSeverity.Blocker, ArenaIssueCodes.BallSpawnInsideGoal,
                                "The ball spawns inside " + ArenaRules.Describe(goal) + " and would score immediately. Move the ball out of the goal.",
                                ball.id, goal.id));
                        }
                    }
                }
            }

            CheckPlayerSpawn(blue, "Blue", report, solids, portals);
            CheckPlayerSpawn(red, "Red", report, solids, portals);

            if (blue != null && red != null)
            {
                OrientedBox blueBody = ArenaGeometry.PlayerBody(blue.Position);
                OrientedBox redBody = ArenaGeometry.PlayerBody(red.Position);
                if (blueBody.Overlaps(redBody) || Vector2.Distance(blue.Position, red.Position) < ArenaRules.MinSpawnSeparation)
                {
                    report.Add(new ArenaIssue(ArenaIssueSeverity.Blocker, ArenaIssueCodes.SpawnsOverlap,
                        "Blue and Red spawn on top of each other. Move the spawn points apart.", blue.id, red.id));
                }
            }

            if (ball != null)
            {
                foreach (ArenaObjectData player in new[] { blue, red })
                {
                    if (player != null && ArenaGeometry.PlayerBody(player.Position).OverlapsCircle(ball.Position, ArenaRules.BallRadius))
                    {
                        report.Add(new ArenaIssue(ArenaIssueSeverity.Error, ArenaIssueCodes.BallOverlapsPlayer,
                            "The ball spawns inside " + ArenaRules.DisplayName(player.Kind) + ". Move them apart.", ball.id, player.id));
                    }
                }
            }
        }

        static void CheckPlayerSpawn(ArenaObjectData spawn, string team, ArenaValidationReport report,
            List<ArenaObjectData> solids, List<ArenaObjectData> portals)
        {
            if (spawn == null)
                return;

            OrientedBox body = ArenaGeometry.PlayerBody(spawn.Position);
            Rect aabb = body.Aabb();
            Rect bounds = ArenaRules.Bounds;

            if (aabb.xMin < bounds.xMin || aabb.xMax > bounds.xMax || aabb.yMin < bounds.yMin || aabb.yMax > bounds.yMax)
            {
                report.Add(new ArenaIssue(ArenaIssueSeverity.Blocker, ArenaIssueCodes.SpawnOutOfBounds,
                    team + "'s spawn point is outside the arena. Drag it inside the play area.", spawn.id));
                return;
            }

            foreach (ArenaObjectData solid in solids)
            {
                if (SolidBox(solid).Overlaps(body))
                {
                    report.Add(new ArenaIssue(ArenaIssueSeverity.Blocker, ArenaIssueCodes.SpawnInsideSolid,
                        team + " spawns inside " + ArenaRules.Describe(solid) + ". Move the spawn point to an open space.", spawn.id, solid.id));
                }
            }

            foreach (ArenaObjectData portal in portals)
            {
                if (ArenaGeometry.BoxOf(portal).Overlaps(body))
                {
                    report.Add(new ArenaIssue(ArenaIssueSeverity.Blocker, ArenaIssueCodes.SpawnInsidePortal,
                        team + " spawns inside " + ArenaRules.Describe(portal) + " and would be teleported at kick-off. Move the spawn point or the portal.",
                        spawn.id, portal.id));
                }
            }
        }

        // ------------------------------------------------------------------ portals

        static void CheckPortals(List<ArenaObjectData> objects, ArenaValidationReport report)
        {
            List<ArenaObjectData> solids = objects.Where(o => o.IsSolid).ToList();

            foreach (ArenaObjectData portal in objects.Where(o => o.IsPortal))
            {
                if (portal.linkedPortalId == 0)
                {
                    report.Add(new ArenaIssue(ArenaIssueSeverity.Error, ArenaIssueCodes.UnpairedPortal,
                        ArenaRules.Describe(portal) + " is not connected to another portal. Select it and press 'Link to...' " +
                        "(or select two portals and press 'Link portals'), or delete it.", portal.id));
                    continue;
                }

                ArenaObjectData partner = objects.FirstOrDefault(o => o.id == portal.linkedPortalId);
                if (partner == null || !partner.IsPortal || partner.id == portal.id)
                {
                    report.Add(new ArenaIssue(ArenaIssueSeverity.Error, ArenaIssueCodes.PortalLinkBroken,
                        ArenaRules.Describe(portal) + " is linked to a portal that doesn't exist. Press 'Unlink' and link it again.", portal.id));
                    continue;
                }

                if (partner.linkedPortalId != portal.id)
                {
                    report.Add(new ArenaIssue(ArenaIssueSeverity.Error, ArenaIssueCodes.PortalLinkBroken,
                        ArenaRules.Describe(portal) + " points to " + ArenaRules.Describe(partner) +
                        ", but that portal is connected elsewhere. Unlink and link them again.", portal.id, partner.id));
                    continue;
                }

                // Things entering the partner come out of this portal.
                Vector2 landing = ArenaGeometry.PortalLandingPoint(portal);
                Rect inner = Rect.MinMaxRect(ArenaRules.Bounds.xMin + ArenaRules.BallRadius, ArenaRules.Bounds.yMin + ArenaRules.BallRadius,
                    ArenaRules.Bounds.xMax - ArenaRules.BallRadius, ArenaRules.Bounds.yMax - ArenaRules.BallRadius);

                if (!inner.Contains(landing))
                {
                    report.Add(new ArenaIssue(ArenaIssueSeverity.Error, ArenaIssueCodes.PortalExitOutOfBounds,
                        ArenaRules.Describe(portal) + " sends players and the ball outside the arena. Rotate it so its arrow points into the arena, or move it.",
                        portal.id));
                    continue;
                }

                foreach (ArenaObjectData solid in solids)
                {
                    if (SolidBox(solid).OverlapsCircle(landing, ArenaRules.BallRadius))
                    {
                        report.Add(new ArenaIssue(ArenaIssueSeverity.Error, ArenaIssueCodes.PortalExitBlocked,
                            "The exit of " + ArenaRules.Describe(portal) + " is blocked by " + ArenaRules.Describe(solid) +
                            ". Rotate the portal so its arrow points to open space, or move the wall.", portal.id, solid.id));
                        break;
                    }
                }
            }
        }

        static bool IsLinkedPair(ArenaObjectData portal, List<ArenaObjectData> objects, out ArenaObjectData partner)
        {
            partner = null;
            if (portal.linkedPortalId == 0)
                return false;

            partner = objects.FirstOrDefault(o => o.id == portal.linkedPortalId);
            return partner != null && partner.IsPortal && partner.id != portal.id && partner.linkedPortalId == portal.id;
        }

        // ------------------------------------------------------------------ goals

        static void CheckGoalsExist(List<ArenaObjectData> objects, ArenaValidationReport report)
        {
            if (!objects.Any(o => o.Kind == ArenaObjectType.BlueGoal))
            {
                report.Add(new ArenaIssue(ArenaIssueSeverity.Error, ArenaIssueCodes.MissingGoal,
                    "Missing Blue goal (the goal Blue defends, Red scores in it). Pick 'Blue Goal' in the Add panel and place it."));
            }

            if (!objects.Any(o => o.Kind == ArenaObjectType.RedGoal))
            {
                report.Add(new ArenaIssue(ArenaIssueSeverity.Error, ArenaIssueCodes.MissingGoal,
                    "Missing Red goal (the goal Red defends, Blue scores in it). Pick 'Red Goal' in the Add panel and place it."));
            }
        }

        // ------------------------------------------------------------------ enclosure (flood fill)

        static void CheckEnclosure(List<ArenaObjectData> objects, ArenaValidationReport report,
            ArenaObjectData ball, ArenaObjectData blue, ArenaObjectData red)
        {
            if (report.Issues.Any(i => i.Code == ArenaIssueCodes.SpawnOutOfBounds || i.Code == ArenaIssueCodes.SpawnInsideSolid))
                return;

            List<ArenaObjectData> solids = objects.Where(o => o.IsSolid).ToList();
            List<ArenaObjectData> linkedPortals = objects.Where(o => o.IsPortal && IsLinkedPair(o, objects, out _)).ToList();

            if (ball != null)
            {
                var grid = new ReachGrid(solids, ArenaRules.BallRadius);
                FloodResult result = grid.Flood(ball.Position, linkedPortals, objects);

                if (result.Escapes)
                {
                    report.Add(new ArenaIssue(ArenaIssueSeverity.Error, ArenaIssueCodes.BallCanEscape,
                        "The ball can fly out of the arena (" + result.EscapeSide + "). Close the gap with walls.", ball.id));
                }
                else if (!result.StartBlocked)
                {
                    CheckGoalReach(objects, report, grid, result, ArenaObjectType.BlueGoal, "Blue");
                    CheckGoalReach(objects, report, grid, result, ArenaObjectType.RedGoal, "Red");
                }
            }

            var playerGrid = new ReachGrid(solids, PlayerClearance);
            foreach (ArenaObjectData spawn in new[] { blue, red })
            {
                if (spawn == null)
                    continue;

                FloodResult result = playerGrid.Flood(ArenaGeometry.PlayerBody(spawn.Position).Center, linkedPortals, objects);
                if (result.Escapes)
                {
                    string team = spawn.Kind == ArenaObjectType.BlueSpawn ? "Blue" : "Red";
                    report.Add(new ArenaIssue(ArenaIssueSeverity.Error, ArenaIssueCodes.PlayerCanEscape,
                        team + " can leave the arena (" + result.EscapeSide + "). Close the gap with walls.", spawn.id));
                }
            }
        }

        static void CheckGoalReach(List<ArenaObjectData> objects, ArenaValidationReport report, ReachGrid grid, FloodResult result,
            ArenaObjectType goalKind, string team)
        {
            List<ArenaObjectData> goals = objects.Where(o => o.Kind == goalKind).ToList();
            if (goals.Count == 0)
                return;

            List<ArenaObjectData> unreachable = goals.Where(g => !grid.Reaches(result, ArenaGeometry.BoxOf(g).Expanded(ArenaRules.BallRadius))).ToList();

            if (unreachable.Count == goals.Count)
            {
                report.Add(new ArenaIssue(ArenaIssueSeverity.Error, ArenaIssueCodes.GoalUnreachable,
                    "The ball can never reach the " + team + " goal; it is walled off. Open a path to it.",
                    unreachable.Select(g => g.id).ToArray()));
            }
            else
            {
                foreach (ArenaObjectData goal in unreachable)
                {
                    report.Add(new ArenaIssue(ArenaIssueSeverity.Warning, ArenaIssueCodes.GoalUnreachable,
                        ArenaRules.Describe(goal) + " can't be reached by the ball, so it never counts.", goal.id));
                }
            }
        }

        static OrientedBox SolidBox(ArenaObjectData solid)
        {
            return ArenaGeometry.BoxOf(solid).Expanded(ArenaRules.SolidOutlineMargin);
        }

        struct FloodResult
        {
            public bool StartBlocked;
            public bool Escapes;
            public string EscapeSide;
            public bool[] Visited;
        }

        /// <summary>
        /// Grid over the arena (plus a ring of cells outside it) where cells the centre of a body with the given
        /// clearance can't occupy are blocked. A flood fill from a spawn that reaches the outside ring means the
        /// body can leave the arena. Linked portals are followed.
        /// </summary>
        sealed class ReachGrid
        {
            readonly int width;
            readonly int height;
            readonly float originX;
            readonly float originY;
            readonly bool[] blocked;
            readonly float clearance;

            public ReachGrid(List<ArenaObjectData> solids, float clearance)
            {
                this.clearance = clearance;
                Rect b = ArenaRules.Bounds;
                originX = b.xMin - CellSize * 2f;
                originY = b.yMin - CellSize * 2f;
                width = Mathf.CeilToInt((b.width + CellSize * 4f) / CellSize);
                height = Mathf.CeilToInt((b.height + CellSize * 4f) / CellSize);
                blocked = new bool[width * height];

                foreach (ArenaObjectData solid in solids)
                {
                    OrientedBox box = SolidBox(solid).Expanded(clearance);
                    Rect aabb = box.Aabb();
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((aabb.xMin - originX) / CellSize));
                    int x1 = Mathf.Min(width - 1, Mathf.CeilToInt((aabb.xMax - originX) / CellSize));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt((aabb.yMin - originY) / CellSize));
                    int y1 = Mathf.Min(height - 1, Mathf.CeilToInt((aabb.yMax - originY) / CellSize));

                    for (int y = y0; y <= y1; y++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            if (box.Contains(CellCenter(x, y)))
                                blocked[y * width + x] = true;
                        }
                    }
                }
            }

            Vector2 CellCenter(int x, int y)
            {
                return new Vector2(originX + (x + 0.5f) * CellSize, originY + (y + 0.5f) * CellSize);
            }

            bool TryCell(Vector2 point, out int x, out int y)
            {
                x = Mathf.FloorToInt((point.x - originX) / CellSize);
                y = Mathf.FloorToInt((point.y - originY) / CellSize);
                return x >= 0 && y >= 0 && x < width && y < height;
            }

            static string SideOf(Vector2 point)
            {
                Rect b = ArenaRules.Bounds;
                if (point.x < b.xMin) return "left side";
                if (point.x > b.xMax) return "right side";
                if (point.y > b.yMax) return "top";
                return "bottom";
            }

            public FloodResult Flood(Vector2 start, List<ArenaObjectData> linkedPortals, List<ArenaObjectData> objects)
            {
                var result = new FloodResult { Visited = new bool[width * height], EscapeSide = "" };

                if (!TryCell(start, out int sx, out int sy) || blocked[sy * width + sx])
                {
                    result.StartBlocked = true;
                    return result;
                }

                var portalBoxes = new List<OrientedBox>();
                var portalTargets = new List<Vector2>();
                foreach (ArenaObjectData portal in linkedPortals)
                {
                    ArenaObjectData partner = objects.FirstOrDefault(o => o.id == portal.linkedPortalId);
                    if (partner == null)
                        continue;
                    portalBoxes.Add(ArenaGeometry.BoxOf(portal).Expanded(clearance));
                    portalTargets.Add(ArenaGeometry.PortalLandingPoint(partner));
                }

                var usedPortal = new bool[portalBoxes.Count];
                var queue = new Queue<int>();
                result.Visited[sy * width + sx] = true;
                queue.Enqueue(sy * width + sx);
                Rect bounds = ArenaRules.Bounds;

                while (queue.Count > 0)
                {
                    int index = queue.Dequeue();
                    int x = index % width;
                    int y = index / width;
                    Vector2 center = CellCenter(x, y);

                    if (!bounds.Contains(center))
                    {
                        if (!result.Escapes)
                        {
                            result.Escapes = true;
                            result.EscapeSide = SideOf(center);
                        }

                        continue;
                    }

                    for (int p = 0; p < portalBoxes.Count; p++)
                    {
                        if (usedPortal[p] || !portalBoxes[p].Contains(center))
                            continue;

                        usedPortal[p] = true;
                        if (bounds.Contains(portalTargets[p]) && TryCell(portalTargets[p], out int tx, out int ty))
                            Visit(tx, ty, result.Visited, queue);
                    }

                    Visit(x + 1, y, result.Visited, queue);
                    Visit(x - 1, y, result.Visited, queue);
                    Visit(x, y + 1, result.Visited, queue);
                    Visit(x, y - 1, result.Visited, queue);
                }

                return result;
            }

            void Visit(int x, int y, bool[] visited, Queue<int> queue)
            {
                if (x < 0 || y < 0 || x >= width || y >= height)
                    return;

                int index = y * width + x;
                if (visited[index] || blocked[index])
                    return;

                visited[index] = true;
                queue.Enqueue(index);
            }

            public bool Reaches(FloodResult result, OrientedBox area)
            {
                Rect aabb = area.Aabb();
                int x0 = Mathf.Max(0, Mathf.FloorToInt((aabb.xMin - originX) / CellSize));
                int x1 = Mathf.Min(width - 1, Mathf.CeilToInt((aabb.xMax - originX) / CellSize));
                int y0 = Mathf.Max(0, Mathf.FloorToInt((aabb.yMin - originY) / CellSize));
                int y1 = Mathf.Min(height - 1, Mathf.CeilToInt((aabb.yMax - originY) / CellSize));

                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        if (result.Visited[y * width + x] && area.Contains(CellCenter(x, y)))
                            return true;
                    }
                }

                return false;
            }
        }
    }
}
