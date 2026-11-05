using UnityEngine;

namespace NeonStrikers.Arenas
{
    /// <summary>
    /// Scene order of a series: Arena_01 -> Arena_02 -> Arena_03 -> [custom 4th arena] -> Results.
    /// The custom arena is only part of the flow when one is assigned in the arena editor and it is valid;
    /// otherwise the original three-arena flow is used unchanged.
    /// </summary>
    public static class CustomArenaFlow
    {
        public const string MainMenuScene = "Main Menu";
        public const string CustomArenaScene = "CustomArena";
        public const string EditorScene = "ArenaEditor";
        public const string ResultsScene = "Results";
        public const int CustomArenaIndex = 3;

        public static readonly string[] StockArenaScenes = { "Arena_01", "Arena_02", "Arena_03" };

        /// <summary>
        /// Scene to play for a (0 based) arena index of the series, the same rule GameManager uses after a match:
        /// 0..2 are the stock arenas, 3 is the custom arena (when included), anything else is Results.
        /// </summary>
        public static string SceneForArenaIndex(int arenaIndex, bool includeCustomArena)
        {
            if (arenaIndex >= 0 && arenaIndex < StockArenaScenes.Length)
                return StockArenaScenes[arenaIndex];

            if (arenaIndex == CustomArenaIndex && includeCustomArena)
                return CustomArenaScene;

            return ResultsScene;
        }

        /// <summary>
        /// Loads the arena assigned as the 4th arena. Fails (with a reason) when none is assigned, the file is
        /// missing or broken, or the arena has problems that keep it out of the game flow.
        /// </summary>
        public static bool TryGetGameFlowArena(ArenaStorage storage, out ArenaData data, out string reason)
        {
            data = null;
            reason = null;

            if (storage == null)
            {
                reason = "No storage.";
                return false;
            }

            string id = storage.GetGameFlowArenaId();
            if (string.IsNullOrEmpty(id))
            {
                reason = "No custom arena is assigned to the game flow.";
                return false;
            }

            if (!storage.TryLoad(id, out ArenaData loaded, out string error))
            {
                reason = "The 4th arena can't be loaded: " + error;
                return false;
            }

            ArenaValidationReport report = ArenaValidator.Validate(loaded);
            if (!report.CanUseInGameFlow)
            {
                reason = "The 4th arena '" + loaded.DisplayName + "' has problems: " + report.Summary;
                return false;
            }

            data = loaded;
            return true;
        }

        /// <summary>
        /// Next scene after the arena with index <paramref name="completedArenaIndex"/> has been played.
        /// Resolves (and remembers in <see cref="CustomArenaSession"/>) the custom arena when it is next.
        /// </summary>
        public static string NextSceneAfter(int completedArenaIndex, ArenaStorage storage)
        {
            int next = completedArenaIndex + 1;

            if (next == CustomArenaIndex)
            {
                if (TryGetGameFlowArena(storage, out ArenaData arena, out string reason))
                {
                    CustomArenaSession.SetFlowArena(arena);
                    return CustomArenaScene;
                }

                CustomArenaSession.SetFlowArena(null);
                if (!string.IsNullOrEmpty(storage != null ? storage.GetGameFlowArenaId() : null))
                    Debug.LogWarning("[CustomArena] Skipping the 4th arena. " + reason);
            }

            return SceneForArenaIndex(next, false);
        }
    }

    /// <summary>
    /// State that has to survive scene loads: the arena being edited (with its undo history), playtest mode and
    /// the arena the custom arena scene must build.
    /// </summary>
    public static class CustomArenaSession
    {
        /// <summary>The editor's document, kept while playtesting so no progress (or undo history) is lost.</summary>
        public static ArenaEditorDocument EditorDocument { get; set; }

        public static bool IsPlaytesting { get; private set; }
        public static ArenaData PlaytestArena { get; private set; }
        public static ArenaData FlowArena { get; private set; }

        /// <summary>Message shown by the editor when it opens (e.g. the playtest result).</summary>
        public static string PendingEditorMessage { get; set; }

        public static void BeginPlaytest(ArenaData arena)
        {
            PlaytestArena = arena != null ? arena.Clone() : null;
            IsPlaytesting = PlaytestArena != null;
        }

        public static void EndPlaytest()
        {
            IsPlaytesting = false;
            PlaytestArena = null;
        }

        public static void SetFlowArena(ArenaData arena)
        {
            FlowArena = arena != null ? arena.Clone() : null;
        }

        /// <summary>
        /// The arena the CustomArena scene should build: the playtest layout while playtesting, otherwise the
        /// 4th arena of the game flow (if any).
        /// </summary>
        public static ArenaData ArenaForCustomScene(ArenaStorage storage)
        {
            if (IsPlaytesting && PlaytestArena != null)
                return PlaytestArena.Clone();

            if (FlowArena != null)
                return FlowArena.Clone();

            if (CustomArenaFlow.TryGetGameFlowArena(storage, out ArenaData data, out _))
                return data;

            return null;
        }

        /// <summary>Forget everything (fresh launch).</summary>
        public static void Reset()
        {
            EditorDocument = null;
            IsPlaytesting = false;
            PlaytestArena = null;
            FlowArena = null;
            PendingEditorMessage = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLaunch()
        {
            Reset();
        }
    }
}
