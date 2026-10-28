using UnityEngine;

public static class MatchData
{
    public static int blueMatchWins = 0;
    public static int redMatchWins = 0;

    public static int blueScore = 0;
    public static int redScore = 0;

    public static string winnerText = "";

    public static int currentArenaIndex = 0;

    public static void ResetMatch()
    {
        blueMatchWins = 0;
        redMatchWins = 0;

        blueScore = 0;
        redScore = 0;

        winnerText = "";
        currentArenaIndex = 0;
    }

    // Domain reload is disabled in this project (Enter Play Mode Options), so make sure every
    // fresh launch / editor Play session starts a clean series, even when an arena is played directly.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnLaunch()
    {
        ResetMatch();
    }
}
