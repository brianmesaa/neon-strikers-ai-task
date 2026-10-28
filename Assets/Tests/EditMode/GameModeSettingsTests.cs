using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace NeonStrikers.Tests
{
    public class GameModeSettingsTests
    {
        [SetUp]
        public void SetUp() => GameModeSettings.ResetToDefault();

        [TearDown]
        public void TearDown() => GameModeSettings.ResetToDefault();

        [Test]
        public void DefaultModeIsTwoPlayers()
        {
            Assert.AreEqual(GameMode.TwoPlayers, GameModeSettings.DefaultMode);
            Assert.AreEqual(GameMode.TwoPlayers, GameModeSettings.Current);
            Assert.IsFalse(GameModeSettings.IsVsComputer);
        }

        [Test]
        public void SelectingVsComputerIsRemembered()
        {
            GameModeSettings.Select(GameMode.VsComputer);

            Assert.AreEqual(GameMode.VsComputer, GameModeSettings.Current);
            Assert.IsTrue(GameModeSettings.IsVsComputer);
            // Reading again (e.g. from the next arena) still gives the same answer.
            Assert.AreEqual(GameMode.VsComputer, GameModeSettings.Current);
        }

        [Test]
        public void SelectingTwoPlayersAfterVsComputerSwitchesBack()
        {
            GameModeSettings.Select(GameMode.VsComputer);
            GameModeSettings.Select(GameMode.TwoPlayers);

            Assert.AreEqual(GameMode.TwoPlayers, GameModeSettings.Current);
            Assert.IsFalse(GameModeSettings.IsVsComputer);
        }

        [Test]
        public void ResetToDefaultClearsTheSelection()
        {
            GameModeSettings.Select(GameMode.VsComputer);
            GameModeSettings.ResetToDefault();

            Assert.AreEqual(GameMode.TwoPlayers, GameModeSettings.Current);
        }

        [Test]
        public void FreshLaunchHookResetsToTwoPlayers()
        {
            // Domain reload is disabled in this project, so the reset must be an explicit
            // RuntimeInitializeOnLoadMethod that runs before any scene (menu OR arena) loads.
            MethodInfo hook = typeof(GameModeSettings).GetMethod("ResetOnLaunch", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(hook, "GameModeSettings.ResetOnLaunch is missing");

            var attribute = hook.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
            Assert.IsNotNull(attribute, "ResetOnLaunch must be a RuntimeInitializeOnLoadMethod");
            Assert.AreEqual(RuntimeInitializeLoadType.SubsystemRegistration, attribute.loadType);

            GameModeSettings.Select(GameMode.VsComputer);
            hook.Invoke(null, null);
            Assert.AreEqual(GameMode.TwoPlayers, GameModeSettings.Current);
        }

        [Test]
        public void DisplayNamesMatchTheMenu()
        {
            Assert.AreEqual("Two Players", GameModeSettings.GetDisplayName(GameMode.TwoPlayers));
            Assert.AreEqual("Vs Computer", GameModeSettings.GetDisplayName(GameMode.VsComputer));
        }
    }
}
