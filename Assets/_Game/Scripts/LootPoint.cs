using UnityEngine;

// Punkt w prefabie miejsca, w którym host może utworzyć przedmiot.
public class LootPoint : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        public GameObject prefab;
        public float weight = 1f;
    }

    [Tooltip("Szansa, że w tym punkcie cokolwiek się pojawi")]
    [Range(0f, 1f)] public float chance = 0.7f;
    public Entry[] items;

    public GameObject PickRandom()
    {
        if (items == null || items.Length == 0 || Random.value > chance) return null;

        float total = 0f;
        foreach (Entry e in items) if (e.prefab != null) total += e.weight;
        if (total <= 0f) return null;

        float r = Random.value * total;
        foreach (Entry e in items)
        {
            if (e.prefab == null) continue;
            if (r < e.weight) return e.prefab;
            r -= e.weight;
        }
        return null;
    }

    // Kulka widoczna tylko w edytorze, żeby łatwo ustawiać punkty
    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.3f);
    }
}