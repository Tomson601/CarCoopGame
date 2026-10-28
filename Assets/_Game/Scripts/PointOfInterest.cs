using UnityEngine;
using Unity.Netcode;

// Miejsce do odkrycia (stacja, porzucone auto, szopa). Dodaj na obiekt główny prefabu.
// Oś +Z prefabu (wejście) jest obracana w stronę drogi.
public class PointOfInterest : MonoBehaviour
{
    public string poiName = "Miejsce";
    [Tooltip("Jak często pojawia się w porównaniu z innymi miejscami")]
    public float weight = 1f;
    [Tooltip("Promień wyrównanego terenu wokół środka")]
    public float flatRadius = 12f;
    [Tooltip("Odległość środka miejsca od krawędzi drogi")]
    public float roadOffset = 10f;

    [HideInInspector] public int poiId;

    private bool lootChecked;

    void Update()
    {
        if (lootChecked) return;

        // Łup tworzy tylko host, raz na całą grę dla każdego miejsca
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        lootChecked = true;
        if (!WorldGenerator.MarkPoiLooted(poiId)) return; // już kiedyś tu był łup

        foreach (LootPoint point in GetComponentsInChildren<LootPoint>())
        {
            GameObject prefab = point.PickRandom();
            if (prefab == null) continue;

            GameObject item = Instantiate(prefab, point.transform.position, point.transform.rotation);
            item.GetComponent<NetworkObject>().Spawn();
        }
    }
}