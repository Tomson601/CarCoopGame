using System.Collections.Generic;
using Unity.Netcode;

[System.Serializable]
public struct FuelMix : INetworkSerializable, System.IEquatable<FuelMix>
{
    public float petrol;
    public float diesel;
    public float oil;
    public float coolant;
    public float usedOil;

    public float Total => petrol + diesel + oil + coolant + usedOil;
    public float PetrolShare => Total > 0f ? petrol / Total : 0f;
    public float DieselShare => Total > 0f ? diesel / Total : 0f;
    public float OilShare => Total > 0f ? oil / Total : 0f;
    public float CoolantShare => Total > 0f ? coolant / Total : 0f;
    public float UsedOilShare => Total > 0f ? usedOil / Total : 0f;

    public FuelMix Scaled(float factor)
    {
        return new FuelMix
        {
            petrol = petrol * factor,
            diesel = diesel * factor,
            oil = oil * factor,
            coolant = coolant * factor,
            usedOil = usedOil * factor
        };
    }

    public FuelMix Plus(FuelMix other)
    {
        return new FuelMix
        {
            petrol = petrol + other.petrol,
            diesel = diesel + other.diesel,
            oil = oil + other.oil,
            coolant = coolant + other.coolant,
            usedOil = usedOil + other.usedOil
        };
    }

    public FuelMix Minus(FuelMix other)
    {
        return new FuelMix
        {
            petrol = UnityEngine.Mathf.Max(0f, petrol - other.petrol),
            diesel = UnityEngine.Mathf.Max(0f, diesel - other.diesel),
            oil = UnityEngine.Mathf.Max(0f, oil - other.oil),
            coolant = UnityEngine.Mathf.Max(0f, coolant - other.coolant),
            usedOil = UnityEngine.Mathf.Max(0f, usedOil - other.usedOil)
        };
    }

    public FuelMix ClampedTo(float capacity)
    {
        return Total > capacity ? Scaled(capacity / Total) : this;
    }

    public string Describe()
    {
        if (Total < 0.001f) return "pusty";

        if (diesel < 0.001f && coolant < 0.001f && usedOil < 0.001f
            && petrol > 0.001f && oil > 0.001f && OilShare < 0.1f)
            return $"mieszanka 1:{petrol / oil:0}";

        List<string> parts = new List<string>();
        if (petrol > 0.001f) parts.Add($"benzyna {PetrolShare * 100f:0}%");
        if (diesel > 0.001f) parts.Add($"diesel {DieselShare * 100f:0}%");
        if (oil > 0.001f) parts.Add($"olej {OilShare * 100f:0}%");
        if (usedOil > 0.001f) parts.Add($"zużyty olej {UsedOilShare * 100f:0}%");
        if (coolant > 0.001f) parts.Add($"płyn chł. {CoolantShare * 100f:0}%");
        return string.Join(", ", parts);
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref petrol);
        serializer.SerializeValue(ref diesel);
        serializer.SerializeValue(ref oil);
        serializer.SerializeValue(ref coolant);
        serializer.SerializeValue(ref usedOil);
    }

    public bool Equals(FuelMix other)
    {
        return petrol == other.petrol && diesel == other.diesel && oil == other.oil
            && coolant == other.coolant && usedOil == other.usedOil;
    }
}