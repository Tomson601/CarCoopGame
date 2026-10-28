using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

// Zapis i wczytywanie gry. Działa tylko u hosta.
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    public string fileName = "save.json";
    [Tooltip("Co ile sekund automatyczny zapis")]
    public float autosaveInterval = 120f;

    private float nextAutosave;
    private string message;
    private float messageTime = -10f;

    string SavePath => Path.Combine(Application.persistentDataPath, fileName);
    public bool HasSave => File.Exists(SavePath);
    bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        if (!IsHost) return;

        if (Keyboard.current.f5Key.wasPressedThisFrame) SaveGame();

        // Pierwsza klatka tylko ustawia licznik, żeby nie zapisać przed wczytaniem
        if (nextAutosave <= 0f) nextAutosave = Time.time + autosaveInterval;
        else if (Time.time >= nextAutosave)
        {
            SaveGame();
            nextAutosave = Time.time + autosaveInterval;
        }
    }

    void OnApplicationQuit()
    {
        if (IsHost) SaveGame();
    }

    // ---------- Zapis ----------

    public void SaveGame()
    {
        if (!IsHost || GameState.Instance == null) return;

        SaveFile file = new SaveFile
        {
            seed = GameState.Instance.Seed,
            lootedPois = WorldGenerator.GetLootedPois()
        };

        FirstPersonController host = FindLocalPlayer();
        file.spawnPoint = host != null ? host.transform.position : GameState.Instance.SpawnPoint;

        foreach (SaveableObject so in FindObjectsByType<SaveableObject>(FindObjectsSortMode.None))
            if (so.IsSpawnedNetworkObject) file.objects.Add(so.Capture());

        File.WriteAllText(SavePath, JsonUtility.ToJson(file, true));
        ShowMessage($"Zapisano grę ({file.objects.Count} obiektów)");
        Debug.Log("Zapis: " + SavePath);
    }

    // ---------- Wczytywanie ----------

    public bool LoadGame()
    {
        if (!IsHost || !HasSave) return false;

        SaveFile file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(SavePath));

        // 1. Świat: najpierw obrobione miejsca, potem seed (przebudowa świata)
        WorldGenerator.SetLootedPois(file.lootedPois);
        GameState.Instance.ServerSetWorld(file.seed, file.spawnPoint);

        // 2. Usuń obiekty startowej sceny (auto, części, kanistry)
        foreach (SaveableObject so in FindObjectsByType<SaveableObject>(FindObjectsSortMode.None))
        {
            NetworkObject no = so.GetComponent<NetworkObject>();
            if (no.IsSpawned) no.Despawn(true);
            else Destroy(so.gameObject);
        }

        // 3. Utwórz zapisane obiekty
        Dictionary<string, SaveableObject> byId = new Dictionary<string, SaveableObject>();
        foreach (ObjectSave d in file.objects)
        {
            GameObject prefab = FindPrefab(d.prefabId);
            if (prefab == null)
            {
                Debug.LogWarning($"Wczytywanie: nie znaleziono prefabu '{d.prefabId}'");
                continue;
            }

            GameObject go = Instantiate(prefab, d.position, d.rotation);

            // Części przyjdą z zapisu, więc auto i felgi nie tworzą swoich startowych
            foreach (CarParts cp in go.GetComponentsInChildren<CarParts>())
                cp.spawnStartingParts = false;

            go.GetComponent<NetworkObject>().Spawn();

            SaveableObject save = go.GetComponent<SaveableObject>();
            save.SaveId = d.saveId;
            save.Restore(d);
            byId[d.saveId] = save;
        }

        // 4. Zamontuj części tam, gdzie były
        foreach (ObjectSave d in file.objects)
        {
            if (d.mounted == null || d.mounted.Count == 0) continue;
            if (!byId.TryGetValue(d.saveId, out SaveableObject owner)) continue;

            CarParts cp = owner.GetComponent<CarParts>();
            if (cp == null) continue;

            for (int i = 0; i < d.mounted.Count; i++)
            {
                if (string.IsNullOrEmpty(d.mounted[i])) continue;
                if (byId.TryGetValue(d.mounted[i], out SaveableObject part))
                    cp.ServerMountLoaded(i, part.GetComponent<CarPart>());
            }
        }

        // 5. Teren pod wszystkim od razu, a host na swoje miejsce
        WorldGenerator.Instance.BuildNow();

        FirstPersonController host = FindLocalPlayer();
        if (host != null) host.TeleportTo(file.spawnPoint + Vector3.up * 0.5f);

        ShowMessage("Wczytano grę");
        return true;
    }

    // ---------- Pomocnicze ----------

    GameObject FindPrefab(string id)
    {
        foreach (NetworkPrefab np in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
        {
            if (np.Prefab == null) continue;
            SaveableObject so = np.Prefab.GetComponent<SaveableObject>();
            if (so != null && so.prefabId == id) return np.Prefab;
        }
        return null;
    }

    FirstPersonController FindLocalPlayer()
    {
        foreach (FirstPersonController p in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
            if (p.IsOwner) return p;
        return null;
    }

    void ShowMessage(string text)
    {
        message = text;
        messageTime = Time.time;
    }

    void OnGUI()
    {
        if (Time.time - messageTime < 2.5f)
            GUI.Label(new Rect(Screen.width - 320f, 20f, 300f, 25f), message);
    }
}