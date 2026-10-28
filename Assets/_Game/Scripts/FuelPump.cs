using UnityEngine;

public enum PumpFuel
{
    Petrol,
    Diesel
}

// Dystrybutor na stacji. Zwykły obiekt świata (nie sieciowy), więc host sprawdza go po pozycji.
public class FuelPump : MonoBehaviour
{
    public PumpFuel fuel = PumpFuel.Petrol;
    [Tooltip("Litry na sekundę")]
    public float flowRate = 3f;

    public string Label => fuel == PumpFuel.Petrol ? "benzyna" : "diesel";

    public FuelMix GetMix(float liters)
    {
        return fuel == PumpFuel.Petrol ? new FuelMix { petrol = liters } : new FuelMix { diesel = liters };
    }

    public static FuelPump FindNearest(Vector3 position, float maxDistance)
    {
        FuelPump best = null;
        float bestDist = maxDistance;
        foreach (FuelPump pump in FindObjectsByType<FuelPump>(FindObjectsSortMode.None))
        {
            float d = Vector3.Distance(pump.transform.position, position);
            if (d < bestDist)
            {
                best = pump;
                bestDist = d;
            }
        }
        return best;
    }
}