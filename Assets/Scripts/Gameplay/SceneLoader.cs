using NeonStrikers;
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
        MatchData.ResetMatch();
        GameModeSettings.ResetToDefault();
        Time.timeScale = 1f;
        SceneManager.LoadScene(MainMenuScene);
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}
