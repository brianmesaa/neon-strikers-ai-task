using System.Linq;
using NeonStrikers.Arenas;
using NUnit.Framework;
using UnityEngine;

namespace NeonStrikers.Tests
{
    /// <summary>Detection of broken arenas and invalid configurations.</summary>
    public class ArenaValidatorTests
    {
        static ArenaData Starter()
        {
            return ArenaFactory.CreateStarter("Validator Test");
        }

        static ArenaObjectData First(ArenaData data, ArenaObjectType kind)
        {
            return data.objects.First(o => o.Kind == kind);
        }

        static ArenaObjectData Add(ArenaData data, ArenaObjectType kind, float x, float y, float w, float h, float rotation = 0f)
        {
            ArenaObjectData obj = ArenaFactory.Create(kind, data.AllocateId(), new Vector2(x, y));
            obj.width = w;
            obj.height = h;
            obj.rotation = rotation;
            data.objects.Add(obj);
            return obj;
        }

        [Test]
        public void StarterArenaIsReadyForTheGame()
        {
            ArenaValidationReport report = ArenaValidator.Validate(Starter());
            Assert.IsTrue(report.CanPlaytest, string.Join("\n", report.Issues));
            Assert.IsTrue(report.CanUseInGameFlow, string.Join("\n", report.Issues));
            Assert.AreEqual(0, report.Count(ArenaIssueSeverity.Error) + report.Count(ArenaIssueSeverity.Blocker));
        }

        [Test]
        public void EmptyArenaCanBePlaytestedButNeedsGoalsForTheGame()
        {
            ArenaValidationReport report = ArenaValidator.Validate(ArenaFactory.CreateEmpty());
            Assert.IsTrue(report.CanPlaytest, string.Join("\n", report.Issues));
            Assert.IsFalse(report.CanUseInGameFlow);
            Assert.AreEqual(2, report.Issues.Count(i => i.Code == ArenaIssueCodes.MissingGoal));
            StringAssert.Contains("Blue goal", report.Issues.First(i => i.Code == ArenaIssueCodes.MissingGoal).Message);
        }

        [Test]
        public void MissingBallOrSpawnBlocksPlaytest()
        {
            ArenaData data = Starter();
            data.objects.Remove(First(data, ArenaObjectType.Ball));
            ArenaValidationReport report = ArenaValidator.Validate(data);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.MissingBall));
            Assert.IsFalse(report.CanPlaytest);

            data = Starter();
            data.objects.Remove(First(data, ArenaObjectType.RedSpawn));
            report = ArenaValidator.Validate(data);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.MissingSpawn));
            Assert.IsFalse(report.CanPlaytest);
        }

        [Test]
        public void InvalidSpawnPointsAreBlockers()
        {
            ArenaData inWall = Starter();
            First(inWall, ArenaObjectType.BlueSpawn).y = -4.8f;
            ArenaValidationReport report = ArenaValidator.Validate(inWall);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.SpawnInsideSolid, First(inWall, ArenaObjectType.BlueSpawn).id));
            Assert.IsFalse(report.CanPlaytest);

            ArenaData outside = Starter();
            First(outside, ArenaObjectType.RedSpawn).y = 7f;
            report = ArenaValidator.Validate(outside);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.SpawnOutOfBounds));
            Assert.IsFalse(report.CanPlaytest);

            ArenaData overlapping = Starter();
            First(overlapping, ArenaObjectType.RedSpawn).x = First(overlapping, ArenaObjectType.BlueSpawn).x + 0.1f;
            report = ArenaValidator.Validate(overlapping);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.SpawnsOverlap));
            Assert.IsFalse(report.CanPlaytest);

            ArenaData ballInGoal = Starter();
            ArenaObjectData goal = First(ballInGoal, ArenaObjectType.BlueGoal);
            First(ballInGoal, ArenaObjectType.Ball).Position = goal.Position;
            report = ArenaValidator.Validate(ballInGoal);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.BallSpawnInsideGoal));
            Assert.IsFalse(report.CanPlaytest);

            ArenaData ballInPortal = Starter();
            First(ballInPortal, ArenaObjectType.Ball).Position = First(ballInPortal, ArenaObjectType.Portal).Position;
            report = ArenaValidator.Validate(ballInPortal);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.SpawnInsidePortal));
        }

        [Test]
        public void DuplicateBallsAreReported()
        {
            ArenaData data = Starter();
            ArenaObjectData copy = First(data, ArenaObjectType.Ball).Clone();
            copy.id = data.AllocateId();
            copy.x += 1f;
            data.objects.Add(copy);

            ArenaValidationReport report = ArenaValidator.Validate(data);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.MultipleBalls));
            Assert.IsFalse(report.CanPlaytest);
        }

        [Test]
        public void UnpairedAndBrokenPortalsAreErrors()
        {
            ArenaData data = Starter();
            ArenaObjectData single = Add(data, ArenaObjectType.Portal, 0f, 3f, 2f, 0.8f);
            ArenaValidationReport report = ArenaValidator.Validate(data);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.UnpairedPortal, single.id));
            Assert.IsTrue(report.CanPlaytest, "an unfinished arena can still be playtested");
            Assert.IsFalse(report.CanUseInGameFlow);

            ArenaData broken = Starter();
            ArenaObjectData portal = First(broken, ArenaObjectType.Portal);
            portal.linkedPortalId = 999;
            report = ArenaValidator.Validate(broken);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.PortalLinkBroken, portal.id));

            ArenaData oneWay = Starter();
            ArenaObjectData third = Add(oneWay, ArenaObjectType.Portal, 0f, 3f, 2f, 0.8f);
            First(oneWay, ArenaObjectType.Portal).linkedPortalId = third.id;
            report = ArenaValidator.Validate(oneWay);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.PortalLinkBroken));
        }

        [Test]
        public void PortalExitsMustLeadIntoTheArena()
        {
            ArenaData outward = Starter();
            ArenaObjectData left = outward.objects.First(o => o.IsPortal && o.x < 0f);
            left.rotation = 90f;
            ArenaValidationReport report = ArenaValidator.Validate(outward);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.PortalExitOutOfBounds, left.id));

            ArenaData blocked = Starter();
            ArenaObjectData right = blocked.objects.First(o => o.IsPortal && o.x > 0f);
            ArenaObjectData wall = Add(blocked, ArenaObjectType.Wall, 7f, 3.1f, 0.25f, 1.5f);
            report = ArenaValidator.Validate(blocked);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.PortalExitBlocked, right.id));
            Assert.IsTrue(report.Issues.First(i => i.Code == ArenaIssueCodes.PortalExitBlocked).ObjectIds.Contains(wall.id));
        }

        [Test]
        public void OpenArenaIsDetected()
        {
            ArenaData data = Starter();
            data.objects.Remove(data.objects.First(o => o.Kind == ArenaObjectType.Wall && o.x < -9f));

            ArenaValidationReport report = ArenaValidator.Validate(data);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.BallCanEscape));
            Assert.IsTrue(report.Contains(ArenaIssueCodes.PlayerCanEscape));
            StringAssert.Contains("left", report.Issues.First(i => i.Code == ArenaIssueCodes.BallCanEscape).Message);
            Assert.IsFalse(report.CanUseInGameFlow);
        }

        [Test]
        public void WalledOffGoalIsDetected()
        {
            ArenaData data = Starter();
            Add(data, ArenaObjectType.Wall, -7.6f, -3f, 0.25f, 3.6f);

            ArenaValidationReport report = ArenaValidator.Validate(data);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.GoalUnreachable, First(data, ArenaObjectType.BlueGoal).id));
            Assert.IsFalse(report.CanUseInGameFlow);
        }

        [Test]
        public void DamagedValuesAreReported()
        {
            ArenaData nan = Starter();
            ArenaObjectData wall = First(nan, ArenaObjectType.Platform);
            wall.x = float.NaN;
            ArenaValidationReport report = ArenaValidator.Validate(nan);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.InvalidNumber, wall.id));
            Assert.IsFalse(report.CanPlaytest);

            ArenaData unknown = Starter();
            var weird = new ArenaObjectData { id = unknown.AllocateId(), type = "Trampoline", x = 0f, y = 2f, width = 1f, height = 1f };
            weird.OnAfterDeserialize();
            unknown.objects.Add(weird);
            report = ArenaValidator.Validate(unknown);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.UnknownType, weird.id));

            ArenaData tiny = Starter();
            ArenaObjectData platform = First(tiny, ArenaObjectType.Platform);
            platform.width = 0f;
            report = ArenaValidator.Validate(tiny);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.InvalidSize, platform.id));

            ArenaData far = Starter();
            ArenaObjectData lost = First(far, ArenaObjectType.SlowZone);
            lost.x = 30f;
            report = ArenaValidator.Validate(far);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.ObjectOutOfBounds, lost.id));

            Assert.IsTrue(ArenaValidator.Validate(null).Contains(ArenaIssueCodes.NoData));
        }

        [Test]
        public void IssuesAreSortedBySeverityAndExplainTheFix()
        {
            ArenaData data = ArenaFactory.CreateEmpty();
            data.objects.Remove(First(data, ArenaObjectType.Ball));
            data.name = "";

            ArenaValidationReport report = ArenaValidator.Validate(data);
            Assert.AreEqual(ArenaIssueSeverity.Blocker, report.Issues[0].Severity);
            Assert.AreEqual(ArenaIssueSeverity.Warning, report.Issues[report.Issues.Count - 1].Severity);
            foreach (ArenaIssue issue in report.Issues)
                Assert.IsFalse(string.IsNullOrEmpty(issue.Message));
        }
    }
}
