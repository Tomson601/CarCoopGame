using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkPickup))]
public class Jerrycan : NetworkBehaviour
{
    public float capacity = 20f;
    public float startLiters = 20f;
    public float pourRate = 2f; // litry na sekundę

    public NetworkVariable<float> liters = new NetworkVariable<float>(0f);

    public override void OnNetworkSpawn()
    {
        if (IsServer) liters.Value = Mathf.Min(startLiters, capacity);
    }

    [ServerRpc(RequireOwnership = false)]
    public void PourIntoServerRpc(NetworkObjectReference tankRef, float amount)
    {
        if (!tankRef.TryGet(out NetworkObject no)) return;

        FuelTankPart tank = no.GetComponent<FuelTankPart>();
        if (tank == null) return;

        // Ochrona przed tankowaniem z drugiego końca mapy
        if (Vector3.Distance(transform.position, tank.transform.position) > 3f) return;

        float give = Mathf.Min(amount, liters.Value);
        float added = tank.ServerAdd(give);
        liters.Value -= added;
    }
}