using System;
using UnityEngine;
using Unity.Netcode;

// Dodaj do każdego prefabu, który ma być zapisywany (auto, części, kanistry).
[RequireComponent(typeof(NetworkObject))]
public class SaveableObject : MonoBehaviour
{
    [Tooltip("Nazwa prefabu. Uzupełnij z menu: Gra -> Uzupełnij ID zapisu")]
    public string prefabId;

    private string saveId;

    // Unikalny identyfikator tego konkretnego obiektu (np. "to koło", a nie "jakieś koło")
    public string SaveId
    {
        get
        {
            if (string.IsNullOrEmpty(saveId)) saveId = Guid.NewGuid().ToString("N");
            return saveId;
        }
        set => saveId = value;
    }

    public bool IsSpawnedNetworkObject => TryGetComponent(out NetworkObject no) && no.IsSpawned;

    public ObjectSave Capture()
    {
        ObjectSave d = new ObjectSave
        {
            saveId = SaveId,
            prefabId = prefabId,
            position = transform.position,
            rotation = transform.rotation
        };

        if (TryGetComponent(out FuelContainer liquid))
        {
            d.hasLiquid = true;
            d.liquid = liquid.contents.Value;
        }

        if (TryGetComponent(out EnginePart engine))
        {
            d.engineHealth = engine.health.Value;
            d.engineTemp = engine.temperature.Value;
            d.oilTemp = engine.oilTemperature.Value;
        }

        if (TryGetComponent(out BatteryPart battery)) d.batteryCharge = battery.charge.Value;
        if (TryGetComponent(out TirePart tire)) d.tireHealth = tire.health.Value;

        if (TryGetComponent(out CarEngine carEngine) && TryGetComponent(out CarController car))
        {
            d.hasCarState = true;
            d.gear = carEngine.Gear;
            d.handbrake = car.HandbrakeOn;
        }

        if (TryGetComponent(out CarParts parts))
        {
            for (int i = 0; i < parts.slots.Length; i++)
            {
                CarPart part = parts.GetMountedPartAt(i);
                SaveableObject partSave = part != null ? part.GetComponent<SaveableObject>() : null;
                d.mounted.Add(partSave != null ? partSave.SaveId : "");
            }
        }

        return d;
    }

    // Wywoływane na hoście po Spawn()
    public void Restore(ObjectSave d)
    {
        if (d.hasLiquid && TryGetComponent(out FuelContainer liquid)) liquid.contents.Value = d.liquid;

        if (TryGetComponent(out EnginePart engine))
        {
            if (d.engineHealth >= 0f) engine.health.Value = d.engineHealth;
            if (d.engineTemp >= 0f) engine.temperature.Value = d.engineTemp;
            if (d.oilTemp >= 0f) engine.oilTemperature.Value = d.oilTemp;
        }

        if (d.batteryCharge >= 0f && TryGetComponent(out BatteryPart battery)) battery.charge.Value = d.batteryCharge;
        if (d.tireHealth >= 0f && TryGetComponent(out TirePart tire)) tire.health.Value = d.tireHealth;

        if (d.hasCarState && TryGetComponent(out CarEngine carEngine) && TryGetComponent(out CarController car))
        {
            carEngine.ServerRestore(d.gear);
            car.ServerRestoreHandbrake(d.handbrake);
        }
    }
}