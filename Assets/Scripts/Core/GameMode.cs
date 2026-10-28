using UnityEngine;

namespace NeonStrikers
{
    /// <summary>
    /// How the Red player is controlled for the current three-arena series.
    /// </summary>
    public enum GameMode
    {
        /// <summary>Local multiplayer: Blue = WASD + Space, Red = Arrow keys + Enter.</summary>
        TwoPlayers = 0,

        /// <summary>One human (Blue, WASD + Space) against the computer controlled Red player.</summary>
        VsComputer = 1
    }

    /// <summary>
    /// Holds the game mode chosen in the Main Menu so it survives the
    /// Arena_01 -> Arena_02 -> Arena_03 -> Results scene chain.
    ///
    /// Rules:
    ///  * Defaults to <see cref="GameMode.TwoPlayers"/> on every fresh launch / editor Play session,
    ///    including when an arena scene is opened and played directly (no menu selection).
    ///  * "Play Again" keeps the current mode.
    ///  * Returning to the Main Menu resets it so the player chooses again.
    /// </summary>
    public static class GameModeSettings
    {
        public const GameMode DefaultMode = GameMode.TwoPlayers;

        public static GameMode Current { get; private set; } = DefaultMode;

        public static bool IsVsComputer => Current == GameMode.VsComputer;

        public static void Select(GameMode mode)
        {
            Current = mode;
        }

        public static void ResetToDefault()
        {
            Current = DefaultMode;
        }

        public static string GetDisplayName(GameMode mode)
        {
            return mode == GameMode.VsComputer ? "Vs Computer" : "Two Players";
        }

        // The project uses "Enter Play Mode Options" with domain reload disabled, which means
        // static state would otherwise leak from one editor Play session into the next.
        // SubsystemRegistration runs before the first scene loads on every launch / Play press.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLaunch()
        {
            ResetToDefault();
        }
    }
}
