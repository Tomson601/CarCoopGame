using UnityEngine;
using Unity.Netcode;

public class FuelContainer : NetworkBehaviour
{
    public float capacity = 20f;
    public FuelMix startContents = new FuelMix { petrol = 10f };

    [Tooltip("Czy da się z tego przelewać, trzymając w rękach")]
    public bool canPourFrom = true;
    public float pourRate = 2f; // litry na sekundę

    public NetworkVariable<FuelMix> contents = new NetworkVariable<FuelMix>();

    public float Liters => contents.Value.Total;
    public bool HasFuel => Liters > 0.001f;

    public override void OnNetworkSpawn()
    {
        if (IsServer) contents.Value = startContents.ClampedTo(capacity);
    }

    // ---------- Tylko na hoście ----------

    public FuelMix ServerAdd(FuelMix mix)
    {
        float space = capacity - Liters;
        if (space <= 0f || mix.Total <= 0f) return default;

        FuelMix added = mix.ClampedTo(space);
        contents.Value = contents.Value.Plus(added);
        return added;
    }

    public FuelMix ServerTake(float liters)
    {
        FuelMix current = contents.Value;
        float take = Mathf.Min(liters, current.Total);
        if (take <= 0f) return default;

        // Zabieramy proporcjonalnie ze wszystkich składników
        FuelMix taken = current.Scaled(take / current.Total);
        contents.Value = current.Minus(taken);
        return taken;
    }
    // Zamienia świeży olej w zużyty (starzenie się oleju w silniku)
    public void ServerConvertOil(float liters)
    {
        FuelMix c = contents.Value;
        float amount = Mathf.Min(liters, c.oil);
        if (amount <= 0f) return;

        c.oil -= amount;
        c.usedOil += amount;
        contents.Value = c;
    }

    // ---------- Prośby od graczy ----------

    [ServerRpc(RequireOwnership = false)]
    public void PourIntoServerRpc(NetworkObjectReference targetRef, float amount)
    {
        if (!canPourFrom) return;
        if (!targetRef.TryGet(out NetworkObject no)) return;

        FuelContainer target = no.GetComponent<FuelContainer>();
        if (target == null || target == this) return;
        if (Vector3.Distance(transform.position, target.transform.position) > 3f) return;

        // Bierzemy tylko tyle, ile zmieści cel
        float space = target.capacity - target.Liters;
        FuelMix taken = ServerTake(Mathf.Min(amount, space));
        target.ServerAdd(taken);
    }

    [ServerRpc(RequireOwnership = false)]
    public void DumpServerRpc(float amount)
    {
        if (!canPourFrom) return;
        ServerTake(amount); // wylane na ziemię znika
    }
}