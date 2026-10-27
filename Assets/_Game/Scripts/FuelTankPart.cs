using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(CarPart))]
public class FuelTankPart : NetworkBehaviour
{
    public float capacity = 40f;
    public float startFuel = 10f;

    // Zmienia tylko host
    public NetworkVariable<float> fuel = new NetworkVariable<float>(0f);

    public bool HasFuel => fuel.Value > 0.001f;

    public override void OnNetworkSpawn()
    {
        if (IsServer) fuel.Value = Mathf.Min(startFuel, capacity);
    }

    // Wywoływane tylko na hoście. Zwraca, ile faktycznie weszło.
    public float ServerAdd(float liters)
    {
        float space = capacity - fuel.Value;
        float added = Mathf.Clamp(liters, 0f, space);
        fuel.Value += added;
        return added;
    }

    public void ServerConsume(float liters)
    {
        fuel.Value = Mathf.Max(0f, fuel.Value - liters);
    }
}