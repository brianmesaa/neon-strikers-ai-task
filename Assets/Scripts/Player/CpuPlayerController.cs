using System.Collections.Generic;
using NeonStrikers.AI;
using UnityEngine;

/// <summary>
/// Drives a player with the computer opponent brain in "Vs Computer" mode.
///
/// This component is added at runtime by <see cref="GameManager"/> (no inspector setup needed).
/// It only ever produces virtual key presses for <see cref="PlayerMovement"/>: it never moves
/// transforms, never writes the ball's velocity and never applies forces of its own. Movement
/// speed, lift, pulse radius/force, soft possession, slow zones, portals, collisions and
/// animation are therefore exactly the same as for a human player.
/// </summary>
[DefaultExecutionOrder(-50)] // decide before PlayerMovement.Update reads the input this frame
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement))]
public class CpuPlayerController : MonoBehaviour
{
    [Tooltip("Difficulty / feel settings. Pulse cooldown can never go below 0.4 s of game time.")]
    public CpuTuning tuning = new CpuTuning();

    [Tooltip("The human player (used to decide when to fall back and guard the goal).")]
    public Transform opponent;

    [Tooltip("Ball wall bounce used for predictions (the arena walls are bouncy).")]
    [Range(0f, 1f)] public float predictedWallBounce = 0.8f;

    private PlayerMovement movement;
    private Rigidbody2D rb;
    private ZoneSlowable zoneSlowable;
    private Rigidbody2D ballRb;
    private BallController ballController;
    private float ballRadius = 0.25f;

    private Rect ownGoal;
    private Rect targetGoal;
    private Rect arenaBounds;
    private bool goalsResolved;

    private CpuOpponentBrain brain;

    public CpuOpponentBrain Brain => brain;
    public CpuInputFrame LastFrame { get; private set; }
    public Rect OwnGoalRect => ownGoal;
    public Rect TargetGoalRect => targetGoal;

    /// <summary>Hand a player over to the computer (idempotent).</summary>
    public static CpuPlayerController Attach(PlayerMovement movement, Transform opponent)
    {
        if (movement == null)
            return null;

        CpuPlayerController controller = movement.GetComponent<CpuPlayerController>();
        if (controller == null)
            controller = movement.gameObject.AddComponent<CpuPlayerController>();

        controller.opponent = opponent;
        controller.enabled = true;
        movement.SetExternalInputEnabled(true);
        return controller;
    }

    /// <summary>Give a player back to the keyboard (used for Two Players mode).</summary>
    public static void Detach(PlayerMovement movement)
    {
        if (movement == null)
            return;

        CpuPlayerController controller = movement.GetComponent<CpuPlayerController>();
        if (controller != null)
            controller.enabled = false; // OnDisable hands input back to the keyboard

        movement.SetExternalInputEnabled(false);
    }

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        rb = GetComponent<Rigidbody2D>();
        zoneSlowable = GetComponent<ZoneSlowable>();
        brain = new CpuOpponentBrain(tuning, CpuBallPhysics.Default, CpuPlayerAbilities.Default);
    }

    void OnEnable()
    {
        if (movement != null)
            movement.SetExternalInputEnabled(true);
    }

    void OnDisable()
    {
        if (movement != null)
            movement.SetExternalInputEnabled(false);
    }

    void Start()
    {
        ResolveSceneReferences();
    }

    void Update()
    {
        if (movement == null || rb == null)
            return;

        if (ballRb == null || !goalsResolved)
            ResolveSceneReferences();

        CpuWorldState state = BuildState();
        CpuInputFrame frame = brain.Tick(state);
        LastFrame = frame;

        movement.SetExternalInput(frame.Horizontal, frame.Vertical, frame.Pulse);
    }

    CpuWorldState BuildState()
    {
        bool gameAllowsAction = GameManager.Instance == null || GameManager.Instance.CanPlayersAct();

        CpuWorldState state = new CpuWorldState
        {
            Time = Time.time,
            DeltaTime = Time.deltaTime,
            CanAct = gameAllowsAction && movement.CanMove && ballRb != null,
            SelfPosition = transform.position,
            SelfVelocity = rb.linearVelocity,
            SelfGrounded = movement.IsGrounded,
            HasOpponent = opponent != null,
            OpponentPosition = opponent != null ? (Vector2)opponent.position : Vector2.zero,
            OwnGoal = ownGoal,
            TargetGoal = targetGoal,
            ArenaBounds = arenaBounds
        };

        if (ballRb != null)
        {
            state.BallPosition = ballRb.position;
            state.BallVelocity = ballRb.linearVelocity;
        }

        brain.BallPhysics = ReadBallPhysics();
        brain.Abilities = ReadAbilities();
        return state;
    }

    CpuBallPhysics ReadBallPhysics()
    {
        CpuBallPhysics physics = CpuBallPhysics.Default;
        physics.Radius = ballRadius;
        physics.WallBounce = predictedWallBounce;

        if (ballRb != null)
        {
            physics.Gravity = Physics2D.gravity * ballRb.gravityScale;
            physics.LinearDamping = ballRb.linearDamping;
            physics.Mass = ballRb.mass > 0f ? ballRb.mass : 1f;
        }

        if (ballController != null)
            physics.MaxSpeed = ballController.maxSpeed;

        return physics;
    }

    CpuPlayerAbilities ReadAbilities()
    {
        float slow = zoneSlowable != null ? zoneSlowable.slowMultiplier : 1f;

        return new CpuPlayerAbilities
        {
            // Same values PlayerMovement uses: the CPU plans with the speed it really has.
            MoveSpeed = movement.moveSpeed * slow,
            PulseRadius = movement.pulseRadius,
            PulseForce = movement.pulseForce,
            PulseUpBias = 0.35f
        };
    }

    void ResolveSceneReferences()
    {
        if (ballRb == null)
        {
            GameManager manager = GameManager.Instance;
            if (manager != null && manager.ball != null)
                ballController = manager.ball;

            if (ballController == null)
            {
                GameObject ballObject = GameObject.FindGameObjectWithTag("Ball");
                if (ballObject != null)
                    ballController = ballObject.GetComponent<BallController>();
            }

            if (ballController != null)
            {
                ballRb = ballController.GetComponent<Rigidbody2D>();

                CircleCollider2D circle = ballController.GetComponent<CircleCollider2D>();
                if (circle != null)
                {
                    Vector3 scale = ballController.transform.lossyScale;
                    ballRadius = circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                }
            }
        }

        if (!goalsResolved)
            goalsResolved = ResolveGoals();

        arenaBounds = ResolveArenaBounds();
    }

    /// <summary>
    /// The goal where "Blue" scores is the one this (Red) player defends; the goal where "Red"
    /// scores is the one it attacks. Works for every arena layout (side goals, floor goals,
    /// rotated goals) because it reads the real GoalZone triggers.
    /// </summary>
    bool ResolveGoals()
    {
        List<GoalZone> zones = FindGoalZones();
        bool foundOwn = false;
        bool foundTarget = false;

        foreach (GoalZone zone in zones)
        {
            if (zone == null || !TryGetBounds(zone.gameObject, out Rect rect))
                continue;

            if (zone.scoringTeam == "Blue")
            {
                ownGoal = rect;
                foundOwn = true;
            }
            else if (zone.scoringTeam == "Red")
            {
                targetGoal = rect;
                foundTarget = true;
            }
        }

        if (foundOwn && foundTarget)
            return true;

        // Fallback for scenes without goal triggers: defend our own side, attack the opponent's.
        float selfX = transform.position.x;
        float opponentX = opponent != null ? opponent.position.x : -selfX;
        float side = Mathf.Sign(selfX - opponentX);
        if (side == 0f) side = 1f;

        if (!foundOwn)
            ownGoal = new Rect(selfX + side * 3f - 0.5f, transform.position.y - 1.5f, 1f, 3f);
        if (!foundTarget)
            targetGoal = new Rect(opponentX - side * 3f - 0.5f, transform.position.y - 1.5f, 1f, 3f);

        return zones.Count > 0;
    }

    List<GoalZone> FindGoalZones()
    {
        List<GoalZone> zones = new List<GoalZone>();
        if (!gameObject.scene.IsValid())
            return zones;

        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            zones.AddRange(root.GetComponentsInChildren<GoalZone>(false));

        return zones;
    }

    static bool TryGetBounds(GameObject target, out Rect rect)
    {
        rect = default;
        Collider2D[] colliders = target.GetComponents<Collider2D>();
        bool any = false;
        Bounds bounds = default;

        foreach (Collider2D col in colliders)
        {
            if (col == null || !col.enabled)
                continue;

            if (!any)
            {
                bounds = col.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(col.bounds);
            }
        }

        if (!any)
            return false;

        rect = Rect.MinMaxRect(bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y);
        return true;
    }

    static Rect ResolveArenaBounds()
    {
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic)
            return default;

        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;
        Vector3 c = cam.transform.position;
        return Rect.MinMaxRect(c.x - halfWidth, c.y - halfHeight, c.x + halfWidth, c.y + halfHeight);
    }
}
