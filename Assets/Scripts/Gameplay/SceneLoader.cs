using NeonStrikers;
using NeonStrikers.Arenas;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public const string FirstArenaScene = "Arena_01";
    public const string MainMenuScene = "Main Menu";

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // While playtesting a custom arena, Esc returns to the arena editor instead of the menu.
            if (CustomArenaSession.IsPlaytesting)
                ReturnToArenaEditor();
            else
                LoadMainMenu();
        }
    }

    /// <summary>Main Menu "Two Players" button: Blue = WASD + Space, Red = Arrow keys + Enter.</summary>
    public void StartTwoPlayers()
    {
        StartNewSeries(GameMode.TwoPlayers);
    }

    /// <summary>Main Menu "Vs Computer" button: the human plays Blue, the computer plays Red.</summary>
    public void StartVsComputer()
    {
        StartNewSeries(GameMode.VsComputer);
    }

    public void StartNewSeries(GameMode mode)
    {
        GameModeSettings.Select(mode);
        LoadGame();
    }

    /// <summary>
    /// Starts a fresh series in Arena_01 with the CURRENT mode ("Play Again" on the Results screen).
    /// </summary>
    public void LoadGame()
    {
        CustomArenaSession.EndPlaytest();
        CustomArenaSession.SetFlowArena(null);
        MatchData.ResetMatch();
        Time.timeScale = 1f;
        SceneManager.LoadScene(FirstArenaScene);
    }

    public void LoadArena02()
    {
        SceneManager.LoadScene("Arena_02");
    }

    public void LoadArena03()
    {
        SceneManager.LoadScene("Arena_03");
    }

    public void LoadResults()
    {
        SceneManager.LoadScene("Results");
    }

    /// <summary>Back to the menu: clears the series AND the mode so the player picks again.</summary>
    public void LoadMainMenu()
    {
        CustomArenaSession.EndPlaytest();
        CustomArenaSession.SetFlowArena(null);
        MatchData.ResetMatch();
        GameModeSettings.ResetToDefault();
        Time.timeScale = 1f;
        SceneManager.LoadScene(MainMenuScene);
    }

    /// <summary>Main Menu "Arena Editor" button: create, edit, playtest and choose custom arenas.</summary>
    public void OpenArenaEditor()
    {
        ArenaEditorNavigation.OpenEditor();
    }

    /// <summary>Leaves a playtest and goes back to the arena editor (the edited arena is kept).</summary>
    public void ReturnToArenaEditor()
    {
        ArenaEditorNavigation.ReturnToEditor("Back from playtest. Your arena is exactly as you left it.");
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}
