using UnityEngine;
using Unity.Netcode;

// Wspólny stan gry przesyłany wszystkim graczom: seed świata i miejsce startu.
// Jeden obiekt na scenie, z Network Object.
public class GameState : NetworkBehaviour
{
    public static GameState Instance { get; private set; }

    private NetworkVariable<int> seed = new NetworkVariable<int>(12345);
    private NetworkVariable<Vector3> spawnPoint = new NetworkVariable<Vector3>(new Vector3(0f, 1f, 0f));

    public int Seed => seed.Value;
    public Vector3 SpawnPoint => spawnPoint.Value;

    void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        seed.OnValueChanged += OnSeedChanged;
        ApplySeed(seed.Value); // klient po dołączeniu od razu buduje właściwy świat
    }

    public override void OnNetworkDespawn()
    {
        seed.OnValueChanged -= OnSeedChanged;
    }

    void OnSeedChanged(int oldSeed, int newSeed)
    {
        ApplySeed(newSeed);
    }

    void ApplySeed(int value)
    {
        if (WorldGenerator.Instance != null) WorldGenerator.Instance.SetSeed(value);
    }

    // Tylko na hoście: nowa gra albo wczytanie zapisu
    public void ServerSetWorld(int newSeed, Vector3 newSpawnPoint)
    {
        seed.Value = newSeed;
        spawnPoint.Value = newSpawnPoint;
        ApplySeed(newSeed);
    }
}