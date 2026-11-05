using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NeonStrikers.Arenas;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace NeonStrikers.Tests
{
    /// <summary>
    /// Runs the real scenes: custom arenas are built from the stock Arena_01 objects with the same gameplay
    /// values, they play like the stock arenas, the 4th-arena game flow works in both modes, and playtesting
    /// returns to the editor without losing anything. Gameplay scripts live in Assembly-CSharp, so they are
    /// accessed by name / reflection. Arena files go to a temporary folder.
    /// </summary>
    public class CustomArenaPlayModeTests
    {
        string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "NeonStrikersPlayMode_" + Guid.NewGuid().ToString("N"));
            ArenaStorage.OverrideDefaultRoot(root);
            CustomArenaSession.Reset();
            GameModeSettings.ResetToDefault();
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            CustomArenaSession.Reset();
            ArenaStorage.OverrideDefaultRoot(null);
            GameModeSettings.ResetToDefault();
            Time.timeScale = 1f;
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        // ---------------------------------------------------------------- building

        [UnityTest]
        public IEnumerator PlaytestBuildsEveryObjectFromTheStockTemplates()
        {
            ArenaData arena = ArenaFactory.CreateStarter("PlayMode Arena");
            CustomArenaSession.BeginPlaytest(arena);
            GameModeSettings.Select(GameMode.TwoPlayers);
            yield return Load(CustomArenaFlow.CustomArenaScene);

            Scene scene = SceneManager.GetActiveScene();
            GameObject layout = Root(scene, "CustomArenaLayout");
            Assert.IsNotNull(layout, "the custom layout was not built");

            foreach (string stock in new[] { "Walls", "ArenaPlatforms", "Portals", "Goals", "WhiteGrass" })
                Assert.IsFalse(Root(scene, stock).activeSelf, stock + " (stock Arena_01 layout) must be hidden");

            Assert.AreEqual(arena.Count(ArenaObjectType.Wall), Children(layout, "Wall #").Count);
            Assert.AreEqual(arena.Count(ArenaObjectType.Platform), Children(layout, "Platform #").Count);
            Assert.AreEqual(arena.Count(ArenaObjectType.Portal), Children(layout, "Portal #").Count);
            Assert.AreEqual(arena.Count(ArenaObjectType.SlowZone), Children(layout, "Slow Zone #").Count);

            // Walls: solid, same layer as the stock walls, outline collider kept.
            Transform stockWall = FindPath(scene, "Walls/BottomLeftWall");
            foreach (GameObject wall in Children(layout, "Wall #"))
            {
                Assert.AreEqual(stockWall.gameObject.layer, wall.layer);
                Assert.IsTrue(wall.GetComponentsInChildren<BoxCollider2D>().All(c => !c.isTrigger));
                Assert.GreaterOrEqual(wall.GetComponentsInChildren<BoxCollider2D>().Length, 2);
            }

            // Portals: stock PortalTeleporter values, connected to the partner's exit.
            Component stockPortal = FindPath(scene, "Portals/LeftPortal").GetComponent("PortalTeleporter");
            foreach (ArenaObjectData portal in arena.objects.Where(o => o.IsPortal))
            {
                GameObject built = layout.transform.Find("Portal #" + portal.id).gameObject;
                Component teleporter = built.GetComponent("PortalTeleporter");
                foreach (string field in new[] { "teleportCooldown", "exitSpeedMultiplier", "playerSpeedMultiplier", "minExitSpeed", "exitOffset" })
                    Assert.AreEqual(Field<float>(stockPortal, field), Field<float>(teleporter, field), 1e-6f, "portal " + field);

                var exit = Field<Transform>(teleporter, "exitPoint");
                Assert.IsNotNull(exit, "linked portals have an exit point");
                Assert.AreEqual("Portal #" + portal.linkedPortalId, exit.parent.name, "the exit belongs to the partner");
                Assert.IsTrue(built.GetComponent<BoxCollider2D>().isTrigger);
            }

            // Goals: GoalZone teams like the stock goals.
            ArenaObjectData blueGoal = arena.FirstOf(ArenaObjectType.BlueGoal);
            ArenaObjectData redGoal = arena.FirstOf(ArenaObjectType.RedGoal);
            GameObject blueGoalObject = layout.transform.Find("Blue Goal #" + blueGoal.id).gameObject;
            GameObject redGoalObject = layout.transform.Find("Red Goal #" + redGoal.id).gameObject;
            Assert.AreEqual("Red", Field<string>(blueGoalObject.GetComponent("GoalZone"), "scoringTeam"));
            Assert.AreEqual("Blue", Field<string>(redGoalObject.GetComponent("GoalZone"), "scoringTeam"));
            Assert.AreEqual(blueGoal.width, blueGoalObject.GetComponent<BoxCollider2D>().bounds.size.x, 0.01f);
            Assert.AreEqual(blueGoal.height, blueGoalObject.GetComponent<BoxCollider2D>().bounds.size.y, 0.01f);

            // Slow zones: stock LocalSlowZone values, circle of the chosen size.
            Component stockSlow = FindPath(scene, "Goals/LeftGoalSlowZone").GetComponent("LocalSlowZone");
            foreach (GameObject zone in Children(layout, "Slow Zone #"))
            {
                Component slow = zone.GetComponent("LocalSlowZone");
                Assert.AreEqual(Field<float>(stockSlow, "ballSlowMultiplier"), Field<float>(slow, "ballSlowMultiplier"), 1e-6f);
                Assert.AreEqual(Field<float>(stockSlow, "playerSlowMultiplier"), Field<float>(slow, "playerSlowMultiplier"), 1e-6f);
                Assert.AreEqual(1.6f, zone.GetComponent<CircleCollider2D>().radius, 1e-4f);
                Assert.IsTrue(zone.GetComponent<CircleCollider2D>().isTrigger);
            }

            // Spawns and GameManager references.
            Component manager = GameManager();
            Assert.AreEqual((Vector2)arena.FirstOf(ArenaObjectType.Ball).Position, (Vector2)Field<Transform>(manager, "ballSpawnPoint").position);
            Assert.AreEqual((Vector2)arena.FirstOf(ArenaObjectType.BlueSpawn).Position, (Vector2)Field<Transform>(manager, "player1SpawnPoint").position);
            Assert.AreEqual((Vector2)arena.FirstOf(ArenaObjectType.RedSpawn).Position, (Vector2)Field<Transform>(manager, "player2SpawnPoint").position);
            Assert.IsTrue(Field<ParticleSystem>(manager, "leftGoalBurst").transform.IsChildOf(blueGoalObject.transform));
            Assert.IsTrue(Field<ParticleSystem>(manager, "rightGoalBurst").transform.IsChildOf(redGoalObject.transform));
            Assert.IsNotNull(manager.GetComponent("PlaytestOverlay"), "playtest shows the back-to-editor bar");
        }

        // ---------------------------------------------------------------- behaviour

        [UnityTest]
        public IEnumerator BallInACustomGoalScoresForTheRightTeam()
        {
            ArenaData arena = ArenaFactory.CreateStarter("Goals");
            CustomArenaSession.BeginPlaytest(arena);
            yield return Load(CustomArenaFlow.CustomArenaScene);
            yield return WaitForKickOff();

            Component manager = GameManager();
            Assert.AreEqual(3, Field<int>(manager, "redLives"));

            Rigidbody2D ball = Ball(manager);
            ball.position = arena.FirstOf(ArenaObjectType.RedGoal).Position;
            ball.linearVelocity = Vector2.zero;
            yield return FixedSteps(4);

            Assert.AreEqual(2, Field<int>(manager, "redLives"), "Blue scores in the Red goal");
            Assert.AreEqual(3, Field<int>(manager, "blueLives"));
        }

        [UnityTest]
        public IEnumerator LinkedPortalsTeleportTheBallToTheirPartner()
        {
            ArenaData arena = ArenaFactory.CreateStarter("Portals");
            CustomArenaSession.BeginPlaytest(arena);
            yield return Load(CustomArenaFlow.CustomArenaScene);
            yield return WaitForKickOff();

            ArenaObjectData left = arena.objects.First(o => o.IsPortal && o.x < 0f);
            ArenaObjectData right = arena.Find(left.linkedPortalId);
            Rigidbody2D ball = Ball(GameManager());
            ball.position = left.Position;
            ball.linearVelocity = new Vector2(-2f, 0f);
            yield return FixedSteps(3);

            Vector2 landing = ArenaGeometry.PortalLandingPoint(right);
            Assert.Less(Vector2.Distance(ball.position, landing), 1.5f, "the ball comes out of the partner portal");
            Assert.Less(ball.linearVelocity.x, 0f, "it leaves along the partner's arrow");
            Assert.GreaterOrEqual(ball.linearVelocity.magnitude, 5f * 1.1f - 0.6f, "stock minimum exit speed x exit multiplier");
        }

        [UnityTest]
        public IEnumerator CustomSlowZoneSlowsTimeLikeTheStockArena()
        {
            // Stock Arena_01: the ball resting in a goal slow zone.
            yield return Load("Arena_01");
            yield return WaitForKickOff();
            Scene stock = SceneManager.GetActiveScene();
            Vector2 stockCenter = FindPath(stock, "Goals/LeftGoalSlowZone").GetComponent<CircleCollider2D>().bounds.center;
            float stockScale = 0f;
            yield return MeasureTimeScale(stockCenter, value => stockScale = value);

            Time.timeScale = 1f;
            ArenaData arena = ArenaFactory.CreateStarter("Slow");
            CustomArenaSession.BeginPlaytest(arena);
            yield return Load(CustomArenaFlow.CustomArenaScene);
            yield return WaitForKickOff();
            float customScale = 0f;
            yield return MeasureTimeScale(arena.FirstOf(ArenaObjectType.SlowZone).Position, value => customScale = value);

            Assert.Less(stockScale, 0.9f, "stock zones slow time");
            Assert.Greater(customScale, 0.2f, "a custom zone must not freeze the game at 0.01");
            Assert.Less(customScale, 0.9f, "a custom zone slows time");
            Assert.AreEqual(stockScale, customScale, 0.2f, "same slow motion as in the stock arena");

            // Leaving the zone brings time back to normal.
            Rigidbody2D ball = Ball(GameManager());
            ball.position = new Vector2(0f, 2f);
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.Greater(Time.timeScale, 0.95f);
        }

        // ---------------------------------------------------------------- game flow

        [UnityTest]
        public IEnumerator FourthArenaIsPlayedAfterArena03InVsComputerThenResults()
        {
            ArenaData arena = ArenaStorage.Default.Save(ArenaFactory.CreateStarter("Fourth"));
            ArenaStorage.Default.SetGameFlowArena(arena.id);
            GameModeSettings.Select(GameMode.VsComputer);

            yield return Load("Arena_03");
            SetMatchIndex(2);
            InvokeGameManager("LoadNextArenaOrResults");
            yield return WaitForScene(CustomArenaFlow.CustomArenaScene);

            Assert.AreEqual(3, MatchIndex());
            Assert.IsNotNull(Root(SceneManager.GetActiveScene(), "CustomArenaLayout"));
            Assert.AreEqual(GameMode.VsComputer, GameModeSettings.Current, "the mode is kept");

            Transform red = Field<Transform>(GameManager(), "player2");
            var cpu = red.GetComponent("CpuPlayerController") as Behaviour;
            Assert.IsNotNull(cpu);
            Assert.IsTrue(cpu.enabled, "the CPU plays Red in the custom arena");
            yield return null;
            var ownGoal = (Rect)cpu.GetType().GetProperty("OwnGoalRect").GetValue(cpu);
            var targetGoal = (Rect)cpu.GetType().GetProperty("TargetGoalRect").GetValue(cpu);
            Assert.Greater(ownGoal.center.x, 0f, "the CPU defends the custom Red goal");
            Assert.Less(targetGoal.center.x, 0f, "the CPU attacks the custom Blue goal");

            InvokeGameManager("LoadNextArenaOrResults");
            yield return WaitForScene(CustomArenaFlow.ResultsScene);
        }

        [UnityTest]
        public IEnumerator WithoutAFourthArenaTheOriginalFlowEndsAfterArena03()
        {
            yield return Load("Arena_03");
            SetMatchIndex(2);
            InvokeGameManager("LoadNextArenaOrResults");
            yield return WaitForScene(CustomArenaFlow.ResultsScene);
        }

        // ---------------------------------------------------------------- editor

        [UnityTest]
        public IEnumerator MainMenuButtonOpensTheEditor()
        {
            yield return Load(CustomArenaFlow.MainMenuScene);
            Button button = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Button>(true))
                .First(b => b.name == "ArenaEditorButton");
            button.onClick.Invoke();
            yield return WaitForScene(CustomArenaFlow.EditorScene);
            Assert.IsNotNull(Editor());
        }

        [UnityTest]
        public IEnumerator PlaytestAndBackKeepsTheArenaAndItsHistory()
        {
            yield return Load(CustomArenaFlow.EditorScene);
            ArenaEditorDocument doc = Document(Editor());
            ArenaObjectData wall = doc.Add(ArenaObjectType.Wall, new Vector2(0f, 2.5f));
            int wallId = wall.id;
            int count = doc.Data.objects.Count;
            int undo = doc.UndoCount;

            Invoke(Editor(), "StartPlaytest");
            yield return WaitForScene(CustomArenaFlow.CustomArenaScene);

            Assert.IsTrue(CustomArenaSession.IsPlaytesting);
            Assert.AreEqual(GameMode.TwoPlayers, GameModeSettings.Current, "playtest uses Two Players");
            Assert.IsNotNull(Root(SceneManager.GetActiveScene(), "CustomArenaLayout").transform.Find("Wall #" + wallId),
                "the unsaved layout is the one being played");

            Component loader = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
                .First(c => c.GetType().Name == "SceneLoader");
            loader.GetType().GetMethod("ReturnToArenaEditor").Invoke(loader, null);
            yield return WaitForScene(CustomArenaFlow.EditorScene);

            Assert.IsFalse(CustomArenaSession.IsPlaytesting);
            ArenaEditorDocument back = Document(Editor());
            Assert.AreSame(doc, back, "the same document (with its undo history) is still open");
            Assert.AreEqual(count, back.Data.objects.Count);
            Assert.IsNotNull(back.Find(wallId));
            Assert.AreEqual(undo, back.UndoCount);
            Assert.IsTrue(back.Undo());
            Assert.IsNull(back.Find(wallId));
        }

        [UnityTest]
        public IEnumerator EditorExplainsProblemsAndAssignsTheFourthArena()
        {
            yield return Load(CustomArenaFlow.EditorScene);
            Component editor = Editor();
            ArenaEditorDocument doc = Document(editor);

            doc.Delete(new[] { doc.Data.FirstOf(ArenaObjectType.Ball).id });
            Invoke(editor, "Playtest");
            yield return null;
            Assert.AreEqual(CustomArenaFlow.EditorScene, SceneManager.GetActiveScene().name, "no playtest without a ball");
            Assert.IsTrue((bool)editor.GetType().GetProperty("HasOpenDialog").GetValue(editor), "the problem is explained");

            Assert.IsFalse((bool)Invoke(editor, "UseCurrentInGameFlow"));
            Assert.AreEqual("", ArenaStorage.Default.GetGameFlowArenaId());

            doc.Undo();
            Assert.IsTrue((bool)Invoke(editor, "UseCurrentInGameFlow"));
            string firstId = doc.Data.id;
            Assert.AreEqual(firstId, ArenaStorage.Default.GetGameFlowArenaId());
            Assert.IsFalse(doc.IsDirty, "using it saves it");

            editor.GetType().GetMethod("NewArena").Invoke(editor, new object[] { true });
            ArenaEditorDocument second = Document(editor);
            Assert.AreNotSame(doc, second);
            Assert.IsTrue((bool)Invoke(editor, "UseCurrentInGameFlow"));
            Assert.AreEqual(second.Data.id, ArenaStorage.Default.GetGameFlowArenaId(), "the new arena replaces the 4th arena");
            Assert.IsTrue(ArenaStorage.Default.Exists(firstId), "the replaced arena is still stored");
            Assert.AreEqual(2, ArenaStorage.Default.ListArenas().Count);
        }

        // ---------------------------------------------------------------- helpers

        static IEnumerator Load(string scene)
        {
            SceneManager.LoadScene(scene);
            yield return WaitForScene(scene);
        }

        static IEnumerator WaitForScene(string scene)
        {
            float timeout = Time.realtimeSinceStartup + 10f;
            while (SceneManager.GetActiveScene().name != scene && Time.realtimeSinceStartup < timeout)
                yield return null;

            Assert.AreEqual(scene, SceneManager.GetActiveScene().name);
            yield return null;
            yield return null;
        }

        static IEnumerator WaitForKickOff()
        {
            // Countdown 3, 2, 1, GO! = 3.7 s of game time.
            Time.timeScale = 1f;
            yield return new WaitForSeconds(3.9f);
        }

        static IEnumerator FixedSteps(int count)
        {
            for (int i = 0; i < count; i++)
                yield return new WaitForFixedUpdate();
        }

        IEnumerator MeasureTimeScale(Vector2 zoneCenter, Action<float> result)
        {
            Rigidbody2D ball = Ball(GameManager());
            ball.gravityScale = 0f;
            ball.position = zoneCenter;
            ball.linearVelocity = Vector2.zero;
            yield return new WaitForSecondsRealtime(1.5f);
            result(Time.timeScale);
        }

        static GameObject Root(Scene scene, string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
        }

        static List<GameObject> Children(GameObject parent, string prefix)
        {
            var list = new List<GameObject>();
            foreach (Transform child in parent.transform)
            {
                if (child.name.StartsWith(prefix))
                    list.Add(child.gameObject);
            }

            return list;
        }

        static Transform FindPath(Scene scene, string path)
        {
            string[] parts = path.Split('/');
            GameObject root = Root(scene, parts[0]);
            Assert.IsNotNull(root, path);
            return root.transform.Find(string.Join("/", parts.Skip(1)));
        }

        static Component GameManager()
        {
            Component manager = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
                .FirstOrDefault(c => c.GetType().Name == "GameManager");
            Assert.IsNotNull(manager, "GameManager");
            return manager;
        }

        static Rigidbody2D Ball(Component manager)
        {
            var ball = (Component)manager.GetType().GetField("ball").GetValue(manager);
            return ball.GetComponent<Rigidbody2D>();
        }

        static Component Editor()
        {
            Component editor = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
                .FirstOrDefault(c => c.GetType().Name == "ArenaEditorController");
            Assert.IsNotNull(editor, "ArenaEditorController");
            return editor;
        }

        static ArenaEditorDocument Document(Component editor)
        {
            var doc = (ArenaEditorDocument)editor.GetType().GetProperty("Document").GetValue(editor);
            Assert.IsNotNull(doc);
            return doc;
        }

        static object Invoke(Component target, string method)
        {
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            Assert.IsNotNull(info, target.GetType().Name + "." + method);
            return info.Invoke(target, null);
        }

        static void InvokeGameManager(string method)
        {
            Component manager = GameManager();
            MethodInfo info = manager.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(info, "GameManager." + method);
            info.Invoke(manager, null);
        }

        static Type MatchDataType()
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == "Assembly-CSharp")
                .Select(a => a.GetType("MatchData"))
                .FirstOrDefault(t => t != null);
            Assert.IsNotNull(type, "MatchData");
            return type;
        }

        static void SetMatchIndex(int index)
        {
            MatchDataType().GetField("currentArenaIndex").SetValue(null, index);
        }

        static int MatchIndex()
        {
            return (int)MatchDataType().GetField("currentArenaIndex").GetValue(null);
        }

        static T Field<T>(Component component, string name)
        {
            FieldInfo field = component.GetType().GetField(name);
            Assert.IsNotNull(field, component.GetType().Name + "." + name);
            return (T)field.GetValue(component);
        }
    }
}
