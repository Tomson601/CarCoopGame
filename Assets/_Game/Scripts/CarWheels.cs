using UnityEngine;
using Unity.Netcode;

public class CarWheels : NetworkBehaviour
{
    public const ulong None = ulong.MaxValue;

    public WheelSlot[] slots;
    public GameObject wheelPrefab;
    public bool spawnWithWheels = true;

    // Które koło (NetworkObjectId) siedzi w którym slocie
    private NetworkList<ulong> mountedWheels;
    private WheelPart[] mountedParts;

    void Awake()
    {
        mountedWheels = new NetworkList<ulong>();
        mountedParts = new WheelPart[slots.Length];
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        if (mountedWheels.Count == 0)
            for (int i = 0; i < slots.Length; i++) mountedWheels.Add(None);

        if (!spawnWithWheels) return;

        // Host tworzy koła i od razu je montuje
        for (int i = 0; i < slots.Length; i++)
        {
            GameObject go = Instantiate(wheelPrefab, slots[i].transform.position, slots[i].transform.rotation);
            NetworkObject no = go.GetComponent<NetworkObject>();
            no.Spawn();

            WheelPart part = go.GetComponent<WheelPart>();
            part.SetMounted(true, this, i); // od razu, żeby koło nie zderzyło się z autem
            mountedParts[i] = part;
            mountedWheels[i] = no.NetworkObjectId;
        }
    }

    ulong GetMounted(int i)
    {
        return (mountedWheels != null && i < mountedWheels.Count) ? mountedWheels[i] : None;
    }

    public bool TryFindEmptySlot(Vector3 position, out int index, out float distance)
    {
        index = -1;
        distance = float.MaxValue;

        for (int i = 0; i < slots.Length; i++)
        {
            if (GetMounted(i) != None) continue;

            float d = Vector3.Distance(slots[i].transform.position, position);
            if (d < slots[i].snapDistance && d < distance)
            {
                index = i;
                distance = d;
            }
        }
        return index != -1;
    }

    // ---------- Prośby do hosta ----------

    [ServerRpc(RequireOwnership = false)]
    public void RequestDetachServerRpc(int slot, ServerRpcParams rpcParams = default)
    {
        if (slot < 0 || slot >= mountedWheels.Count) return;

        ulong id = mountedWheels[slot];
        if (id == None) return;

        mountedWheels[slot] = None;

        // Daj zdjęte koło do ręki temu, kto je odkręcił
        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject no))
            no.GetComponent<NetworkPickup>().ServerGiveTo(rpcParams.Receive.SenderClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestMountServerRpc(int slot, NetworkObjectReference wheelRef, ServerRpcParams rpcParams = default)
    {
        if (slot < 0 || slot >= mountedWheels.Count) return;
        if (mountedWheels[slot] != None) return;               // slot zajęty
        if (!wheelRef.TryGet(out NetworkObject no)) return;
        if (no.GetComponent<WheelPart>() == null) return;      // to nie koło
        if (mountedWheels.Contains(no.NetworkObjectId)) return; // już zamontowane

        // Montować może tylko ten, kto trzyma koło
        NetworkPickup pickup = no.GetComponent<NetworkPickup>();
        if (pickup.holderId.Value != rpcParams.Receive.SenderClientId) return;

        pickup.holderId.Value = NetworkPickup.NoHolder;
        mountedWheels[slot] = no.NetworkObjectId;
    }

    // ---------- U wszystkich graczy: ustaw koła na miejscach ----------

    void LateUpdate()
    {
        if (!IsSpawned) return;

        for (int i = 0; i < slots.Length; i++)
        {
            ulong id = GetMounted(i);
            WheelPart part = mountedParts[i];

            // Koło zostało zdjęte
            if (part != null && part.net.NetworkObjectId != id)
            {
                part.SetMounted(false, null, -1);
                mountedParts[i] = null;
                part = null;
            }

            // Koło zostało założone
            if (id != None && part == null &&
                NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject no))
            {
                part = no.GetComponent<WheelPart>();
                part.SetMounted(true, this, i);
                mountedParts[i] = part;
                slots[i].wheelCollider.radius = part.radius;
            }

            // Bez koła WheelCollider jest wyłączony i róg auta opada
            slots[i].wheelCollider.enabled = (part != null);

            if (part != null)
                part.transform.SetPositionAndRotation(slots[i].transform.position, slots[i].transform.rotation);
        }
    }
}