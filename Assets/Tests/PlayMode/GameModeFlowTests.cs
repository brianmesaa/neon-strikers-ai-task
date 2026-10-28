using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NeonStrikers.AI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace NeonStrikers.Tests
{
    /// <summary>
    /// Play Mode integration tests: run the real scenes and check the mode flow and the CPU wiring.
    /// Gameplay scripts live in Assembly-CSharp, so they are accessed by name / reflection.
    /// </summary>
    public class GameModeFlowTests
    {
        [SetUp]
        public void SetUp()
        {
            GameModeSettings.ResetToDefault();
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            GameModeSettings.ResetToDefault();
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator ArenaOpenedDirectlyDefaultsToTwoPlayers()
        {
            yield return Load("Arena_01");

            Assert.AreEqual(GameMode.TwoPlayers, GameModeSettings.Current);
            AssertHumanControlled(Player("player1"));
            AssertHumanControlled(Player("player2"));
        }

        [UnityTest]
        public IEnumerator VsComputerGivesRedToTheCpuAndKeepsBlueHuman()
        {
            GameModeSettings.Select(GameMode.VsComputer);
            yield return Load("Arena_01");

            AssertHumanControlled(Player("player1"));
            AssertCpuControlled(Player("player2"));
        }

        [UnityTest]
        public IEnumerator MenuButtonsSelectTheModeAndStartFreshInArena01()
        {
            yield return Load("Main Menu");
            ClickButton("VsComputerButton");
            yield return WaitForScene("Arena_01");

            Assert.AreEqual(GameMode.VsComputer, GameModeSettings.Current);
            AssertCpuControlled(Player("player2"));

            InvokeSceneLoader("LoadMainMenu");
            yield return WaitForScene("Main Menu");
            Assert.AreEqual(GameMode.TwoPlayers, GameModeSettings.Current, "going to the menu resets the mode");

            ClickButton("TwoPlayersButton");
            yield return WaitForScene("Arena_01");
            Assert.AreEqual(GameMode.TwoPlayers, GameModeSettings.Current);
            AssertHumanControlled(Player("player2"));
        }

        [UnityTest]
        public IEnumerator ModePersistsThroughAllArenasAndPlayAgain()
        {
            GameModeSettings.Select(GameMode.VsComputer);

            foreach (string arena in new[] { "Arena_01", "Arena_02", "Arena_03" })
            {
                yield return Load(arena);
                AssertCpuControlled(Player("player2"));
            }

            yield return Load("Results");
            InvokeSceneLoader("LoadGame"); // "Play Again"
            yield return WaitForScene("Arena_01");

            Assert.AreEqual(GameMode.VsComputer, GameModeSettings.Current, "Play Again keeps the mode");
            AssertCpuControlled(Player("player2"));
        }

        [UnityTest]
        public IEnumerator CpuPlaysThroughTheNormalGameLoopWithinTheRules()
        {
            GameModeSettings.Select(GameMode.VsComputer);
            yield return Load("Arena_01");

            Transform red = Player("player2");
            Transform blue = Player("player1");
            Component cpu = red.GetComponent("CpuPlayerController");
            var brain = (CpuOpponentBrain)cpu.GetType().GetProperty("Brain").GetValue(cpu);

            // Same tuning as Blue: the CPU doesn't get faster movement or stronger pulses.
            foreach (string field in new[] { "moveSpeed", "pulseRadius", "pulseForce", "liftForce", "softPossessionForce" })
                Assert.AreEqual(Field<float>(Movement(blue), field), Field<float>(Movement(red), field), field);

            var kick = red.GetComponent("PlayerKick") as Behaviour;
            if (kick != null)
                Assert.IsFalse(kick.enabled, "the old kick system must stay off");

            // Countdown (3, 2, 1, GO!) ~3.7 s, then let the CPU play for a while.
            yield return new WaitForSeconds(4f);

            Vector3 start = red.position;
            float travelled = 0f;
            Vector3 last = start;
            var pulseTimes = new List<float>();
            float lastSeenPulse = brain.LastPulseTime;
            var pulseEffects = new HashSet<GameObject>();
            float end = Time.time + 6f;
            // Goal slow-motion zones can drop timeScale to 0.01, so also cap the real time spent.
            float realTimeLimit = Time.realtimeSinceStartup + 45f;

            while (Time.time < end && Time.realtimeSinceStartup < realTimeLimit)
            {
                yield return null;
                travelled += Vector3.Distance(last, red.position);
                last = red.position;

                if (!brain.LastPulseTime.Equals(lastSeenPulse))
                {
                    lastSeenPulse = brain.LastPulseTime;
                    pulseTimes.Add(lastSeenPulse);
                }

                foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    if (root.name.StartsWith("JumpPulse"))
                        pulseEffects.Add(root);
                }
            }

            Assert.Greater(travelled, 2f, "the CPU should actually move around");
            Assert.Greater(pulseTimes.Count, 0, "the CPU should use its pulse");

            for (int i = 1; i < pulseTimes.Count; i++)
                Assert.GreaterOrEqual(pulseTimes[i] - pulseTimes[i - 1], 0.4f - 1e-4f, "pulse rate limit violated");

            // Blue is idle in this test, so every pulse effect belongs to the CPU: one effect per pulse.
            Assert.LessOrEqual(pulseEffects.Count, pulseTimes.Count, "pulse effect must not be duplicated");
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
            // Let Awake/Start run (GameManager.Start applies the mode).
            yield return null;
            yield return null;
        }

        static void ClickButton(string name)
        {
            Button button = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Button>(true))
                .FirstOrDefault(b => b.name == name);
            Assert.IsNotNull(button, "button " + name);
            button.onClick.Invoke();
        }

        static void InvokeSceneLoader(string method)
        {
            Component loader = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
                .FirstOrDefault(c => c.GetType().Name == "SceneLoader");
            Assert.IsNotNull(loader, "SceneLoader in " + SceneManager.GetActiveScene().name);
            loader.GetType().GetMethod(method).Invoke(loader, null);
        }

        static Transform Player(string field)
        {
            Component manager = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
                .FirstOrDefault(c => c.GetType().Name == "GameManager");
            Assert.IsNotNull(manager, "GameManager");
            var player = manager.GetType().GetField(field).GetValue(manager) as Transform;
            Assert.IsNotNull(player, "GameManager." + field);
            return player;
        }

        static Component Movement(Transform player)
        {
            Component movement = player.GetComponent("PlayerMovement");
            Assert.IsNotNull(movement, player.name + " PlayerMovement");
            return movement;
        }

        static bool UsesExternalInput(Transform player)
        {
            Component movement = Movement(player);
            return (bool)movement.GetType().GetProperty("UsesExternalInput").GetValue(movement);
        }

        static void AssertHumanControlled(Transform player)
        {
            var cpu = player.GetComponent("CpuPlayerController") as Behaviour;
            Assert.IsTrue(cpu == null || !cpu.enabled, player.name + " should not be CPU controlled");
            Assert.IsFalse(UsesExternalInput(player), player.name + " should read its keyboard keys");
        }

        static void AssertCpuControlled(Transform player)
        {
            var cpu = player.GetComponent("CpuPlayerController") as Behaviour;
            Assert.IsNotNull(cpu, player.name + " should have a CpuPlayerController");
            Assert.IsTrue(cpu.enabled);
            Assert.IsTrue(UsesExternalInput(player), player.name + " keyboard keys must be ignored");
        }

        static T Field<T>(Component component, string name)
        {
            FieldInfo field = component.GetType().GetField(name);
            Assert.IsNotNull(field, component.GetType().Name + "." + name);
            return (T)field.GetValue(component);
        }
    }
}
