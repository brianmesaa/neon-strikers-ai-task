using System.Collections;
using NeonStrikers;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Lives")]
    public int blueLives = 3;
    public int redLives = 3;

    [Header("Match Settings")]
    public float goalResetDelay = 1.5f;
    public float finalGoalPause = 1.25f;

    [Header("References")]
    public BallController ball;
    public Transform ballSpawnPoint;
    public Transform player1;
    public Transform player2;
    public Transform player1SpawnPoint;
    public Transform player2SpawnPoint;

    [Header("Regular Goal Burst VFX")]
    public ParticleSystem leftGoalBurst;
    public ParticleSystem rightGoalBurst;

    [Header("Final Goal VFX")]
    public ParticleSystem finalGoalBlast;
    public SpriteRenderer arenaRenderer;
    public float arenaFlashDuration = 1f;
    public Color blueGoalFlashColor = new Color(0.2f, 0.6f, 1f, 1f);
    public Color redGoalFlashColor = new Color(1f, 0.25f, 0.25f, 1f);

    [Header("Camera Shake")]
    public CameraShake2D cameraShake;
    public float finalShakeDuration = 0.45f;
    public float finalShakeMagnitude = 0.18f;

    [Header("Lose Launch")]
    public float losingPlayerLaunchForce = 8f;
    public Vector2 blueWinLaunchDirection = Vector2.left;
    public Vector2 redWinLaunchDirection = Vector2.right;

    [Header("UI")]
    public TextMeshProUGUI centerMessageText;

    [Header("Blue Life Images")]
    public Image[] blueLifeImages;

    [Header("Red Life Images")]
    public Image[] redLifeImages;

    private bool gameEnded = false;
    private bool playersCanAct = false;
    private bool goalSequenceRunning = false;

    private PlayerMovement p1Movement;
    private PlayerMovement p2Movement;
    private Rigidbody2D p1Rb;
    private Rigidbody2D p2Rb;
    private PlayerLoseLaunch p1LoseLaunch;
    private PlayerLoseLaunch p2LoseLaunch;

    private Coroutine goalRoutine;
    private Coroutine arenaFlashRoutine;
    private Color arenaOriginalColor = Color.white;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        CacheReferences();
        ApplyGameMode();

        if (arenaRenderer != null)
            arenaOriginalColor = arenaRenderer.color;

        UpdateLivesUI();
        StartCoroutine(StartMatchSequence());
    }

    public bool CanPlayersAct()
    {
        return playersCanAct && !gameEnded && !goalSequenceRunning;
    }

    void CacheReferences()
    {
        if (player1 != null)
        {
            p1Movement = player1.GetComponent<PlayerMovement>();
            p1Rb = player1.GetComponent<Rigidbody2D>();
            p1LoseLaunch = player1.GetComponent<PlayerLoseLaunch>();
        }

        if (player2 != null)
        {
            p2Movement = player2.GetComponent<PlayerMovement>();
            p2Rb = player2.GetComponent<Rigidbody2D>();
            p2LoseLaunch = player2.GetComponent<PlayerLoseLaunch>();
        }
    }

    /// <summary>
    /// Two Players: Red stays on the keyboard (Arrow keys + Enter).
    /// Vs Computer: Red (player2) is handed to <see cref="CpuPlayerController"/>; its keyboard keys are ignored.
    /// Blue (player1) is always the human on WASD + Space.
    /// Playing an arena directly (no menu selection) uses the default Two Players mode.
    /// </summary>
    void ApplyGameMode()
    {
        if (p2Movement == null)
            return;

        if (GameModeSettings.IsVsComputer)
            CpuPlayerController.Attach(p2Movement, player1);
        else
            CpuPlayerController.Detach(p2Movement);
    }

    IEnumerator StartMatchSequence()
    {
        ResetPositions();
        ResetLoseEffects();
        yield return StartCoroutine(CountdownRoutine());
        EnableGameplay();
    }

    IEnumerator CountdownRoutine()
    {
        DisableGameplay();

        if (centerMessageText != null)
            centerMessageText.text = "3";
        yield return new WaitForSeconds(1f);

        if (centerMessageText != null)
            centerMessageText.text = "2";
        yield return new WaitForSeconds(1f);

        if (centerMessageText != null)
            centerMessageText.text = "1";
        yield return new WaitForSeconds(1f);

        if (centerMessageText != null)
            centerMessageText.text = "GO!";
        yield return new WaitForSeconds(0.7f);

        if (centerMessageText != null)
            centerMessageText.text = "";
    }

    public void GoalScored(string scoringTeam)
    {
        if (!CanPlayersAct())
            return;

        if (goalRoutine != null)
            StopCoroutine(goalRoutine);

        goalRoutine = StartCoroutine(HandleGoal(scoringTeam));
    }

    IEnumerator HandleGoal(string scoringTeam)
    {
        goalSequenceRunning = true;
        DisableGameplay();

        bool isFinalGoal = IsFinalGoal(scoringTeam);

        if (isFinalGoal)
        {
            ApplyGoalResult(scoringTeam);
            UpdateLivesUI();
            ShowGoalMessage(scoringTeam, true);

            PlayFinalGoalBlast(scoringTeam);
            PlayFinalGoalCameraShake();
            LaunchLosingPlayer(scoringTeam);

            yield return new WaitForSeconds(finalGoalPause);

            EndMatch();
            yield break;
        }

        PlayRegularGoalBurst(scoringTeam);

        ApplyGoalResult(scoringTeam);
        UpdateLivesUI();
        ShowGoalMessage(scoringTeam, false);

        yield return new WaitForSeconds(goalResetDelay);

        ResetPositions();
        ResetLoseEffects();
        yield return StartCoroutine(CountdownRoutine());

        goalSequenceRunning = false;
        goalRoutine = null;
        EnableGameplay();
    }

    bool IsFinalGoal(string scoringTeam)
    {
        if (scoringTeam == "Blue")
            return redLives <= 1;

        if (scoringTeam == "Red")
            return blueLives <= 1;

        return false;
    }

    void PlayRegularGoalBurst(string scoringTeam)
    {
        if (scoringTeam == "Blue")
        {
            if (rightGoalBurst != null)
                rightGoalBurst.Play();
        }
        else if (scoringTeam == "Red")
        {
            if (leftGoalBurst != null)
                leftGoalBurst.Play();
        }
    }

    void PlayFinalGoalBlast(string scoringTeam)
    {
        Color flashColor = GetTeamColor(scoringTeam);
        Vector3 blastPosition = GetFinalBlastPosition(scoringTeam);

        if (finalGoalBlast != null)
        {
            finalGoalBlast.transform.position = blastPosition;

            var main = finalGoalBlast.main;
            main.startColor = flashColor;

            var colorOverLifetime = finalGoalBlast.colorOverLifetime;
            if (colorOverLifetime.enabled)
            {
                Gradient gradient = new Gradient();
                gradient.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(flashColor, 0f),
                        new GradientColorKey(flashColor, 1f)
                    },
                    new GradientAlphaKey[]
                    {
                        new GradientAlphaKey(0.65f, 0f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );

                colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
            }

            finalGoalBlast.Clear();
            finalGoalBlast.Play();
        }

        if (arenaRenderer != null)
        {
            if (arenaFlashRoutine != null)
                StopCoroutine(arenaFlashRoutine);

            arenaFlashRoutine = StartCoroutine(FlashArena(flashColor));
        }
    }

    void PlayFinalGoalCameraShake()
    {
        if (cameraShake != null)
            StartCoroutine(cameraShake.Shake(finalShakeDuration, finalShakeMagnitude));
    }

    void LaunchLosingPlayer(string scoringTeam)
    {
        if (scoringTeam == "Blue")
        {
            if (p2LoseLaunch != null)
                p2LoseLaunch.Launch(blueWinLaunchDirection, losingPlayerLaunchForce);
        }
        else if (scoringTeam == "Red")
        {
            if (p1LoseLaunch != null)
                p1LoseLaunch.Launch(redWinLaunchDirection, losingPlayerLaunchForce);
        }
    }

    Vector3 GetFinalBlastPosition(string scoringTeam)
    {
        if (scoringTeam == "Blue" && rightGoalBurst != null)
            return rightGoalBurst.transform.position;

        if (scoringTeam == "Red" && leftGoalBurst != null)
            return leftGoalBurst.transform.position;

        if (ball != null)
            return ball.transform.position;

        return Vector3.zero;
    }

    Color GetTeamColor(string scoringTeam)
    {
        if (scoringTeam == "Blue")
            return blueGoalFlashColor;

        if (scoringTeam == "Red")
            return redGoalFlashColor;

        return Color.white;
    }

    IEnumerator FlashArena(Color flashColor)
    {
        arenaRenderer.color = flashColor;
        yield return new WaitForSeconds(arenaFlashDuration);
        arenaRenderer.color = arenaOriginalColor;
        arenaFlashRoutine = null;
    }

    void ApplyGoalResult(string scoringTeam)
    {
        if (scoringTeam == "Blue")
        {
            redLives = Mathf.Max(0, redLives - 1);
        }
        else if (scoringTeam == "Red")
        {
            blueLives = Mathf.Max(0, blueLives - 1);
        }
        else
        {
            Debug.LogWarning("Unknown scoring team: " + scoringTeam);
        }
    }

    void ShowGoalMessage(string scoringTeam, bool isFinalGoal)
    {
        if (centerMessageText == null)
            return;

        if (isFinalGoal)
            centerMessageText.text = scoringTeam + " WINS!";
        else
            centerMessageText.text = scoringTeam + " SCORES!";
    }

    void ResetPositions()
    {
        ResetBall();
        ResetPlayer(player1, player1SpawnPoint, p1Rb);
        ResetPlayer(player2, player2SpawnPoint, p2Rb);
    }

    void ResetBall()
    {
        if (ball != null && ballSpawnPoint != null)
        {
            ball.ResetBall(ballSpawnPoint.position);
        }
    }

    void ResetPlayer(Transform playerTransform, Transform spawnPoint, Rigidbody2D playerRb)
    {
        if (playerTransform == null || spawnPoint == null)
            return;

        playerTransform.position = spawnPoint.position;

        if (playerRb != null)
        {
            playerRb.linearVelocity = Vector2.zero;
            playerRb.angularVelocity = 0f;
        }
    }

    void ResetLoseEffects()
    {
        if (p1LoseLaunch != null)
            p1LoseLaunch.ResetLoseVisual();

        if (p2LoseLaunch != null)
            p2LoseLaunch.ResetLoseVisual();
    }

    void EnableGameplay()
    {
        playersCanAct = true;
        SetPlayerControl(true);
    }

    void DisableGameplay()
    {
        playersCanAct = false;
        SetPlayerControl(false);
    }

    void SetPlayerControl(bool enabled)
    {
        if (p1Movement != null)
            p1Movement.SetMovementEnabled(enabled);

        if (p2Movement != null)
            p2Movement.SetMovementEnabled(enabled);
    }

    void UpdateLivesUI()
    {
        UpdateLifeArray(blueLifeImages, blueLives);
        UpdateLifeArray(redLifeImages, redLives);
    }

    void UpdateLifeArray(Image[] images, int currentLives)
    {
        if (images == null)
            return;

        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] != null)
                images[i].enabled = i < currentLives;
        }
    }

    void EndMatch()
    {
        if (gameEnded)
            return;

        gameEnded = true;
        goalSequenceRunning = false;
        DisableGameplay();

        MatchData.blueScore = blueLives;
        MatchData.redScore = redLives;

        if (blueLives > redLives)
        {
            MatchData.blueMatchWins++;
            MatchData.winnerText = "Blue Wins!";
        }
        else if (redLives > blueLives)
        {
            MatchData.redMatchWins++;
            MatchData.winnerText = "Red Wins!";
        }
        else
        {
            MatchData.winnerText = "It's a Tie!";
        }

        LoadNextArenaOrResults();
    }

    void LoadNextArenaOrResults()
    {
        MatchData.currentArenaIndex++;

        if (MatchData.currentArenaIndex == 1)
        {
            SceneManager.LoadScene("Arena_02");
        }
        else if (MatchData.currentArenaIndex == 2)
        {
            SceneManager.LoadScene("Arena_03");
        }
        else
        {
            SceneManager.LoadScene("Results");
        }
    }
}