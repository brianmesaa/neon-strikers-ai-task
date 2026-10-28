using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NeonStrikers.Tests
{
    /// <summary>
    /// Static integration checks of the scene series (Main Menu -> Arena_01..03 -> Results).
    /// Scenes are opened additively and closed again, so the user's open scene is untouched.
    /// Gameplay scripts live in Assembly-CSharp, so they are looked up by name.
    /// </summary>
    public class SceneWiringTests
    {
        const string MainMenuPath = "Assets/Scenes/Main Menu.unity";
        const string ResultsPath = "Assets/Scenes/Results.unity";
        static readonly string[] ArenaPaths =
        {
            "Assets/Scenes/Arena_01.unity",
            "Assets/Scenes/Arena_02.unity",
            "Assets/Scenes/Arena_03.unity"
        };

        readonly List<Scene> openedScenes = new List<Scene>();

        [TearDown]
        public void CloseScenes()
        {
            foreach (Scene scene in openedScenes)
            {
                if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(scene, true);
            }

            openedScenes.Clear();
        }

        // ---------------------------------------------------------------- Main Menu

        [Test]
        public void MainMenuOffersTwoPlayersAndVsComputer()
        {
            Scene scene = Open(MainMenuPath);

            Button twoPlayers = FindComponent<Button>(scene, "TwoPlayersButton");
            Button vsComputer = FindComponent<Button>(scene, "VsComputerButton");

            AssertButtonCalls(twoPlayers, "SceneLoader", "StartTwoPlayers");
            AssertButtonCalls(vsComputer, "SceneLoader", "StartVsComputer");

            StringAssert.Contains("TWO PLAYERS", twoPlayers.GetComponentInChildren<TMP_Text>(true).text);
            StringAssert.Contains("VS COMPUTER", vsComputer.GetComponentInChildren<TMP_Text>(true).text);
            Assert.IsTrue(twoPlayers.gameObject.activeSelf && vsComputer.gameObject.activeSelf);
            Assert.IsTrue(twoPlayers.interactable && vsComputer.interactable);
        }

        [Test]
        public void MainMenuHasNoModelessPlayButtonLeft()
        {
            Scene scene = Open(MainMenuPath);

            foreach (Button button in AllComponents<Button>(scene))
            {
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                {
                    Assert.AreNotEqual("LoadGame", button.onClick.GetPersistentMethodName(i),
                        button.name + " starts a game without choosing a mode");
                }
            }

            AssertButtonCalls(FindComponent<Button>(scene, "QuitButton"), "SceneLoader", "QuitGame");
        }

        // ---------------------------------------------------------------- Results

        [Test]
        public void ResultsPlayAgainRestartsAndMainMenuReturns()
        {
            Scene scene = Open(ResultsPath);

            // LoadGame restarts Arena_01 keeping the mode; LoadMainMenu resets the mode.
            AssertButtonCalls(FindComponent<Button>(scene, "PlayAgainButton"), "SceneLoader", "LoadGame");
            AssertButtonCalls(FindComponent<Button>(scene, "MainMenuButton"), "SceneLoader", "LoadMainMenu");
        }

        // ---------------------------------------------------------------- Arenas

        [TestCaseSource(nameof(ArenaPaths))]
        public void ArenaKeepsHumanControlsForBothPlayersInTheScene(string path)
        {
            Scene scene = Open(path);
            SerializedObject manager = GameManagerOf(scene);

            Transform blue = (Transform)manager.FindProperty("player1").objectReferenceValue;
            Transform red = (Transform)manager.FindProperty("player2").objectReferenceValue;
            Assert.IsNotNull(blue, "GameManager.player1 (Blue) is not assigned");
            Assert.IsNotNull(red, "GameManager.player2 (Red) is not assigned");

            AssertKeys(blue, KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D, KeyCode.Space);
            AssertKeys(red, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.Return);
        }

        [TestCaseSource(nameof(ArenaPaths))]
        public void ArenaPlayersShareTheSameMovementAndPulseSettings(string path)
        {
            Scene scene = Open(path);
            SerializedObject manager = GameManagerOf(scene);
            var blue = new SerializedObject(Movement((Transform)manager.FindProperty("player1").objectReferenceValue));
            var red = new SerializedObject(Movement((Transform)manager.FindProperty("player2").objectReferenceValue));

            foreach (string field in new[] { "moveSpeed", "liftForce", "directionalLiftForce", "pulseRadius",
                         "pulseForce", "softPossessionRadius", "softPossessionForce" })
            {
                Assert.AreEqual(blue.FindProperty(field).floatValue, red.FindProperty(field).floatValue, 1e-6f,
                    path + ": " + field + " differs between Blue and Red");
            }
        }

        [TestCaseSource(nameof(ArenaPaths))]
        public void ArenaHasNoSecondKickingSystemOrSceneBakedCpu(string path)
        {
            Scene scene = Open(path);
            SerializedObject manager = GameManagerOf(scene);

            foreach (string player in new[] { "player1", "player2" })
            {
                Transform t = (Transform)manager.FindProperty(player).objectReferenceValue;

                var kick = t.GetComponent("PlayerKick") as Behaviour;
                if (kick != null)
                    Assert.IsFalse(kick.enabled, path + ": PlayerKick must stay disabled (pulse is the only kick)");

                Assert.IsNull(t.GetComponent("CpuPlayerController"),
                    path + ": the CPU controller is added at runtime only in Vs Computer mode");
            }
        }

        [TestCaseSource(nameof(ArenaPaths))]
        public void ArenaHasBothGoalsAndAnEscapeToMenuLoader(string path)
        {
            Scene scene = Open(path);

            var teams = AllComponents<MonoBehaviour>(scene)
                .Where(c => c != null && c.GetType().Name == "GoalZone")
                .Select(c => new SerializedObject(c).FindProperty("scoringTeam").stringValue)
                .ToList();

            CollectionAssert.Contains(teams, "Blue", path + ": missing the goal Red defends");
            CollectionAssert.Contains(teams, "Red", path + ": missing the goal Red attacks");

            Assert.IsTrue(AllComponents<MonoBehaviour>(scene).Any(c => c != null && c.GetType().Name == "SceneLoader"));
        }

        [Test]
        public void BuildSettingsContainTheWholeSeries()
        {
            var enabled = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();

            CollectionAssert.Contains(enabled, MainMenuPath);
            foreach (string arena in ArenaPaths)
                CollectionAssert.Contains(enabled, arena);
            CollectionAssert.Contains(enabled, ResultsPath);
        }

        // ---------------------------------------------------------------- code wiring

        [Test]
        public void GameplayTypesExposeTheModeHooks()
        {
            Type sceneLoader = FindGameplayType("SceneLoader");
            foreach (string method in new[] { "StartTwoPlayers", "StartVsComputer", "LoadGame", "LoadMainMenu" })
                Assert.IsNotNull(sceneLoader.GetMethod(method, BindingFlags.Public | BindingFlags.Instance), "SceneLoader." + method);

            Type movement = FindGameplayType("PlayerMovement");
            Assert.IsNotNull(movement.GetMethod("SetExternalInputEnabled"));
            Assert.IsNotNull(movement.GetMethod("SetExternalInput"));

            Type cpu = FindGameplayType("CpuPlayerController");
            var order = cpu.GetCustomAttribute<DefaultExecutionOrder>();
            Assert.IsNotNull(order, "CPU must decide before PlayerMovement reads input");
            Assert.Less(order.order, 0);
        }

        // ---------------------------------------------------------------- helpers

        Scene Open(string path)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            openedScenes.Add(scene);
            Assert.IsTrue(scene.IsValid(), "could not open " + path);
            return scene;
        }

        static IEnumerable<T> AllComponents<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
        }

        static T FindComponent<T>(Scene scene, string objectName) where T : Component
        {
            T found = AllComponents<T>(scene).FirstOrDefault(c => c.gameObject.name == objectName);
            Assert.IsNotNull(found, $"{scene.name}: no {typeof(T).Name} on '{objectName}'");
            return found;
        }

        static void AssertButtonCalls(Button button, string targetType, string method)
        {
            int count = button.onClick.GetPersistentEventCount();
            Assert.Greater(count, 0, button.name + " has no OnClick handler");

            bool found = false;
            for (int i = 0; i < count; i++)
            {
                UnityEngine.Object target = button.onClick.GetPersistentTarget(i);
                if (target != null && target.GetType().Name == targetType && button.onClick.GetPersistentMethodName(i) == method)
                    found = true;
            }

            Assert.IsTrue(found, $"{button.name} should call {targetType}.{method}");
        }

        static SerializedObject GameManagerOf(Scene scene)
        {
            MonoBehaviour manager = AllComponents<MonoBehaviour>(scene).FirstOrDefault(c => c != null && c.GetType().Name == "GameManager");
            Assert.IsNotNull(manager, scene.name + " has no GameManager");
            return new SerializedObject(manager);
        }

        static Component Movement(Transform player)
        {
            Component movement = player.GetComponent("PlayerMovement");
            Assert.IsNotNull(movement, player.name + " has no PlayerMovement");
            return movement;
        }

        static void AssertKeys(Transform player, KeyCode up, KeyCode down, KeyCode left, KeyCode right, KeyCode action)
        {
            var so = new SerializedObject(Movement(player));
            Assert.AreEqual((int)up, so.FindProperty("upKey").intValue, player.name + " up");
            Assert.AreEqual((int)down, so.FindProperty("downKey").intValue, player.name + " down");
            Assert.AreEqual((int)left, so.FindProperty("leftKey").intValue, player.name + " left");
            Assert.AreEqual((int)right, so.FindProperty("rightKey").intValue, player.name + " right");
            Assert.AreEqual((int)action, so.FindProperty("actionKey").intValue, player.name + " action");
        }

        static Type FindGameplayType(string name)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == "Assembly-CSharp")
                .Select(a => a.GetType(name))
                .FirstOrDefault(t => t != null);

            Assert.IsNotNull(type, name + " not found in Assembly-CSharp");
            return type;
        }
    }
}
