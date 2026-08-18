using UnityEngine;

/// <summary>
/// Spins the object continuously at runtime. Play-mode only, like any MonoBehaviour Update.
/// </summary>
[AddComponentMenu("Demo/Continuous Rotator")]
[DisallowMultipleComponent]
public class ContinuousRotator : MonoBehaviour
{
    [Tooltip("Rotation speed in degrees per second. Negative reverses direction.")]
    public float degreesPerSecond = 45f;

    [Tooltip("Axis to spin around. Y (0,1,0) is the usual turntable spin.")]
    public Vector3 axis = Vector3.up;

    [Tooltip("Self spins around the object's own axis; World uses the scene axis regardless of parent rotation.")]
    public Space space = Space.Self;

    [Tooltip("Keep spinning at the same rate even if Time.timeScale changes.")]
    public bool useUnscaledTime;

    [Tooltip("Offset each object's starting angle by a random amount, so a crowd does not spin in lockstep.")]
    public bool randomiseStartAngle;

    void Start()
    {
        if (randomiseStartAngle)
            transform.Rotate(axis.normalized, Random.Range(0f, 360f), space);
    }

    void Update()
    {
        var dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        transform.Rotate(axis.normalized, degreesPerSecond * dt, space);
    }
}
