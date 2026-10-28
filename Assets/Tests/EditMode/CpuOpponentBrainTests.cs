using System.Collections.Generic;
using NeonStrikers.AI;
using NUnit.Framework;
using UnityEngine;

namespace NeonStrikers.Tests
{
    /// <summary>
    /// Decision tests for the computer opponent. Geometry mirrors Arena_01: the CPU (Red) defends the
    /// right goal and attacks the left (Blue) goal; the camera shows x in [-8.89, 8.89], y in [-5, 5].
    /// </summary>
    public class CpuOpponentBrainTests
    {
        static readonly Rect RedGoal = Rect.MinMaxRect(8.78f, -3.95f, 9.28f, 1.05f);   // CPU defends
        static readonly Rect BlueGoal = Rect.MinMaxRect(-9.2f, -3.84f, -8.7f, 1.16f);  // CPU attacks
        static readonly Rect Arena = Rect.MinMaxRect(-8.89f, -5f, 8.89f, 5f);
        const float Frame = 1f / 60f;

        static CpuBallPhysics NoGravity()
        {
            CpuBallPhysics physics = CpuBallPhysics.Default;
            physics.Gravity = Vector2.zero;
            return physics;
        }

        static CpuOpponentBrain NewBrain(CpuBallPhysics? physics = null, CpuTuning tuning = null)
        {
            return new CpuOpponentBrain(tuning ?? new CpuTuning(), physics ?? NoGravity(), CpuPlayerAbilities.Default);
        }

        static CpuWorldState State(Vector2 self, Vector2 ball, Vector2 ballVelocity = default, float time = 10f,
            float dt = Frame, Vector2 selfVelocity = default, Vector2? opponent = null, bool canAct = true,
            Rect? ownGoal = null, Rect? targetGoal = null)
        {
            return new CpuWorldState
            {
                Time = time,
                DeltaTime = dt,
                CanAct = canAct,
                SelfPosition = self,
                SelfVelocity = selfVelocity,
                SelfGrounded = true,
                BallPosition = ball,
                BallVelocity = ballVelocity,
                HasOpponent = opponent.HasValue,
                OpponentPosition = opponent ?? Vector2.zero,
                OwnGoal = ownGoal ?? RedGoal,
                TargetGoal = targetGoal ?? BlueGoal,
                ArenaBounds = Arena
            };
        }

        // ---------------------------------------------------------------- basics

        [Test]
        public void ReleasesAllKeysWhileGameplayIsPaused()
        {
            CpuOpponentBrain brain = NewBrain();
            CpuInputFrame frame = brain.Tick(State(new Vector2(6f, -4f), new Vector2(0f, -4f), canAct: false));

            Assert.AreEqual(0, frame.Horizontal);
            Assert.AreEqual(0, frame.Vertical);
            Assert.IsFalse(frame.Pulse);
            Assert.AreEqual(CpuIntent.Idle, brain.LastIntent);
        }

        [Test]
        public void OutputsAreAlwaysVirtualKeys()
        {
            var random = new System.Random(3);
            for (int i = 0; i < 300; i++)
            {
                CpuOpponentBrain brain = NewBrain(CpuBallPhysics.Default);
                CpuInputFrame frame = brain.Tick(State(
                    new Vector2(R(random, -8, 8), R(random, -4, 4)),
                    new Vector2(R(random, -8, 8), R(random, -4, 4)),
                    new Vector2(R(random, -12, 12), R(random, -12, 12)),
                    opponent: new Vector2(R(random, -8, 8), -4f)));

                Assert.That(frame.Horizontal, Is.InRange(-1, 1));
                Assert.That(frame.Vertical, Is.InRange(-1, 1));
            }
        }

        // ---------------------------------------------------------------- positioning

        [Test]
        public void ApproachesTheBallFromTheSideAwayFromTheBlueGoal()
        {
            CpuOpponentBrain brain = NewBrain();
            CpuInputFrame frame = brain.Tick(State(new Vector2(6f, -4f), new Vector2(0f, -3.8f)));

            Assert.AreEqual(CpuIntent.Attack, brain.LastIntent);
            Assert.AreEqual(-1, frame.Horizontal, "should run left towards the ball");
            Assert.Greater(brain.LastTarget.x, 0f, "should line up on the right of the ball (away from the Blue goal)");
        }

        [Test]
        public void GoesAroundToTheUsefulSideWhenStandingOnTheWrongSide()
        {
            CpuOpponentBrain brain = NewBrain();
            CpuInputFrame frame = brain.Tick(State(new Vector2(-3f, -4f), new Vector2(0f, -4f)));

            Assert.AreEqual(1, frame.Horizontal);
            Assert.Greater(brain.LastTarget.x, 0f);
        }

        [Test]
        public void DefendsWhenTheBallIsHeadingIntoTheRedGoal()
        {
            CpuOpponentBrain brain = NewBrain();
            CpuInputFrame frame = brain.Tick(State(new Vector2(2f, -4f), new Vector2(3f, -1f), new Vector2(11f, 0f)));

            Assert.AreEqual(CpuIntent.Defend, brain.LastIntent);
            Assert.Greater(brain.LastTarget.x, 3f, "should get between the ball and the Red goal");
            Assert.AreEqual(1, frame.Horizontal);
        }

        [Test]
        public void DefendsAFloorGoalLikeArena02()
        {
            Rect redFloorGoal = Rect.MinMaxRect(0.07f, -4.68f, 9.57f, -3.80f);
            Rect blueFloorGoal = Rect.MinMaxRect(-9.66f, -4.65f, 0.1f, -3.77f);
            CpuOpponentBrain brain = NewBrain(CpuBallPhysics.Default);

            CpuInputFrame frame = brain.Tick(State(new Vector2(1f, -3.6f), new Vector2(5f, 0f),
                ownGoal: redFloorGoal, targetGoal: blueFloorGoal));

            Assert.AreEqual(CpuIntent.Defend, brain.LastIntent);
            Assert.AreEqual(1, frame.Horizontal, "should run under the falling ball");
        }

        [Test]
        public void FallsBackToGuardWhenTheOpponentHasTheBall()
        {
            CpuOpponentBrain brain = NewBrain();
            CpuInputFrame frame = brain.Tick(State(new Vector2(-6f, -4f), new Vector2(-2.5f, -3.8f),
                opponent: new Vector2(-3f, -4f)));

            Assert.AreEqual(CpuIntent.Guard, brain.LastIntent);
            Assert.Greater(brain.LastTarget.x, -2.5f);
            Assert.Less(brain.LastTarget.x, RedGoal.xMin);
            Assert.AreEqual(1, frame.Horizontal);
        }

        [Test]
        public void MovementIsSmoothAndDoesNotFlickerAroundTheTarget()
        {
            CpuOpponentBrain brain = NewBrain();
            float x = 3f;
            var pressed = new List<int>();

            for (int i = 0; i < 60; i++)
            {
                CpuInputFrame frame = brain.Tick(State(new Vector2(x, -4f), new Vector2(0f, -4f), time: 10f + i * Frame));
                pressed.Add(frame.Horizontal);
                x += frame.Horizontal * 0.1f;
            }

            CollectionAssert.DoesNotContain(pressed, 1, "never reverses back and forth across the target");
            Assert.AreEqual(0, pressed[pressed.Count - 1], "settles with the keys released");
            Assert.Greater(pressed.FindAll(h => h == 0).Count, 10);
        }

        // ---------------------------------------------------------------- reacting to changes

        [Test]
        public void RetargetsImmediatelyWhenTheBallComesOutOfAPortal()
        {
            CpuOpponentBrain brain = NewBrain();
            Vector2 self = new Vector2(0f, -4f);

            CpuInputFrame before = brain.Tick(State(self, new Vector2(-7f, 3.5f), new Vector2(-8f, 0f), time: 10f));
            Assert.AreEqual(-1, before.Horizontal);
            Assert.Less(brain.LastTarget.x, 0f);
            int replans = brain.ReplanCount;

            // One frame later the ball pops out of the right portal exit, moving the other way.
            CpuInputFrame after = brain.Tick(State(self, new Vector2(7.99f, 3.64f), new Vector2(8f, 0f), time: 10f + Frame));

            Assert.AreEqual(replans + 1, brain.ReplanCount, "teleport should be detected");
            Assert.Greater(brain.LastTarget.x, 0f, "target must follow the ball's NEW position");
            Assert.AreEqual(1, after.Horizontal, "must turn around on the very next frame");
        }

        [Test]
        public void RetargetsWhenTheBallChangesDirection()
        {
            CpuOpponentBrain brain = NewBrain();
            Vector2 self = new Vector2(2f, -4f);

            brain.Tick(State(self, new Vector2(-2f, -1f), new Vector2(-6f, 0f), time: 10f));
            Vector2 oldTarget = brain.LastTarget;

            brain.Tick(State(self, new Vector2(-2.1f, -1f), new Vector2(6f, 0f), time: 10f + Frame));

            Assert.AreEqual(1, brain.ReplanCount);
            Assert.Greater(brain.LastTarget.x, oldTarget.x + 1f);
        }

        // ---------------------------------------------------------------- pulses

        [Test]
        public void PulsesTheBallTowardsTheBlueGoalWhenInReach()
        {
            CpuOpponentBrain brain = NewBrain();
            CpuInputFrame frame = brain.Tick(State(new Vector2(0.8f, -4f), new Vector2(0f, -3.8f)));

            Assert.IsTrue(frame.Pulse);
            Assert.AreEqual(-1, frame.Horizontal, "the pulse direction must point at the Blue goal");
        }

        [Test]
        public void NeverChoosesAPulseThatSendsTheBallIntoItsOwnGoal()
        {
            var random = new System.Random(7);
            int fired = 0;

            for (int i = 0; i < 400; i++)
            {
                CpuOpponentBrain brain = NewBrain(CpuBallPhysics.Default);
                Vector2 self = new Vector2(R(random, -8, 8), R(random, -4, 3));
                float angle = R(random, 0f, Mathf.PI * 2f);
                float radius = R(random, 0f, 1.7f);
                Vector2 ball = self + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Vector2 ballVelocity = new Vector2(R(random, -12, 12), R(random, -8, 8));

                CpuInputFrame frame = brain.Tick(State(self, ball, ballVelocity));
                if (!frame.Pulse || Vector2.Distance(self, ball) > brain.PulseReach)
                    continue;

                fired++;
                Vector2 velocity = ballVelocity + brain.PulseDirectionFor(new Vector2Int(frame.Horizontal, frame.Vertical)) * 12f;
                if (velocity.magnitude > brain.BallPhysics.MaxSpeed)
                    velocity = velocity.normalized * brain.BallPhysics.MaxSpeed; // same arithmetic as the brain
                var outcome = CpuBallPredictor.Simulate(ball, velocity, brain.BallPhysics, BlueGoal, RedGoal, Arena,
                    brain.Tuning.shotHorizon, brain.Tuning.simulationStep);

                Assert.IsFalse(outcome.ReachesOwnGoal, $"case {i}: pulse would score an own goal");
            }

            Assert.Greater(fired, 50, "the scenario should exercise plenty of shots");
        }

        [Test]
        public void PulseDirectionMatchesPlayerMovementPulseBall()
        {
            CpuOpponentBrain brain = NewBrain();

            // PlayerMovement.PulseBall: (inputDirection or up).normalized + up * 0.35, normalized.
            Assert.That(Vector2.Distance(brain.PulseDirectionFor(Vector2Int.zero), Vector2.up), Is.LessThan(1e-4f));
            Vector2 expectedLeft = (Vector2.left + Vector2.up * 0.35f).normalized;
            Assert.That(Vector2.Distance(brain.PulseDirectionFor(new Vector2Int(-1, 0)), expectedLeft), Is.LessThan(1e-4f));
            Vector2 expectedDiagonal = (new Vector2(1f, -1f).normalized + Vector2.up * 0.35f).normalized;
            Assert.That(Vector2.Distance(brain.PulseDirectionFor(new Vector2Int(1, -1)), expectedDiagonal), Is.LessThan(1e-4f));
        }

        [Test]
        public void LiftsTowardsABallHighAbove()
        {
            CpuOpponentBrain brain = NewBrain();
            CpuInputFrame frame = brain.Tick(State(new Vector2(6f, -4f), new Vector2(6f, 1f)));

            Assert.IsTrue(frame.Pulse);
            Assert.AreEqual(1, frame.Vertical);
        }

        [Test]
        public void DoesNotLiftAgainWhileStillRising()
        {
            CpuOpponentBrain brain = NewBrain();
            CpuInputFrame frame = brain.Tick(State(new Vector2(6f, -4f), new Vector2(6f, 1f), selfVelocity: new Vector2(0f, 5f)));

            Assert.IsFalse(frame.Pulse);
        }

        [Test]
        public void HopsWhenPushingIntoAWallWithoutMoving()
        {
            CpuOpponentBrain brain = NewBrain();
            float time = 0f;
            float firstPulse = -1f;

            for (int i = 0; i < 120 && firstPulse < 0f; i++)
            {
                CpuInputFrame frame = brain.Tick(State(new Vector2(0f, -4f), new Vector2(-6f, -3.8f), time: time));
                if (frame.Pulse)
                    firstPulse = time;
                time += Frame;
            }

            Assert.That(firstPulse, Is.InRange(0.45f, 0.7f));
        }

        // ---------------------------------------------------------------- pulse rate limit

        [Test]
        public void NeverPulsesMoreThanOnceEvery0_4SecondsOfGameTime()
        {
            List<float> pulses = RunWithBallAlwaysInReach(NewBrain(), Frame, 600, 1f);
            AssertPulseSpacing(pulses, 0.4f);
            Assert.GreaterOrEqual(pulses.Count, 20, "should still pulse regularly (about 2.5 per second)");
        }

        [Test]
        public void PulseCooldownCannotBeTunedBelow0_4Seconds()
        {
            var tuning = new CpuTuning { pulseCooldown = 0.05f };
            Assert.AreEqual(0.4f, tuning.EffectivePulseCooldown, 1e-6f);

            List<float> pulses = RunWithBallAlwaysInReach(NewBrain(tuning: tuning), Frame, 600, 1f);
            AssertPulseSpacing(pulses, 0.4f);
        }

        [Test]
        public void PulseLimitIsIndependentOfFrameRate()
        {
            List<float> pulses = RunWithBallAlwaysInReach(NewBrain(), 1f / 240f, 2400, 1f);
            AssertPulseSpacing(pulses, 0.4f);
        }

        [Test]
        public void PulseLimitUsesScaledGameTimeDuringSlowMotion()
        {
            // 600 frames at timeScale 0.01 is only 0.1 s of game time: one pulse at most.
            List<float> pulses = RunWithBallAlwaysInReach(NewBrain(), Frame, 600, 0.01f);
            Assert.LessOrEqual(pulses.Count, 1);
        }

        [Test]
        public void PulseGateAllowsOnePulsePerInterval()
        {
            var gate = new CpuPulseGate(0.1f);
            Assert.AreEqual(0.4f, gate.Interval, 1e-6f, "interval is clamped to the 0.4 s minimum");

            // Use a zero origin to test the exact stored cooldown interval.
            Assert.IsTrue(gate.TryConsume(0f));
            Assert.IsFalse(gate.TryConsume(0f));
            Assert.IsFalse(gate.TryConsume(0.39f));
            Assert.IsTrue(gate.TryConsume(0.4f));
        }

        // ---------------------------------------------------------------- prediction

        [Test]
        public void PredictorFollowsGravityDampingAndSpeedCap()
        {
            CpuBallPhysics physics = CpuBallPhysics.Default;
            Vector2 fallen = CpuBallPredictor.PositionAt(Vector2.zero, Vector2.zero, 1f, physics, default, 0.02f);
            Assert.Less(fallen.y, -2f, "gravity pulls the ball down");
            Assert.Greater(fallen.y, -0.5f * 9.81f * 0.6f - 0.01f, "damping makes it fall a bit slower than free fall");

            Vector2 capped = CpuBallPredictor.Step(new Vector2(100f, 0f), physics, 0.02f);
            Assert.LessOrEqual(capped.magnitude, physics.MaxSpeed + 1e-4f);
        }

        [Test]
        public void PredictorDetectsWhichGoalTheBallReaches()
        {
            CpuBallPhysics physics = NoGravity();
            var intoBlue = CpuBallPredictor.Simulate(new Vector2(0f, -1f), new Vector2(-12f, 0f), physics, BlueGoal, RedGoal, Arena, 2f, 0.02f);
            Assert.IsTrue(intoBlue.ReachesTarget);
            Assert.IsFalse(intoBlue.ReachesOwnGoal);

            var intoRed = CpuBallPredictor.Simulate(new Vector2(0f, -1f), new Vector2(12f, 0f), physics, BlueGoal, RedGoal, Arena, 2f, 0.02f);
            Assert.IsTrue(intoRed.ReachesOwnGoal);
            Assert.IsFalse(intoRed.ReachesTarget);
        }

        // ---------------------------------------------------------------- helpers

        static List<float> RunWithBallAlwaysInReach(CpuOpponentBrain brain, float frameTime, int frames, float timeScale)
        {
            var pulses = new List<float>();
            float time = 0f;

            for (int i = 0; i < frames; i++)
            {
                float dt = frameTime * timeScale;
                CpuInputFrame frame = brain.Tick(State(new Vector2(0f, -4f), new Vector2(-0.5f, -3.8f), time: time, dt: dt));
                if (frame.Pulse)
                    pulses.Add(time);
                time += dt;
            }

            return pulses;
        }

        static void AssertPulseSpacing(List<float> pulses, float minimum)
        {
            for (int i = 1; i < pulses.Count; i++)
            {
                float gap = pulses[i] - pulses[i - 1];
                Assert.GreaterOrEqual(gap, minimum - 1e-4f, $"pulses {i - 1} and {i} only {gap:0.000}s apart");
            }
        }

        static float R(System.Random random, float min, float max)
        {
            return (float)(min + random.NextDouble() * (max - min));
        }
    }
}
