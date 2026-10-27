using UnityEngine;
using Unity.Netcode;

public enum EngineFuelType
{
    Petrol,
    Diesel,
    TwoStroke
}

[RequireComponent(typeof(CarPart))]
public class EnginePart : NetworkBehaviour
{
    public string engineName = "1.6 R4";
    public EngineFuelType fuelType = EngineFuelType.Petrol;

    [Tooltip("Maksymalny moment obrotowy w Nm")]
    public float maxTorque = 150f;

    public float idleRpm = 900f;
    public float maxRpm = 6500f;

    [Tooltip("Moment hamowania silnikiem w Nm przy maksymalnych obrotach, bez gazu")]
    public float engineBraking = 40f;

    [Tooltip("Spalanie w litrach na godzinę przy pełnym gazie i maksymalnych obrotach")]
    public float maxFuelConsumption = 20f;
    [Tooltip("Ile °C na sekundę silnik się grzeje przy pełnym gazie i maksymalnych obrotach")]
    public float heatOutput = 4f;
    [Header("Elektryka")]
    [Tooltip("Ile % akumulatora rozrusznik pobiera na sekundę kręcenia")]
    public float starterDrain = 2f;

    [Tooltip("Ile % na sekundę alternator ładuje akumulator przy pracującym silniku")]
    public float alternatorOutput = 0.3f;

    [Tooltip("Oś X: obroty (0 = 0 RPM, 1 = max RPM). Oś Y: mnożnik momentu (1 = pełny moment).")]
    public AnimationCurve torqueCurve = new AnimationCurve(
        new Keyframe(0f, 0.6f),
        new Keyframe(0.55f, 1f),
        new Keyframe(1f, 0.7f));

    // Stan silnika 0-100%. Zmienia tylko host.
    public NetworkVariable<float> health = new NetworkVariable<float>(100f);
    // Temperatura silnika. Liczy kierowca, zapisuje host.
    public NetworkVariable<float> temperature = new NetworkVariable<float>(20f);
    [Header("Olej silnikowy")]
    [Tooltip("Ile litrów świeżego oleju zamienia się w zużyty na sekundę przy pełnym obciążeniu")]
    public float oilDegradeRate = 0.001f;

    [Tooltip("Ile litrów oleju silnik spala na sekundę przy pełnym obciążeniu (więcej, gdy zużyty)")]
    public float oilBurnRate = 0.0001f;

    public NetworkVariable<float> oilTemperature = new NetworkVariable<float>(20f);

    [HideInInspector] public FuelContainer sump; // miska olejowa

    // Dwusuw nie ma miski, olej jest w paliwie
    public bool UsesSumpOil => fuelType != EngineFuelType.TwoStroke;

    void Awake()
    {
        sump = GetComponent<FuelContainer>();
    }

    // 1 = pełno świeżego oleju, 0 = brak smarowania
    public float GetLubrication()
    {
        if (!UsesSumpOil) return 1f;
        if (sump == null || sump.capacity <= 0f) return 0f;

        FuelMix m = sump.contents.Value;
        float anyOil = m.oil + m.usedOil;
        float level = Mathf.Clamp01(anyOil / sump.capacity / 0.5f); // poniżej połowy smarowanie słabnie
        float fresh = anyOil > 0f ? m.oil / anyOil : 0f;
        return level * Mathf.Lerp(0.3f, 1f, fresh);
    }
    public float GetTorque(float rpm)
    {
        return maxTorque * torqueCurve.Evaluate(rpm / maxRpm);
    }

    public void ServerDamage(float amount)
    {
        if (amount <= 0f) return;
        health.Value = Mathf.Max(0f, health.Value - amount);
    }

    // quality: 1 = idealne paliwo, poniżej 0.2 silnik nie pracuje
    // damagePerSecond: ile % stanu silnika ubywa na sekundę (przy pełnych obrotach)
    public void EvaluateFuel(FuelMix mix, out float quality, out float damagePerSecond)
    {
        float petrol = mix.PetrolShare;
        float diesel = mix.DieselShare;
        float oil = mix.OilShare;

        quality = 1f;
        damagePerSecond = 0f;

        switch (fuelType)
        {
            case EngineFuelType.Petrol:
                quality = 1f - diesel * 3.3f - oil * 2f;
                damagePerSecond = diesel * 0.5f + oil * 0.2f;
                break;

            case EngineFuelType.Diesel:
                quality = 1f - petrol * 2.5f - oil * 1f;
                damagePerSecond = petrol * 2f; // benzyna niszczy pompę wtryskową
                break;

            case EngineFuelType.TwoStroke:
                quality = 1f - diesel * 3.3f;
                if (oil < 0.01f)
                    damagePerSecond = (0.01f - oil) * 300f; // za mało oleju: zatarcie
                else if (oil > 0.05f)
                    quality -= (oil - 0.05f) * 8f;          // za dużo oleju: dymi i traci moc
                damagePerSecond += diesel * 0.5f;
                break;
        }
        // Płyn chłodniczy w paliwie szkodzi każdemu silnikowi
        float coolant = mix.CoolantShare;
        quality -= coolant * 4f;
        damagePerSecond += coolant * 1f;
        // Zużyty olej w paliwie to śmieci
        quality -= mix.UsedOilShare * 3f;
        quality = Mathf.Clamp01(quality);
    }
}