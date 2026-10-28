using UnityEngine;
using Unity.Netcode;

// Akumulator auta: rozładowywanie przez rozrusznik, ładowanie z alternatora.
// Zmiany liczy właściciel auta, raz na sekundę wysyła je do hosta.
public class CarElectrics : NetworkBehaviour
{
    private CarParts parts;
    private float pendingDelta;
    private float flushTimer;

    GameTuning Tuning => GameTuning.Instance;

    void Awake()
    {
        parts = GetComponent<CarParts>();
    }

    public BatteryPart GetBattery()
    {
        CarPart part = parts.GetMountedPart(PartType.Battery);
        return part != null ? part.GetComponent<BatteryPart>() : null;
    }

    public bool HasBattery => GetBattery() != null;

    // Naładowanie wraz ze zmianami, których host jeszcze nie dostał
    public float GetCharge()
    {
        BatteryPart battery = GetBattery();
        return battery != null ? Mathf.Clamp(battery.charge.Value + pendingDelta, 0f, 100f) : 0f;
    }

    // 0-1: jak mocno kręci rozrusznik
    public float GetStarterStrength()
    {
        if (!HasBattery) return 0f;
        return Mathf.Clamp01((GetCharge() - Tuning.batteryEmptyLevel)
                             / (Tuning.batteryFullCrankLevel - Tuning.batteryEmptyLevel));
    }

    // Czy jest prąd na iskrę (silnik benzynowy)
    public bool HasSpark => HasBattery && GetCharge() > Tuning.minSparkCharge;

    public void Drain(float percent)
    {
        if (HasBattery && GetCharge() > 0f) pendingDelta -= percent;
    }

    public void Charge(float percent)
    {
        if (HasBattery) pendingDelta += percent;
    }

    // Wywoływane co FixedUpdate u właściciela auta
    public void Tick(float dt)
    {
        flushTimer += dt;
        if (flushTimer < 1f) return;

        if (pendingDelta != 0f) SyncChargeServerRpc(pendingDelta);
        pendingDelta = 0f;
        flushTimer = 0f;
    }

    [ServerRpc]
    void SyncChargeServerRpc(float delta)
    {
        BatteryPart battery = GetBattery();
        if (battery != null) battery.ServerAddCharge(delta);
    }
}