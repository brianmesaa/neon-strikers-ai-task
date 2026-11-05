using System.Collections.Generic;
using System.Linq;
using NeonStrikers.Arenas;
using NUnit.Framework;
using UnityEngine;

namespace NeonStrikers.Tests
{
    /// <summary>Editing model of the arena editor: undo / redo, multi-selection, duplicate / delete and portal links.</summary>
    public class ArenaEditorDocumentTests
    {
        static ArenaEditorDocument StarterDocument()
        {
            return ArenaEditorDocument.CreateNew(true, "Test Arena");
        }

        static List<ArenaObjectData> Portals(ArenaEditorDocument doc)
        {
            return doc.Data.objects.Where(o => o.IsPortal).ToList();
        }

        static ArenaEditorDocument EmptyWithPortalPair(out int a, out int b)
        {
            ArenaEditorDocument doc = ArenaEditorDocument.CreateNew(false);
            List<ArenaObjectData> pair = doc.AddPortalPair(new Vector2(-4f, 2f), new Vector2(4f, 2f), -90f, 90f);
            a = pair[0].id;
            b = pair[1].id;
            return doc;
        }

        [Test]
        public void AddUndoRedo()
        {
            ArenaEditorDocument doc = StarterDocument();
            int before = doc.Data.objects.Count;

            ArenaObjectData wall = doc.Add(ArenaObjectType.Wall, new Vector2(1f, 1f));
            Assert.AreEqual(before + 1, doc.Data.objects.Count);
            Assert.AreEqual(wall.id, doc.PrimarySelection, "a new object is selected");

            Assert.IsTrue(doc.Undo());
            Assert.AreEqual(before, doc.Data.objects.Count);
            Assert.IsNull(doc.Find(wall.id));

            Assert.IsTrue(doc.Redo());
            Assert.AreEqual(before + 1, doc.Data.objects.Count);
            Assert.IsNotNull(doc.Find(wall.id));
            Assert.AreEqual(ArenaObjectType.Wall, doc.Find(wall.id).Kind);
        }

        [Test]
        public void NewEditClearsRedo()
        {
            ArenaEditorDocument doc = StarterDocument();
            doc.Add(ArenaObjectType.Wall, Vector2.zero);
            doc.Undo();
            Assert.IsTrue(doc.CanRedo);

            doc.Add(ArenaObjectType.Platform, Vector2.one);
            Assert.IsFalse(doc.CanRedo);
        }

        [Test]
        public void DuplicatingBothPortalsOfAPairCreatesANewConnectedPairAndKeepsTheOriginals()
        {
            ArenaEditorDocument doc = EmptyWithPortalPair(out int a, out int b);

            List<ArenaObjectData> copies = doc.Duplicate(new[] { a, b }, new Vector2(0.5f, -0.5f));

            Assert.AreEqual(2, copies.Count);
            ArenaObjectData copyA = copies[0];
            ArenaObjectData copyB = copies[1];
            Assert.AreNotEqual(a, copyA.id);
            Assert.AreNotEqual(b, copyB.id);
            Assert.AreEqual(copyB.id, copyA.linkedPortalId, "copies are linked to each other");
            Assert.AreEqual(copyA.id, copyB.linkedPortalId);

            Assert.AreEqual(b, doc.Find(a).linkedPortalId, "originals keep their link");
            Assert.AreEqual(a, doc.Find(b).linkedPortalId);
            Assert.AreEqual(4, Portals(doc).Count);
            CollectionAssert.AreEquivalent(new[] { copyA.id, copyB.id }, doc.Selection, "the copies become the selection");

            Assert.AreEqual(-4f + 0.5f, copyA.x, 1e-5f);
            Assert.AreEqual(2f - 0.5f, copyA.y, 1e-5f);
            Assert.AreEqual(doc.Find(a).rotation, copyA.rotation, 1e-5f);

            ArenaValidationReport report = ArenaValidator.Validate(doc.Data);
            Assert.IsFalse(report.Contains(ArenaIssueCodes.UnpairedPortal));
            Assert.IsFalse(report.Contains(ArenaIssueCodes.PortalLinkBroken));
        }

        [Test]
        public void DuplicatingOnePortalLeavesTheCopyUnpairedWithValidationFeedback()
        {
            ArenaEditorDocument doc = EmptyWithPortalPair(out int a, out int b);

            List<ArenaObjectData> copies = doc.Duplicate(new[] { a }, new Vector2(0.5f, -0.5f));

            Assert.AreEqual(1, copies.Count);
            Assert.AreEqual(0, copies[0].linkedPortalId, "the copy is not connected");
            Assert.AreEqual(b, doc.Find(a).linkedPortalId, "the original pair is untouched");
            Assert.AreEqual(a, doc.Find(b).linkedPortalId);

            ArenaValidationReport report = ArenaValidator.Validate(doc.Data);
            Assert.IsTrue(report.Contains(ArenaIssueCodes.UnpairedPortal, copies[0].id));
            Assert.IsFalse(report.Contains(ArenaIssueCodes.UnpairedPortal, a));
            Assert.IsFalse(report.CanUseInGameFlow);
        }

        [Test]
        public void UndoingADeletionRestoresTheObjectAndItsConnection()
        {
            ArenaEditorDocument doc = EmptyWithPortalPair(out int a, out int b);

            doc.Delete(new[] { a });
            Assert.IsNull(doc.Find(a));
            Assert.AreEqual(0, doc.Find(b).linkedPortalId, "the partner becomes unpaired");
            Assert.IsTrue(ArenaValidator.Validate(doc.Data).Contains(ArenaIssueCodes.UnpairedPortal, b));

            doc.Undo();

            Assert.IsNotNull(doc.Find(a));
            Assert.AreEqual(b, doc.Find(a).linkedPortalId);
            Assert.AreEqual(a, doc.Find(b).linkedPortalId, "the partner's link is restored too");
            Assert.IsFalse(ArenaValidator.Validate(doc.Data).Contains(ArenaIssueCodes.UnpairedPortal));

            doc.Redo();
            Assert.IsNull(doc.Find(a));
            Assert.AreEqual(0, doc.Find(b).linkedPortalId);
        }

        [Test]
        public void MultiSelectionDeleteAndDuplicate()
        {
            ArenaEditorDocument doc = StarterDocument();
            List<int> walls = doc.Data.objects.Where(o => o.Kind == ArenaObjectType.Platform).Select(o => o.id).ToList();
            Assert.AreEqual(3, walls.Count);
            int total = doc.Data.objects.Count;

            doc.SetSelection(walls);
            Assert.AreEqual(3, doc.Selection.Count);

            List<ArenaObjectData> copies = doc.DuplicateSelection(new Vector2(0.5f, -0.5f));
            Assert.AreEqual(3, copies.Count);
            Assert.AreEqual(total + 3, doc.Data.objects.Count);
            CollectionAssert.AreEquivalent(copies.Select(c => c.id), doc.Selection);

            Assert.AreEqual(3, doc.DeleteSelection());
            Assert.AreEqual(total, doc.Data.objects.Count);
            Assert.AreEqual(0, doc.Selection.Count);

            doc.SetSelection(walls);
            doc.DeleteSelection();
            Assert.AreEqual(total - 3, doc.Data.objects.Count);

            doc.Undo();
            Assert.AreEqual(total, doc.Data.objects.Count);
            foreach (int id in walls)
                Assert.IsNotNull(doc.Find(id));
        }

        [Test]
        public void DuplicatingAMixedSelectionKeepsOnlyInternalPortalLinks()
        {
            ArenaEditorDocument doc = StarterDocument();
            List<ArenaObjectData> portals = Portals(doc);
            Assert.AreEqual(2, portals.Count);
            ArenaObjectData wall = doc.Data.objects.First(o => o.Kind == ArenaObjectType.Wall);
            ArenaObjectData ball = doc.Data.FirstOf(ArenaObjectType.Ball);

            List<ArenaObjectData> copies = doc.Duplicate(new[] { portals[0].id, portals[1].id, wall.id, ball.id }, new Vector2(0.5f, -0.5f));
            List<ArenaObjectData> portalCopies = copies.Where(c => c.IsPortal).ToList();

            Assert.AreEqual(portalCopies[1].id, portalCopies[0].linkedPortalId);
            Assert.AreEqual(portalCopies[0].id, portalCopies[1].linkedPortalId);
            Assert.IsTrue(ArenaValidator.Validate(doc.Data).Contains(ArenaIssueCodes.MultipleBalls),
                "a duplicated ball is reported (only one ball allowed)");
        }

        [Test]
        public void DragInteractionIsASingleUndoStepAndCanBeCancelled()
        {
            ArenaEditorDocument doc = StarterDocument();
            ArenaObjectData ball = doc.Data.FirstOf(ArenaObjectType.Ball);
            int id = ball.id;
            Vector2 start = ball.Position;
            int undoBefore = doc.UndoCount;

            doc.BeginInteraction("Move Ball");
            for (int i = 1; i <= 5; i++)
            {
                float step = i * 0.1f;
                doc.UpdateInteraction(d => d.Find(id).x = start.x + step);
            }

            doc.EndInteraction();
            Assert.AreEqual(undoBefore + 1, doc.UndoCount);
            Assert.AreEqual(start.x + 0.5f, doc.Find(id).x, 1e-5f);

            doc.Undo();
            Assert.AreEqual(start.x, doc.Find(id).x, 1e-5f);

            doc.BeginInteraction("Move Ball");
            doc.UpdateInteraction(d => d.Find(id).x = 3f);
            doc.CancelInteraction();
            Assert.AreEqual(start.x, doc.Find(id).x, 1e-5f);
            Assert.AreEqual(undoBefore, doc.UndoCount);
        }

        [Test]
        public void TypingIntoAFieldMergesIntoOneUndoStep()
        {
            ArenaEditorDocument doc = StarterDocument();
            ArenaObjectData wall = doc.Data.objects.First(o => o.Kind == ArenaObjectType.Wall);
            int id = wall.id;
            float original = wall.width;
            int undoBefore = doc.UndoCount;

            doc.Modify("width", d => d.Find(id).width = 1f, "field:" + id + ":w");
            doc.Modify("width", d => d.Find(id).width = 12f, "field:" + id + ":w");
            doc.Modify("width", d => d.Find(id).width = 12.5f, "field:" + id + ":w");

            Assert.AreEqual(undoBefore + 1, doc.UndoCount);
            doc.Undo();
            Assert.AreEqual(original, doc.Find(id).width, 1e-5f);
        }

        [Test]
        public void DirtyStateFollowsSaveAndUndo()
        {
            ArenaEditorDocument doc = StarterDocument();
            Assert.IsTrue(doc.IsDirty, "a new arena is unsaved");
            Assert.IsTrue(doc.IsNew);

            ArenaData saved = doc.Data.Clone();
            saved.id = "abc123";
            doc.MarkSaved(saved);
            Assert.IsFalse(doc.IsDirty);
            Assert.IsFalse(doc.IsNew);

            doc.Add(ArenaObjectType.Wall, Vector2.zero);
            Assert.IsTrue(doc.IsDirty);

            doc.Undo();
            Assert.IsFalse(doc.IsDirty, "undoing back to the saved state is clean again");
            Assert.AreEqual("abc123", doc.Data.id, "undo never forgets the file identity");
        }

        [Test]
        public void LinkingReplacesPreviousPartnersAndUnlinkIsMutual()
        {
            ArenaEditorDocument doc = EmptyWithPortalPair(out int a, out int b);
            ArenaObjectData single = doc.Add(ArenaObjectType.Portal, new Vector2(0f, 3f));
            int c = single.id;

            Assert.IsTrue(doc.Link(a, c));
            Assert.AreEqual(c, doc.Find(a).linkedPortalId);
            Assert.AreEqual(a, doc.Find(c).linkedPortalId);
            Assert.AreEqual(0, doc.Find(b).linkedPortalId, "the old partner is released");

            Assert.IsTrue(doc.Unlink(c));
            Assert.AreEqual(0, doc.Find(a).linkedPortalId);
            Assert.AreEqual(0, doc.Find(c).linkedPortalId);

            doc.Undo();
            Assert.AreEqual(c, doc.Find(a).linkedPortalId);
            doc.Undo();
            Assert.AreEqual(b, doc.Find(a).linkedPortalId);
            Assert.AreEqual(a, doc.Find(b).linkedPortalId);
            Assert.AreEqual(0, doc.Find(c).linkedPortalId);

            Assert.IsFalse(doc.Link(a, a), "a portal can't link to itself");
            Assert.IsFalse(doc.Link(a, doc.Data.FirstOf(ArenaObjectType.Ball).id), "only portals can be linked");
        }

        [Test]
        public void PlacingTheBallOrASpawnMovesTheExistingOne()
        {
            ArenaEditorDocument doc = StarterDocument();
            int balls = doc.Data.Count(ArenaObjectType.Ball);
            int ballId = doc.Data.FirstOf(ArenaObjectType.Ball).id;

            ArenaObjectData placed = doc.Place(ArenaObjectType.Ball, new Vector2(2f, 2f));

            Assert.AreEqual(balls, doc.Data.Count(ArenaObjectType.Ball));
            Assert.AreEqual(ballId, placed.id);
            Assert.AreEqual(new Vector2(2f, 2f), doc.Find(ballId).Position);
        }

        [Test]
        public void SwapGoalTeamAndRotate()
        {
            ArenaEditorDocument doc = StarterDocument();
            ArenaObjectData goal = doc.Data.FirstOf(ArenaObjectType.BlueGoal);
            int id = goal.id;

            doc.SwapGoalTeam(id);
            Assert.AreEqual(ArenaObjectType.RedGoal, doc.Find(id).Kind);
            Assert.AreEqual("RedGoal", doc.Find(id).type);

            doc.RotateBy(new[] { id }, 200f);
            Assert.AreEqual(-160f, doc.Find(id).rotation, 1e-4f);

            doc.Undo();
            doc.Undo();
            Assert.AreEqual(ArenaObjectType.BlueGoal, doc.Find(id).Kind);
        }

        [Test]
        public void ForgetFileMakesTheArenaNewAndUnsaved()
        {
            ArenaEditorDocument doc = StarterDocument();
            ArenaData saved = doc.Data.Clone();
            saved.id = "file1";
            doc.MarkSaved(saved);

            doc.ForgetFile();

            Assert.IsTrue(doc.IsNew);
            Assert.IsTrue(doc.IsDirty);
        }
    }
}
