using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(CarPart))]
public class BatteryPart : NetworkBehaviour
{
    public string batteryName = "Akumulator 45Ah";

    [Range(0f, 100f)] public float startCharge = 80f;

    [Tooltip("Większy akumulator = wolniej się rozładowuje (i wolniej ładuje)")]
    public float capacityFactor = 1f;

    // Naładowanie 0-100%. Zmienia tylko host.
    public NetworkVariable<float> charge = new NetworkVariable<float>(0f);

    public override void OnNetworkSpawn()
    {
        if (IsServer) charge.Value = startCharge;
    }

    public void ServerAddCharge(float delta)
    {
        charge.Value = Mathf.Clamp(charge.Value + delta / capacityFactor, 0f, 100f);
    }
}