using UnityEngine;
using Unity.Netcode;

public enum TireType
{
    Road,
    Offroad
}

[RequireComponent(typeof(CarPart))]
public class TirePart : NetworkBehaviour
{
    public string tireName = "Opona szosowa";
    public TireType tireType = TireType.Road;
    public float outerRadius = 0.35f;

    [Header("Przyczepność na nawierzchniach (1 = pełna)")]
    public float gripAsphalt = 1f;
    public float gripDirt = 0.55f;
    public float gripGrass = 0.5f;

    [Header("Zużycie")]
    [Tooltip("Ogólne tempo zużycia. Większe = opona szybciej się ściera.")]
    public float wearRate = 1f;
    public float wearOnAsphalt = 1f;
    public float wearOnDirt = 1.5f;
    public float wearOnGrass = 1.2f;

    // Stan opony 0-100%. Zmienia tylko host.
    public NetworkVariable<float> health = new NetworkVariable<float>(100f);

    public float GetGrip(SurfaceType surface)
    {
        float grip = surface switch
        {
            SurfaceType.Asphalt => gripAsphalt,
            SurfaceType.Grass => gripGrass,
            _ => gripDirt
        };

        // Starta opona trzyma gorzej
        return grip * Mathf.Lerp(0.6f, 1f, health.Value / 100f);
    }

    public float GetWearMultiplier(SurfaceType surface)
    {
        return surface switch
        {
            SurfaceType.Asphalt => wearOnAsphalt,
            SurfaceType.Grass => wearOnGrass,
            _ => wearOnDirt
        };
    }

    public void ServerWear(float amount)
    {
        if (amount <= 0f) return;
        health.Value = Mathf.Max(0f, health.Value - amount);
    }
}