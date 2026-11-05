using System.IO;
using System.Linq;
using NeonStrikers.Arenas;
using NUnit.Framework;
using UnityEngine;

namespace NeonStrikers.Tests
{
    /// <summary>Local storage of custom arenas, broken file detection and the 4th-arena game flow.</summary>
    public class ArenaStorageTests
    {
        string root;
        ArenaStorage storage;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "NeonStrikersArenaTests_" + System.Guid.NewGuid().ToString("N"));
            storage = new ArenaStorage(root);
            CustomArenaSession.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            CustomArenaSession.Reset();
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void SaveAndLoadRoundTripKeepsEverything()
        {
            ArenaData arena = ArenaFactory.CreateStarter("Round Trip");
            ArenaData stored = storage.Save(arena);

            Assert.IsTrue(ArenaStorage.IsValidId(stored.id));
            Assert.AreEqual(stored.id, arena.id, "the id is written back into the saved arena");
            Assert.IsTrue(File.Exists(storage.PathFor(stored.id)));

            Assert.IsTrue(storage.TryLoad(stored.id, out ArenaData loaded, out string error), error);
            Assert.AreEqual("Round Trip", loaded.name);
            Assert.AreEqual(arena.objects.Count, loaded.objects.Count);

            foreach (ArenaObjectData original in arena.objects)
            {
                ArenaObjectData copy = loaded.Find(original.id);
                Assert.IsNotNull(copy);
                Assert.AreEqual(original.Kind, copy.Kind);
                Assert.AreEqual(original.x, copy.x, 1e-5f);
                Assert.AreEqual(original.y, copy.y, 1e-5f);
                Assert.AreEqual(original.width, copy.width, 1e-5f);
                Assert.AreEqual(original.height, copy.height, 1e-5f);
                Assert.AreEqual(original.rotation, copy.rotation, 1e-5f);
                Assert.AreEqual(original.linkedPortalId, copy.linkedPortalId);
            }

            string json = File.ReadAllText(storage.PathFor(stored.id));
            StringAssert.Contains("\"type\": \"Portal\"", json, "types are stored as readable names");
            Assert.IsTrue(ArenaValidator.Validate(loaded).CanUseInGameFlow);
        }

        [Test]
        public void ArenasRemainAvailableForANewStorageInstance()
        {
            ArenaData a = storage.Save(ArenaFactory.CreateStarter("First"));
            ArenaData b = storage.Save(ArenaFactory.CreateEmpty("Second"));

            // A new instance on the same folder is what the game sees after a restart.
            var afterRestart = new ArenaStorage(root);
            var entries = afterRestart.ListArenas();

            Assert.AreEqual(2, entries.Count);
            CollectionAssert.AreEquivalent(new[] { a.id, b.id }, entries.Select(e => e.Id));
            CollectionAssert.AreEquivalent(new[] { "First", "Second" }, entries.Select(e => e.Name));
            Assert.IsTrue(entries.All(e => !e.IsBroken));
        }

        [Test]
        public void SavingAgainUpdatesTheSameFile()
        {
            ArenaData arena = storage.Save(ArenaFactory.CreateStarter("Editable"));
            string id = arena.id;

            arena.objects.RemoveAll(o => o.Kind == ArenaObjectType.Platform);
            arena.name = "Edited";
            storage.Save(arena);

            Assert.AreEqual(1, storage.ListArenas().Count);
            Assert.IsTrue(storage.TryLoad(id, out ArenaData loaded, out _));
            Assert.AreEqual("Edited", loaded.name);
            Assert.AreEqual(0, loaded.Count(ArenaObjectType.Platform));
        }

        [Test]
        public void BrokenFilesAreListedAndExplained()
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "garbage" + ArenaStorage.ArenaFileSuffix), "{ this is not json");
            File.WriteAllText(Path.Combine(root, "empty" + ArenaStorage.ArenaFileSuffix), "");
            File.WriteAllText(Path.Combine(root, "other" + ArenaStorage.ArenaFileSuffix), "{\"hello\": 1}");
            File.WriteAllText(Path.Combine(root, "future" + ArenaStorage.ArenaFileSuffix),
                "{\"formatVersion\": 99, \"name\": \"Future\", \"objects\": []}");
            File.WriteAllText(Path.Combine(root, "dupes" + ArenaStorage.ArenaFileSuffix),
                "{\"formatVersion\": 1, \"name\": \"Dupes\", \"objects\": [{\"id\": 1, \"type\": \"Wall\"}, {\"id\": 1, \"type\": \"Wall\"}]}");
            storage.Save(ArenaFactory.CreateStarter("Good"));

            var entries = storage.ListArenas();
            Assert.AreEqual(6, entries.Count);
            Assert.AreEqual(1, entries.Count(e => !e.IsBroken));

            foreach (var entry in entries.Where(e => e.IsBroken))
            {
                Assert.IsFalse(string.IsNullOrEmpty(entry.Error), entry.Id + " should explain why it is broken");
                Assert.IsNull(entry.Data);
            }

            StringAssert.Contains("newer version", entries.First(e => e.Id == "future").Error);
            StringAssert.Contains("more than once", entries.First(e => e.Id == "dupes").Error);
            Assert.IsFalse(storage.TryLoad("garbage", out _, out string error));
            Assert.IsFalse(string.IsNullOrEmpty(error));
        }

        [Test]
        public void UnknownObjectTypesSurviveLoadingAndAreReported()
        {
            Directory.CreateDirectory(root);
            ArenaData arena = ArenaFactory.CreateStarter("Odd");
            string json = ArenaStorage.ToJson(arena).Replace("\"type\": \"SlowZone\"", "\"type\": \"Trampoline\"");
            File.WriteAllText(storage.PathFor("odd"), json);

            Assert.IsTrue(storage.TryLoad("odd", out ArenaData loaded, out string error), error);
            ArenaObjectData unknown = loaded.objects.First(o => o.Kind == ArenaObjectType.Unknown);
            Assert.AreEqual("Trampoline", unknown.type);
            Assert.IsTrue(ArenaValidator.Validate(loaded).Contains(ArenaIssueCodes.UnknownType, unknown.id));
        }

        [Test]
        public void AssigningAnotherFourthArenaReplacesItWithoutDeletingTheFirst()
        {
            ArenaData first = storage.Save(ArenaFactory.CreateStarter("First"));
            ArenaData second = storage.Save(ArenaFactory.CreateStarter("Second"));

            Assert.AreEqual("", storage.GetGameFlowArenaId(), "no 4th arena by default");

            storage.SetGameFlowArena(first.id);
            Assert.AreEqual(first.id, new ArenaStorage(root).GetGameFlowArenaId(), "the choice survives a restart");

            storage.SetGameFlowArena(second.id);
            Assert.AreEqual(second.id, storage.GetGameFlowArenaId());
            Assert.IsTrue(storage.Exists(first.id), "the replaced arena is still stored");
            Assert.AreEqual(2, storage.ListArenas().Count);

            storage.ClearGameFlowArena();
            Assert.AreEqual("", storage.GetGameFlowArenaId());
            Assert.AreEqual(2, storage.ListArenas().Count, "going back to three arenas deletes nothing");
        }

        [Test]
        public void DeletingTheFourthArenaRestoresTheOriginalFlow()
        {
            ArenaData arena = storage.Save(ArenaFactory.CreateStarter("Doomed"));
            storage.SetGameFlowArena(arena.id);

            Assert.IsTrue(storage.Delete(arena.id));
            Assert.IsFalse(storage.Exists(arena.id));
            Assert.AreEqual("", storage.GetGameFlowArenaId());
            Assert.AreEqual(CustomArenaFlow.ResultsScene, CustomArenaFlow.NextSceneAfter(2, storage));
        }

        [Test]
        public void SceneOrderFollowsTheFourthArenaSelection()
        {
            Assert.AreEqual("Arena_01", CustomArenaFlow.SceneForArenaIndex(0, false));
            Assert.AreEqual("Arena_02", CustomArenaFlow.SceneForArenaIndex(1, false));
            Assert.AreEqual("Arena_03", CustomArenaFlow.SceneForArenaIndex(2, false));
            Assert.AreEqual("Results", CustomArenaFlow.SceneForArenaIndex(3, false));
            Assert.AreEqual("CustomArena", CustomArenaFlow.SceneForArenaIndex(3, true));
            Assert.AreEqual("Results", CustomArenaFlow.SceneForArenaIndex(4, true));

            // No custom arena: the original three-arena flow.
            Assert.AreEqual("Arena_02", CustomArenaFlow.NextSceneAfter(0, storage));
            Assert.AreEqual("Arena_03", CustomArenaFlow.NextSceneAfter(1, storage));
            Assert.AreEqual("Results", CustomArenaFlow.NextSceneAfter(2, storage));

            ArenaData arena = storage.Save(ArenaFactory.CreateStarter("Fourth"));
            storage.SetGameFlowArena(arena.id);

            Assert.AreEqual("Arena_02", CustomArenaFlow.NextSceneAfter(0, storage));
            Assert.AreEqual("CustomArena", CustomArenaFlow.NextSceneAfter(2, storage));
            Assert.IsNotNull(CustomArenaSession.FlowArena);
            Assert.AreEqual(arena.id, CustomArenaSession.FlowArena.id);
            Assert.AreEqual(arena.id, CustomArenaSession.ArenaForCustomScene(storage).id);
            Assert.AreEqual("Results", CustomArenaFlow.NextSceneAfter(3, storage));
        }

        [Test]
        public void ABrokenFourthArenaIsSkipped()
        {
            ArenaData invalid = ArenaFactory.CreateEmpty("No goals");
            storage.Save(invalid);
            storage.SetGameFlowArena(invalid.id);

            Assert.IsFalse(CustomArenaFlow.TryGetGameFlowArena(storage, out _, out string reason));
            StringAssert.Contains("problems", reason);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Skipping the 4th arena"));
            Assert.AreEqual("Results", CustomArenaFlow.NextSceneAfter(2, storage));

            File.WriteAllText(storage.PathFor(invalid.id), "corrupted");
            Assert.IsFalse(CustomArenaFlow.TryGetGameFlowArena(storage, out _, out reason));
            StringAssert.Contains("can't be loaded", reason);
        }

        [Test]
        public void PlaytestLayoutWinsOverTheGameFlowArena()
        {
            ArenaData fourth = storage.Save(ArenaFactory.CreateStarter("Fourth"));
            storage.SetGameFlowArena(fourth.id);

            ArenaData draft = ArenaFactory.CreateEmpty("Unsaved draft");
            CustomArenaSession.BeginPlaytest(draft);
            Assert.IsTrue(CustomArenaSession.IsPlaytesting);
            Assert.AreEqual("Unsaved draft", CustomArenaSession.ArenaForCustomScene(storage).name);

            draft.name = "changed after starting";
            Assert.AreEqual("Unsaved draft", CustomArenaSession.ArenaForCustomScene(storage).name, "the playtest uses a snapshot");

            CustomArenaSession.EndPlaytest();
            Assert.AreEqual("Fourth", CustomArenaSession.ArenaForCustomScene(storage).name);
        }
    }
}
