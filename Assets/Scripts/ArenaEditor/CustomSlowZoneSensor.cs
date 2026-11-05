using UnityEngine;

/// <summary>
/// Tracks whether the ball is inside a custom slow zone, with the same trigger rules as
/// <see cref="GoalSlowMotionZone"/> (enter sets it, exit clears it, only the "Ball" tag counts).
/// </summary>
public class CustomSlowZoneSensor : MonoBehaviour
{
    public bool BallInside { get; private set; }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Ball"))
            BallInside = true;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Ball"))
            BallInside = false;
    }

    void OnDisable()
    {
        BallInside = false;
    }
}
