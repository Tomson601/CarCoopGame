using UnityEngine;

[RequireComponent(typeof(CarPart))]
[RequireComponent(typeof(FuelContainer))]
public class RadiatorPart : MonoBehaviour
{
    public string radiatorName = "Chłodnica standardowa";
    public float coolingPower = 1f;

    [HideInInspector] public FuelContainer liquid;

    void Awake()
    {
        liquid = GetComponent<FuelContainer>();
    }

    // 1 = pełna chłodnica czystego płynu. Inne płyny w środku nie chłodzą.
    public float CoolantFactor =>
        liquid.capacity > 0f ? Mathf.Clamp01(liquid.contents.Value.coolant / liquid.capacity) : 0f;
}