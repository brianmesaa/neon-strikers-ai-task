using NeonStrikers;
using NeonStrikers.Arenas;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene changes of the arena editor feature: entering the editor, playtesting (always Two Players) and
/// coming back without losing the arena being edited (it lives in <see cref="CustomArenaSession"/>).
/// </summary>
public static class ArenaEditorNavigation
{
    /// <summary>Main Menu "Arena Editor" button.</summary>
    public static void OpenEditor()
    {
        CustomArenaSession.EndPlaytest();
        CustomArenaSession.SetFlowArena(null);
        MatchData.ResetMatch();
        GameModeSettings.ResetToDefault();
        Time.timeScale = 1f;
        SceneManager.LoadScene(CustomArenaFlow.EditorScene);
    }

    /// <summary>Plays the given layout in the custom arena scene in Two Players mode.</summary>
    public static void StartPlaytest(ArenaData arena)
    {
        CustomArenaSession.BeginPlaytest(arena);
        GameModeSettings.Select(GameMode.TwoPlayers);
        MatchData.ResetMatch();
        Time.timeScale = 1f;
        SceneManager.LoadScene(CustomArenaFlow.CustomArenaScene);
    }

    public static void RestartPlaytest()
    {
        if (!CustomArenaSession.IsPlaytesting)
            return;

        GameModeSettings.Select(GameMode.TwoPlayers);
        MatchData.ResetMatch();
        Time.timeScale = 1f;
        SceneManager.LoadScene(CustomArenaFlow.CustomArenaScene);
    }

    /// <summary>Back from a playtest to the editor; the edited arena (and its undo history) is still there.</summary>
    public static void ReturnToEditor(string message)
    {
        CustomArenaSession.EndPlaytest();
        CustomArenaSession.PendingEditorMessage = message;
        MatchData.ResetMatch();
        GameModeSettings.ResetToDefault();
        Time.timeScale = 1f;
        SceneManager.LoadScene(CustomArenaFlow.EditorScene);
    }

    /// <summary>Leaves the editor (the caller already handled unsaved changes).</summary>
    public static void LeaveEditorToMainMenu()
    {
        CustomArenaSession.EditorDocument = null;
        CustomArenaSession.EndPlaytest();
        MatchData.ResetMatch();
        GameModeSettings.ResetToDefault();
        Time.timeScale = 1f;
        SceneManager.LoadScene(CustomArenaFlow.MainMenuScene);
    }
}
