using UnityEngine;

[RequireComponent(typeof(CarPart))]
public class EnginePart : MonoBehaviour
{
    public string engineName = "1.6 R4";

    [Tooltip("Maksymalny moment obrotowy w Nm")]
    public float maxTorque = 150f;

    public float idleRpm = 900f;
    public float maxRpm = 6500f;
    [Tooltip("Moment hamowania silnikiem w Nm przy maksymalnych obrotach, bez gazu")]
    public float engineBraking = 40f;

    [Tooltip("Oś X: obroty (0 = 0 RPM, 1 = max RPM). Oś Y: mnożnik momentu (1 = pełny moment).")]
    public AnimationCurve torqueCurve = new AnimationCurve(
        new Keyframe(0f, 0.6f),
        new Keyframe(0.55f, 1f),
        new Keyframe(1f, 0.7f));

    public float GetTorque(float rpm)
    {
        return maxTorque * torqueCurve.Evaluate(rpm / maxRpm);
    }
}