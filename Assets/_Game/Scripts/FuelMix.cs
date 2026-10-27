using System.Collections.Generic;
using Unity.Netcode;

[System.Serializable]
public struct FuelMix : INetworkSerializable, System.IEquatable<FuelMix>
{
    public float petrol;
    public float diesel;
    public float oil;

    public float Total => petrol + diesel + oil;
    public float PetrolShare => Total > 0f ? petrol / Total : 0f;
    public float DieselShare => Total > 0f ? diesel / Total : 0f;
    public float OilShare => Total > 0f ? oil / Total : 0f;

    public FuelMix Scaled(float factor)
    {
        return new FuelMix { petrol = petrol * factor, diesel = diesel * factor, oil = oil * factor };
    }

    public FuelMix Plus(FuelMix other)
    {
        return new FuelMix
        {
            petrol = petrol + other.petrol,
            diesel = diesel + other.diesel,
            oil = oil + other.oil
        };
    }

    public FuelMix Minus(FuelMix other)
    {
        return new FuelMix
        {
            petrol = UnityEngine.Mathf.Max(0f, petrol - other.petrol),
            diesel = UnityEngine.Mathf.Max(0f, diesel - other.diesel),
            oil = UnityEngine.Mathf.Max(0f, oil - other.oil)
        };
    }

    public FuelMix ClampedTo(float capacity)
    {
        return Total > capacity ? Scaled(capacity / Total) : this;
    }

    public string Describe()
    {
        if (Total < 0.001f) return "pusty";

        // Benzyna z odrobiną oleju to mieszanka dwusuwowa
        if (diesel < 0.001f && petrol > 0.001f && oil > 0.001f && OilShare < 0.1f)
            return $"mieszanka 1:{petrol / oil:0}";

        List<string> parts = new List<string>();
        if (petrol > 0.001f) parts.Add($"benzyna {PetrolShare * 100f:0}%");
        if (diesel > 0.001f) parts.Add($"diesel {DieselShare * 100f:0}%");
        if (oil > 0.001f) parts.Add($"olej {OilShare * 100f:0}%");
        return string.Join(", ", parts);
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref petrol);
        serializer.SerializeValue(ref diesel);
        serializer.SerializeValue(ref oil);
    }

    public bool Equals(FuelMix other)
    {
        return petrol == other.petrol && diesel == other.diesel && oil == other.oil;
    }
}