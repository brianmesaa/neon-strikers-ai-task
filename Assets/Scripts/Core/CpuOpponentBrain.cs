using System;
using UnityEngine;

namespace NeonStrikers.AI
{
    /// <summary>
    /// What the computer "presses" this frame. These are virtual keys, exactly like a human's
    /// keyboard: each axis is -1, 0 or +1 and Pulse is a single key-down event.
    /// PlayerMovement turns them into motion through the same code path it uses for keyboard input.
    /// </summary>
    public struct CpuInputFrame
    {
        public int Horizontal;
        public int Vertical;
        public bool Pulse;

        public CpuInputFrame(int horizontal, int vertical, bool pulse)
        {
            Horizontal = Mathf.Clamp(horizontal, -1, 1);
            Vertical = Mathf.Clamp(vertical, -1, 1);
            Pulse = pulse;
        }
    }

    public enum CpuIntent
    {
        /// <summary>Gameplay is paused (countdown, goal sequence, match over).</summary>
        Idle,
        /// <summary>Go to the ball from the side that sends it towards the Blue goal.</summary>
        Attack,
        /// <summary>The ball is heading into / sitting near the Red goal: get between it and the goal.</summary>
        Defend,
        /// <summary>The opponent controls the ball: fall back to a covering spot in front of the Red goal.</summary>
        Guard
    }

    /// <summary>Snapshot of everything the brain is allowed to know for one decision.</summary>
    public struct CpuWorldState
    {
        /// <summary>Scaled game time (Time.time). Slow motion slows the CPU down exactly like everything else.</summary>
        public float Time;
        public float DeltaTime;
        public bool CanAct;

        /// <summary>Player pivot position (the same point PlayerMovement uses as the pulse centre).</summary>
        public Vector2 SelfPosition;
        public Vector2 SelfVelocity;
        public bool SelfGrounded;

        public bool HasOpponent;
        public Vector2 OpponentPosition;

        public Vector2 BallPosition;
        public Vector2 BallVelocity;

        /// <summary>The goal the CPU defends (a goal here scores for the opponent).</summary>
        public Rect OwnGoal;
        /// <summary>The goal the CPU attacks (a goal here scores for the CPU).</summary>
        public Rect TargetGoal;
        /// <summary>Optional playfield bounds used to bounce predictions. Width/height &lt;= 0 means unbounded.</summary>
        public Rect ArenaBounds;
    }

    /// <summary>Ball physics values, read from the real Rigidbody2D / BallController.</summary>
    [Serializable]
    public struct CpuBallPhysics
    {
        public Vector2 Gravity;
        public float LinearDamping;
        public float MaxSpeed;
        public float Mass;
        public float Radius;
        public float WallBounce;

        public static CpuBallPhysics Default => new CpuBallPhysics
        {
            Gravity = new Vector2(0f, -9.81f * 0.6f),
            LinearDamping = 0.35f,
            MaxSpeed = 12f,
            Mass = 1f,
            Radius = 0.25f,
            WallBounce = 0.8f
        };
    }

    /// <summary>The CPU player's abilities, read from its own PlayerMovement (identical to Blue's).</summary>
    [Serializable]
    public struct CpuPlayerAbilities
    {
        public float MoveSpeed;
        public float PulseRadius;
        public float PulseForce;
        /// <summary>PlayerMovement.PulseBall adds this much "up" to the pulse direction.</summary>
        public float PulseUpBias;

        public static CpuPlayerAbilities Default => new CpuPlayerAbilities
        {
            MoveSpeed = 6f,
            PulseRadius = 1.5f,
            PulseForce = 12f,
            PulseUpBias = 0.35f
        };
    }

    /// <summary>Difficulty / feel knobs. Defaults are tuned to be a real opponent, not a pushover.</summary>
    [Serializable]
    public class CpuTuning
    {
        /// <summary>Hard floor for the time between two CPU pulse attempts (seconds of game time).</summary>
        public const float MinPulseInterval = 0.4f;

        [Tooltip("Seconds of game time between pulse attempts. Never lower than 0.4.")]
        public float pulseCooldown = MinPulseInterval;

        [Tooltip("How far ahead (seconds) the ball trajectory is predicted.")]
        public float predictionHorizon = 1.4f;
        public float simulationStep = 0.02f;

        [Tooltip("How far behind the ball (on the side away from the Blue goal) the CPU lines up.")]
        public float approachOffset = 0.55f;

        [Tooltip("Hysteresis for the virtual left/right keys: start moving above, stop below.")]
        public float startDeadzone = 0.3f;
        public float stopDeadzone = 0.1f;

        [Tooltip("Lift (pulse) to climb when the target is at least this much higher.")]
        public float liftHeightThreshold = 1.0f;
        public float liftHorizontalRange = 3f;
        [Tooltip("Only lift again once vertical speed has dropped below this (prevents climbing forever).")]
        public float liftMaxRiseSpeed = 1.0f;

        [Tooltip("Pressing a direction but not moving for this long triggers a hop.")]
        public float stuckTime = 0.5f;

        [Tooltip("A ball/self jump larger than this in one frame is treated as a portal or reset.")]
        public float teleportDistance = 1.5f;
        [Tooltip("A ball velocity turn larger than this is treated as a new situation (bounce, hit).")]
        public float directionChangeDegrees = 60f;

        [Tooltip("How far ahead (seconds) a candidate pulse is simulated before it is judged.")]
        public float shotHorizon = 2.5f;

        [Tooltip("A shot must beat 'do nothing' by at least this score to be taken.")]
        public float minShotImprovement = 0.5f;

        [Tooltip("Ball this close to the Red goal always triggers defending.")]
        public float dangerRadius = 2.5f;
        [Tooltip("Opponent must be this much closer to the ball before the CPU falls back to guard.")]
        public float guardMargin = 1.5f;
        [Tooltip("Opponent within pulse reach + this margin of the ball counts as contesting it.")]
        public float contestMargin = 0.5f;
        [Range(0f, 1f)] public float guardFraction = 0.45f;

        public float EffectivePulseCooldown => Mathf.Max(MinPulseInterval, pulseCooldown);
    }

    /// <summary>
    /// Enforces "at most one pulse attempt every N seconds of game time".
    /// </summary>
    public sealed class CpuPulseGate
    {
        private readonly float interval;
        private float lastPulseTime = float.NegativeInfinity;

        public CpuPulseGate(float interval)
        {
            this.interval = Mathf.Max(CpuTuning.MinPulseInterval, interval);
        }

        public float Interval => interval;
        public float LastPulseTime => lastPulseTime;

        public bool IsReady(float now)
        {
            // Defensive: if the clock ever goes backwards (new session), don't lock up.
            if (now < lastPulseTime)
                lastPulseTime = float.NegativeInfinity;

            return now - lastPulseTime >= interval;
        }

        public bool TryConsume(float now)
        {
            if (!IsReady(now))
                return false;

            lastPulseTime = now;
            return true;
        }

        public void Reset()
        {
            lastPulseTime = float.NegativeInfinity;
        }
    }

    /// <summary>
    /// Cheap ballistic prediction of the ball: gravity, Rigidbody2D linear damping, the
    /// BallController speed cap and simple bounces off the arena bounds. It ignores interior
    /// walls on purpose. The brain re-predicts every frame, so small errors get corrected.
    /// </summary>
    public static class CpuBallPredictor
    {
        public struct Outcome
        {
            public bool ReachesTarget;
            public bool ReachesOwnGoal;
            public float TimeToEvent;
            public float ClosestDistanceToTarget;
            public Vector2 EventPoint;
            public Vector2 FinalPosition;
        }

        public static Vector2 Step(Vector2 velocity, in CpuBallPhysics physics, float dt)
        {
            velocity += physics.Gravity * dt;
            velocity *= 1f / (1f + dt * Mathf.Max(0f, physics.LinearDamping));

            if (physics.MaxSpeed > 0f && velocity.sqrMagnitude > physics.MaxSpeed * physics.MaxSpeed)
                velocity = velocity.normalized * physics.MaxSpeed;

            return velocity;
        }

        public static Vector2 PositionAt(Vector2 position, Vector2 velocity, float time, in CpuBallPhysics physics, Rect bounds, float step)
        {
            step = Mathf.Max(0.005f, step);
            float t = 0f;
            while (t < time)
            {
                float dt = Mathf.Min(step, time - t);
                velocity = Step(velocity, physics, dt);
                position += velocity * dt;
                Bounce(ref position, ref velocity, bounds, physics);
                t += dt;
            }

            return position;
        }

        public static Outcome Simulate(Vector2 position, Vector2 velocity, in CpuBallPhysics physics,
            Rect targetGoal, Rect ownGoal, Rect bounds, float horizon, float step)
        {
            step = Mathf.Max(0.005f, step);
            Outcome outcome = new Outcome
            {
                ClosestDistanceToTarget = DistanceToRect(targetGoal, position),
                TimeToEvent = horizon,
                FinalPosition = position
            };

            if (Contains(targetGoal, position, physics.Radius))
            {
                outcome.ReachesTarget = true;
                outcome.TimeToEvent = 0f;
                outcome.ClosestDistanceToTarget = 0f;
                outcome.EventPoint = position;
                return outcome;
            }

            if (Contains(ownGoal, position, physics.Radius))
            {
                outcome.ReachesOwnGoal = true;
                outcome.TimeToEvent = 0f;
                outcome.EventPoint = position;
                return outcome;
            }

            for (float t = step; t <= horizon + 1e-4f; t += step)
            {
                velocity = Step(velocity, physics, step);
                position += velocity * step;
                Bounce(ref position, ref velocity, bounds, physics);

                float d = DistanceToRect(targetGoal, position);
                if (d < outcome.ClosestDistanceToTarget)
                    outcome.ClosestDistanceToTarget = d;

                if (Contains(targetGoal, position, physics.Radius))
                {
                    outcome.ReachesTarget = true;
                    outcome.TimeToEvent = t;
                    outcome.ClosestDistanceToTarget = 0f;
                    outcome.EventPoint = position;
                    break;
                }

                if (Contains(ownGoal, position, physics.Radius))
                {
                    outcome.ReachesOwnGoal = true;
                    outcome.TimeToEvent = t;
                    outcome.EventPoint = position;
                    break;
                }
            }

            outcome.FinalPosition = position;
            return outcome;
        }

        public static float DistanceToRect(Rect rect, Vector2 point)
        {
            if (rect.width <= 0f && rect.height <= 0f)
                return Vector2.Distance(rect.center, point);

            float dx = Mathf.Max(rect.xMin - point.x, 0f, point.x - rect.xMax);
            float dy = Mathf.Max(rect.yMin - point.y, 0f, point.y - rect.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static bool Contains(Rect rect, Vector2 point, float radius)
        {
            if (rect.width <= 0f || rect.height <= 0f)
                return false;

            return DistanceToRect(rect, point) <= radius;
        }

        private static void Bounce(ref Vector2 position, ref Vector2 velocity, Rect bounds, in CpuBallPhysics physics)
        {
            if (bounds.width <= 0f || bounds.height <= 0f)
                return;

            float bounce = Mathf.Clamp01(physics.WallBounce);

            if (position.x < bounds.xMin) { position.x = bounds.xMin; velocity.x = Mathf.Abs(velocity.x) * bounce; }
            else if (position.x > bounds.xMax) { position.x = bounds.xMax; velocity.x = -Mathf.Abs(velocity.x) * bounce; }

            if (position.y < bounds.yMin) { position.y = bounds.yMin; velocity.y = Mathf.Abs(velocity.y) * bounce; }
            else if (position.y > bounds.yMax) { position.y = bounds.yMax; velocity.y = -Mathf.Abs(velocity.y) * bounce; }
        }
    }

    /// <summary>
    /// Reactive decision maker for the computer controlled Red player.
    ///
    /// Every frame it:
    ///  1. Predicts where the ball is going from its CURRENT position and velocity (nothing cached,
    ///     so portals, bounces and hits are picked up immediately).
    ///  2. Picks an intent: Defend if that prediction ends in the Red goal (or the ball is close to it),
    ///     Guard if the opponent has the ball (clearly closer, or contesting it while our pulse
    ///     recharges), otherwise Attack.
    ///  3. Picks a target point: the earliest point on the predicted path it can reach, offset to the
    ///     useful side of the ball (away from the Blue goal when attacking, goal-side when defending).
    ///  4. Presses left/right towards it with hysteresis so movement doesn't jitter.
    ///  5. When the ball is within pulse reach, tries each direction a human could hold, simulates the
    ///     resulting pulse and only fires if it beats doing nothing (never pulses the ball into its own
    ///     goal). Otherwise it may pulse to climb towards a high ball or to hop when stuck.
    ///  6. Never pulses more than once per <see cref="CpuTuning.EffectivePulseCooldown"/> seconds.
    /// </summary>
    public sealed class CpuOpponentBrain
    {
        private static readonly Vector2Int[] ShotKeyCandidates =
        {
            new Vector2Int(-1, 0), new Vector2Int(1, 0),
            new Vector2Int(-1, 1), new Vector2Int(1, 1),
            new Vector2Int(-1, -1), new Vector2Int(1, -1),
            new Vector2Int(0, 1), new Vector2Int(0, -1)
        };

        private readonly CpuTuning tuning;
        private readonly CpuPulseGate pulseGate;

        private int heldHorizontal;
        private float stuckTimer;
        private bool hasHistory;
        private Vector2 lastBallPosition;
        private Vector2 lastBallVelocity;
        private Vector2 lastSelfPosition;

        public CpuBallPhysics BallPhysics;
        public CpuPlayerAbilities Abilities;

        public CpuOpponentBrain(CpuTuning tuning, CpuBallPhysics ballPhysics, CpuPlayerAbilities abilities)
        {
            this.tuning = tuning ?? new CpuTuning();
            BallPhysics = ballPhysics;
            Abilities = abilities;
            pulseGate = new CpuPulseGate(this.tuning.EffectivePulseCooldown);
        }

        public CpuTuning Tuning => tuning;
        public CpuIntent LastIntent { get; private set; } = CpuIntent.Idle;
        public Vector2 LastTarget { get; private set; }
        public float LastPulseTime => pulseGate.LastPulseTime;
        public float PulseInterval => pulseGate.Interval;
        /// <summary>Incremented whenever the ball jumps (portal / reset) or sharply changes direction.</summary>
        public int ReplanCount { get; private set; }

        /// <summary>Total reach of a pulse measured from the player's pivot to the ball centre.</summary>
        public float PulseReach => Abilities.PulseRadius + Mathf.Max(0f, BallPhysics.Radius);

        public CpuInputFrame Tick(in CpuWorldState state)
        {
            if (!state.CanAct)
            {
                // Countdown / goal sequence / match end: release all keys and forget motion history
                // so the next round starts from a clean slate (positions get reset by GameManager).
                heldHorizontal = 0;
                stuckTimer = 0f;
                hasHistory = false;
                LastIntent = CpuIntent.Idle;
                return new CpuInputFrame(0, 0, false);
            }

            TrackDiscontinuities(state);

            bool pulseReady = pulseGate.IsReady(state.Time);
            CpuIntent intent = ChooseIntent(state, pulseReady, out CpuBallPredictor.Outcome baseline);
            Vector2 target = ChooseTarget(state, intent, baseline);

            LastIntent = intent;
            LastTarget = target;

            int horizontal = ComputeHorizontal(target.x - state.SelfPosition.x);
            int vertical = target.y - state.SelfPosition.y > tuning.liftHeightThreshold * 0.5f ? 1 : 0;

            bool ballInReach = Vector2.Distance(state.SelfPosition, state.BallPosition) <= PulseReach;

            if (intent == CpuIntent.Attack && !pulseReady && IsOnWrongSide(state) && ballInReach)
            {
                // Between the ball and the Blue goal with the pulse on cooldown: don't walk into the
                // ball and shove it towards our own goal. Wait for the pulse instead.
                if (horizontal == Sign(state.BallPosition.x - state.SelfPosition.x))
                    horizontal = 0;
            }

            UpdateStuckTimer(state, horizontal);

            bool pulse = false;
            if (pulseReady)
            {
                if (ballInReach)
                {
                    // The pulse WILL hit the ball, so only fire with a direction that helps.
                    if (TryChooseShot(state, baseline, out Vector2Int keys))
                    {
                        horizontal = keys.x;
                        vertical = keys.y;
                        pulse = true;
                    }
                }
                else if (WantsLift(state, target))
                {
                    pulse = true;
                    stuckTimer = 0f;
                }
            }

            if (pulse)
                pulseGate.TryConsume(state.Time);

            return new CpuInputFrame(horizontal, vertical, pulse);
        }

        // ------------------------------------------------------------------ perception

        private void TrackDiscontinuities(in CpuWorldState state)
        {
            if (hasHistory)
            {
                float dt = Mathf.Max(0f, state.DeltaTime);
                Vector2 expectedBall = lastBallPosition + lastBallVelocity * dt;
                bool ballJumped = (state.BallPosition - expectedBall).magnitude > tuning.teleportDistance;

                bool ballTurned = lastBallVelocity.magnitude > 1.5f && state.BallVelocity.magnitude > 1.5f &&
                                  Vector2.Angle(lastBallVelocity, state.BallVelocity) > tuning.directionChangeDegrees;

                bool selfJumped = (state.SelfPosition - lastSelfPosition).magnitude > tuning.teleportDistance;

                if (ballJumped || ballTurned || selfJumped)
                {
                    ReplanCount++;
                    // Drop any committed movement so the new situation is answered immediately.
                    heldHorizontal = 0;
                    stuckTimer = 0f;
                }
            }

            hasHistory = true;
            lastBallPosition = state.BallPosition;
            lastBallVelocity = state.BallVelocity;
            lastSelfPosition = state.SelfPosition;
        }

        // ------------------------------------------------------------------ planning

        private CpuIntent ChooseIntent(in CpuWorldState state, bool pulseReady, out CpuBallPredictor.Outcome baseline)
        {
            baseline = CpuBallPredictor.Simulate(state.BallPosition, state.BallVelocity, BallPhysics,
                state.TargetGoal, state.OwnGoal, state.ArenaBounds, tuning.predictionHorizon, tuning.simulationStep);

            if (baseline.ReachesOwnGoal)
                return CpuIntent.Defend;

            if (CpuBallPredictor.DistanceToRect(state.OwnGoal, state.BallPosition) <= tuning.dangerRadius)
                return CpuIntent.Defend;

            if (state.HasOpponent)
            {
                float selfToBall = Vector2.Distance(state.SelfPosition, state.BallPosition);
                float opponentToBall = Vector2.Distance(state.OpponentPosition, state.BallPosition);
                bool opponentContests = opponentToBall <= PulseReach + tuning.contestMargin;

                // The opponent "has" the ball when clearly closer, or when it can pulse the ball
                // while our own pulse is still on cooldown (we would lose that exchange).
                bool opponentHasBall = opponentToBall + tuning.guardMargin < selfToBall ||
                                       (opponentContests && !pulseReady);

                Vector2 ownGoal = state.OwnGoal.center;
                bool alreadyGoalSide = Vector2.Distance(state.SelfPosition, ownGoal) + 0.5f <
                                       Vector2.Distance(state.BallPosition, ownGoal);

                if (opponentHasBall && !alreadyGoalSide)
                    return CpuIntent.Guard;
            }

            return CpuIntent.Attack;
        }

        private Vector2 ChooseTarget(in CpuWorldState state, CpuIntent intent, in CpuBallPredictor.Outcome baseline)
        {
            if (intent == CpuIntent.Guard)
            {
                Vector2 goal = state.OwnGoal.center;
                return goal + (state.BallPosition - goal) * tuning.guardFraction;
            }

            float horizon = intent == CpuIntent.Defend && baseline.ReachesOwnGoal
                ? baseline.TimeToEvent
                : tuning.predictionHorizon;

            Vector2 intercept = FindIntercept(state, horizon);

            float sideOffset;
            if (intent == CpuIntent.Defend)
            {
                // Stand between the ball and our goal.
                sideOffset = Sign(state.OwnGoal.center.x - intercept.x) * tuning.approachOffset;
            }
            else
            {
                // Come from the side opposite the Blue goal so contact pushes the ball the right way.
                sideOffset = -Sign(state.TargetGoal.center.x - intercept.x) * tuning.approachOffset;
            }

            return new Vector2(intercept.x + sideOffset, intercept.y);
        }

        /// <summary>
        /// Earliest point on the predicted ball path the CPU can get under/next to in time.
        /// </summary>
        private Vector2 FindIntercept(in CpuWorldState state, float horizon)
        {
            float step = Mathf.Max(0.01f, tuning.simulationStep * 2f);
            float speed = Mathf.Max(0.01f, Abilities.MoveSpeed);
            float reach = PulseReach * 0.6f;

            Vector2 position = state.BallPosition;
            Vector2 velocity = state.BallVelocity;

            if (Mathf.Abs(position.x - state.SelfPosition.x) - reach <= 0f)
                return position;

            for (float t = step; t <= horizon + 1e-4f; t += step)
            {
                velocity = CpuBallPredictor.Step(velocity, BallPhysics, step);
                position += velocity * step;
                BounceInside(ref position, ref velocity, state.ArenaBounds);

                float travelTime = (Mathf.Abs(position.x - state.SelfPosition.x) - reach) / speed;
                if (travelTime <= t)
                    return position;
            }

            return position;
        }

        private void BounceInside(ref Vector2 position, ref Vector2 velocity, Rect bounds)
        {
            if (bounds.width <= 0f || bounds.height <= 0f)
                return;

            float bounce = Mathf.Clamp01(BallPhysics.WallBounce);
            if (position.x < bounds.xMin) { position.x = bounds.xMin; velocity.x = Mathf.Abs(velocity.x) * bounce; }
            else if (position.x > bounds.xMax) { position.x = bounds.xMax; velocity.x = -Mathf.Abs(velocity.x) * bounce; }
            if (position.y < bounds.yMin) { position.y = bounds.yMin; velocity.y = Mathf.Abs(velocity.y) * bounce; }
            else if (position.y > bounds.yMax) { position.y = bounds.yMax; velocity.y = -Mathf.Abs(velocity.y) * bounce; }
        }

        private bool IsOnWrongSide(in CpuWorldState state)
        {
            int usefulSide = -Sign(state.TargetGoal.center.x - state.BallPosition.x);
            int currentSide = Sign(state.SelfPosition.x - state.BallPosition.x);
            return usefulSide != 0 && currentSide != 0 && currentSide != usefulSide;
        }

        // ------------------------------------------------------------------ actions

        /// <summary>Virtual left/right keys with hysteresis (smooth, no flicker around the target).</summary>
        private int ComputeHorizontal(float dx)
        {
            float distance = Mathf.Abs(dx);
            int direction = Sign(dx);

            if (distance >= tuning.startDeadzone)
                heldHorizontal = direction;
            else if (distance <= tuning.stopDeadzone)
                heldHorizontal = 0;
            else if (heldHorizontal != 0 && heldHorizontal != direction)
                heldHorizontal = 0; // overshot the target: release instead of reversing back and forth

            return heldHorizontal;
        }

        private void UpdateStuckTimer(in CpuWorldState state, int horizontal)
        {
            bool pushingButNotMoving = horizontal != 0 &&
                                       Mathf.Abs(state.SelfVelocity.x) < Abilities.MoveSpeed * 0.15f;

            stuckTimer = pushingButNotMoving ? stuckTimer + Mathf.Max(0f, state.DeltaTime) : 0f;
        }

        private bool WantsLift(in CpuWorldState state, Vector2 target)
        {
            if (stuckTimer >= tuning.stuckTime)
                return true;

            float dy = target.y - state.SelfPosition.y;
            float dx = Mathf.Abs(target.x - state.SelfPosition.x);

            return dy > tuning.liftHeightThreshold &&
                   dx < tuning.liftHorizontalRange &&
                   state.SelfVelocity.y <= tuning.liftMaxRiseSpeed;
        }

        /// <summary>
        /// Same maths as PlayerMovement.PulseBall: input direction (or up when no keys) plus an upward bias.
        /// </summary>
        public Vector2 PulseDirectionFor(Vector2Int keys)
        {
            Vector2 direction = keys == Vector2Int.zero ? Vector2.up : ((Vector2)keys).normalized;
            return (direction + Vector2.up * Abilities.PulseUpBias).normalized;
        }

        /// <summary>
        /// Try every direction a human could hold while pressing pulse, simulate the ball after the
        /// impulse and keep the best one. Returns false when no option beats leaving the ball alone.
        /// </summary>
        public bool TryChooseShot(in CpuWorldState state, in CpuBallPredictor.Outcome baseline, out Vector2Int keys)
        {
            float shotHorizon = Mathf.Max(tuning.predictionHorizon, tuning.shotHorizon);
            float baselineScore = ScoreOutcome(baseline);
            float mass = BallPhysics.Mass > 0f ? BallPhysics.Mass : 1f;

            float bestScore = float.NegativeInfinity;
            bool bestHitsOwnGoal = true;
            keys = Vector2Int.zero;

            for (int i = 0; i < ShotKeyCandidates.Length; i++)
            {
                Vector2Int candidate = ShotKeyCandidates[i];
                Vector2 velocity = state.BallVelocity + PulseDirectionFor(candidate) * (Abilities.PulseForce / mass);

                if (BallPhysics.MaxSpeed > 0f && velocity.magnitude > BallPhysics.MaxSpeed)
                    velocity = velocity.normalized * BallPhysics.MaxSpeed;

                CpuBallPredictor.Outcome outcome = CpuBallPredictor.Simulate(state.BallPosition, velocity, BallPhysics,
                    state.TargetGoal, state.OwnGoal, state.ArenaBounds, shotHorizon, tuning.simulationStep);

                float score = ScoreOutcome(outcome);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestHitsOwnGoal = outcome.ReachesOwnGoal;
                    keys = candidate;
                }
            }

            if (bestHitsOwnGoal)
                return false;

            if (baseline.ReachesOwnGoal)
                return true; // any safe touch is a save

            return bestScore >= baselineScore + tuning.minShotImprovement;
        }

        private static float ScoreOutcome(in CpuBallPredictor.Outcome outcome)
        {
            if (outcome.ReachesOwnGoal)
                return -100f + outcome.TimeToEvent; // later is (slightly) less bad

            if (outcome.ReachesTarget)
                return 20f - outcome.TimeToEvent; // sooner is better

            return -outcome.ClosestDistanceToTarget;
        }

        private static int Sign(float value)
        {
            if (value > 1e-4f) return 1;
            if (value < -1e-4f) return -1;
            return 0;
        }
    }
}
