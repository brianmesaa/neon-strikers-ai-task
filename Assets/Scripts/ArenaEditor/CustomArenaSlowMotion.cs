using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Goal slow motion for the slow zones of a custom arena.
///
/// Every stock arena has exactly two slow zones (one in front of each goal), each with an enabled
/// <see cref="GoalSlowMotionZone"/> (slowTimeScale 0.01 / 0.009, transitionSpeed 10). Each of them lerps
/// Time.timeScale every frame - towards slowTimeScale while the ball is inside it, towards normalTimeScale
/// otherwise. Because the ball can only be inside one of the two zones, the zone holding the ball and the
/// other zone pull against each other, and the stock game settles at roughly half speed while the ball is
/// in a zone (not at 0.01).
///
/// A custom arena can have any number of slow zones, so reusing the script on every zone would change that
/// behaviour (one zone would freeze the game at 0.01, many zones would cancel it out). This component
/// reproduces the stock two-zone pull exactly, with the values copied from the stock zone:
///   timeScale = Lerp(timeScale, ballInsideAnyZone ? slow : normal, k)   // the zone holding the ball
///   timeScale = Lerp(timeScale, normal, k)                               // the opposite stock zone
/// with k = unscaledDeltaTime * transitionSpeed, exactly as GoalSlowMotionZone.Update computes it.
/// </summary>
public class CustomArenaSlowMotion : MonoBehaviour
{
    public float slowTimeScale = 0.01f;
    public float normalTimeScale = 1f;
    public float transitionSpeed = 10f;

    readonly List<CustomSlowZoneSensor> sensors = new List<CustomSlowZoneSensor>();

    public IReadOnlyList<CustomSlowZoneSensor> Sensors => sensors;

    public bool BallInsideAnyZone
    {
        get
        {
            foreach (CustomSlowZoneSensor sensor in sensors)
            {
                if (sensor != null && sensor.isActiveAndEnabled && sensor.BallInside)
                    return true;
            }

            return false;
        }
    }

    public void Configure(GoalSlowMotionZone reference, IEnumerable<CustomSlowZoneSensor> zoneSensors)
    {
        if (reference != null)
        {
            slowTimeScale = reference.slowTimeScale;
            normalTimeScale = reference.normalTimeScale;
            transitionSpeed = reference.transitionSpeed;
        }

        sensors.Clear();
        if (zoneSensors != null)
            sensors.AddRange(zoneSensors);
    }

    /// <summary>One frame of the stock two-zone time scale update.</summary>
    public static float Step(float timeScale, bool ballInside, float slow, float normal, float speed, float unscaledDeltaTime)
    {
        float k = unscaledDeltaTime * speed;
        timeScale = Mathf.Lerp(timeScale, ballInside ? slow : normal, k);
        timeScale = Mathf.Lerp(timeScale, normal, k);
        return timeScale;
    }

    void Update()
    {
        if (sensors.Count == 0)
            return;

        Time.timeScale = Step(Time.timeScale, BallInsideAnyZone, slowTimeScale, normalTimeScale, transitionSpeed, Time.unscaledDeltaTime);
    }
}
