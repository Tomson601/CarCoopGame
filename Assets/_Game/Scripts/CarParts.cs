using UnityEngine;
using Unity.Netcode;

public class CarParts : NetworkBehaviour
{
    public const ulong None = ulong.MaxValue;

    public PartSlot[] slots;

    [Tooltip("Część montowana przy starcie w slocie o tym samym numerze (może być puste)")]
    public GameObject[] startingParts;
    // Przy wczytywaniu gry części przychodzą z zapisu, więc startowych nie tworzymy
    [HideInInspector] public bool spawnStartingParts = true;
    private NetworkList<ulong> mounted;
    private CarPart[] mountedParts;
    private CarPart ownerPart; // jeśli te sloty należą do części (np. felga), to ta część

    public CarPart OwnerPart => ownerPart;

    // Sloty części, która sama jest zamontowana (np. felga na aucie), są zablokowane
    public bool LockedByParent => ownerPart != null && ownerPart.IsMounted;

    void Awake()
    {
        mounted = new NetworkList<ulong>();
        mountedParts = new CarPart[slots.Length];
        ownerPart = GetComponent<CarPart>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        if (mounted.Count == 0)
            for (int i = 0; i < slots.Length; i++) mounted.Add(None);
        if (!spawnStartingParts) return;
        for (int i = 0; i < slots.Length; i++)
        {
            if (startingParts == null || i >= startingParts.Length || startingParts[i] == null) continue;

            GameObject go = Instantiate(startingParts[i], slots[i].transform.position, slots[i].transform.rotation);
            NetworkObject no = go.GetComponent<NetworkObject>();
            no.Spawn();

            CarPart part = go.GetComponent<CarPart>();
            part.SetMounted(true, this, i);
            mountedParts[i] = part;
            mounted[i] = no.NetworkObjectId;
        }
    }

    ulong GetMountedId(int i)
    {
        return (mounted != null && i < mounted.Count) ? mounted[i] : None;
    }

    // ---------- Szukanie części ----------

    public T GetMounted<T>() where T : Component
    {
        foreach (CarPart part in mountedParts)
        {
            if (part == null) continue;
            T component = part.GetComponent<T>();
            if (component != null) return component;
        }
        return null;
    }

    public CarPart GetMountedPart(PartType type)
    {
        foreach (CarPart part in mountedParts)
            if (part != null && part.type == type) return part;
        return null;
    }

    public CarPart GetMountedPartAt(int slot)
    {
        return (slot >= 0 && slot < mountedParts.Length) ? mountedParts[slot] : null;
    }

    public int FindMountedSlot(PartType type)
    {
        for (int i = 0; i < mountedParts.Length; i++)
            if (mountedParts[i] != null && mountedParts[i].type == type) return i;
        return -1;
    }

    public CarPart GetMountedOnCollider(WheelCollider col)
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].wheelCollider == col) return mountedParts[i];
        return null;
    }

    public bool TryFindEmptySlot(Vector3 position, PartType type, out int index, out float distance)
    {
        index = -1;
        distance = float.MaxValue;
        if (LockedByParent) return false;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].acceptedType != type) continue;
            if (GetMountedId(i) != None) continue;

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
        if (LockedByParent) return;
        if (slot < 0 || slot >= mounted.Count) return;

        ulong id = mounted[slot];
        if (id == None) return;

        mounted[slot] = None;

        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject no))
            no.GetComponent<NetworkPickup>().ServerGiveTo(rpcParams.Receive.SenderClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestMountServerRpc(int slot, NetworkObjectReference partRef, ServerRpcParams rpcParams = default)
    {
        if (LockedByParent) return;
        if (slot < 0 || slot >= mounted.Count) return;
        if (mounted[slot] != None) return;
        if (!partRef.TryGet(out NetworkObject no)) return;

        CarPart part = no.GetComponent<CarPart>();
        if (part == null || part.type != slots[slot].acceptedType) return;
        if (mounted.Contains(no.NetworkObjectId)) return;

        NetworkPickup pickup = no.GetComponent<NetworkPickup>();
        if (pickup.holderId.Value != rpcParams.Receive.SenderClientId) return;

        pickup.holderId.Value = NetworkPickup.NoHolder;
        mounted[slot] = no.NetworkObjectId;
    }
    // Tylko na hoście, przy wczytywaniu gry: zamontuj część bez sprawdzania, kto ją trzyma
    public void ServerMountLoaded(int slot, CarPart part)
    {
        if (!IsServer || part == null || slot < 0 || slot >= mounted.Count) return;

        mounted[slot] = part.net.NetworkObjectId;
        part.SetMounted(true, this, slot);
        mountedParts[slot] = part;
    }
    // ---------- U wszystkich graczy: ustaw części na miejscach ----------

    void LateUpdate()
    {
        if (IsSpawned) UpdateMounts();
    }

    public void UpdateMounts()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            ulong id = GetMountedId(i);
            CarPart part = mountedParts[i];

            if (part != null && part.net.NetworkObjectId != id)
            {
                part.SetMounted(false, null, -1);
                mountedParts[i] = null;
                part = null;
            }

            if (id != None && part == null &&
                NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject no))
            {
                part = no.GetComponent<CarPart>();
                part.SetMounted(true, this, i);
                mountedParts[i] = part;
            }

            if (slots[i].wheelCollider != null)
            {
                slots[i].wheelCollider.enabled = (part != null);

                // Promień koła zależy od opony (albo jej braku), u wszystkich graczy
                if (part != null)
                {
                    WheelPart wheel = part.GetComponent<WheelPart>();
                    if (wheel != null) slots[i].wheelCollider.radius = wheel.GetEffectiveRadius();
                }
            }

            if (part != null)
            {
                part.transform.SetPositionAndRotation(slots[i].transform.position, slots[i].transform.rotation);

                // Część z własnymi slotami (felga z oponą) od razu ustawia swoje części, bez opóźnienia klatki
                CarParts nested = part.GetComponent<CarParts>();
                if (nested != null && nested.IsSpawned) nested.UpdateMounts();
            }
        }
    }
}