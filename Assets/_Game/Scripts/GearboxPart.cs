using UnityEngine;

[RequireComponent(typeof(CarPart))]
public class GearboxPart : MonoBehaviour
{
    public string gearboxName = "5-biegowa manualna";
    public bool automatic = false;

    [Tooltip("Przełożenia biegów od 1 w górę")]
    public float[] gearRatios = { 3.5f, 2.1f, 1.4f, 1.0f, 0.8f };
    public float reverseRatio = 3.2f;
    public float finalDrive = 4.0f;

    [Header("Tylko automat")]
    public float shiftUpRpm = 5500f;
    public float shiftDownRpm = 2000f;
}