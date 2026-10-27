using UnityEngine;
using Unity.Netcode;

public class CarParts : NetworkBehaviour
{
    public const ulong None = ulong.MaxValue;

    public PartSlot[] slots;

    [Tooltip("Część montowana przy starcie w slocie o tym samym numerze (może być puste)")]
    public GameObject[] startingParts;

    private NetworkList<ulong> mounted;
    private CarPart[] mountedParts;

    void Awake()
    {
        mounted = new NetworkList<ulong>();
        mountedParts = new CarPart[slots.Length];
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        if (mounted.Count == 0)
            for (int i = 0; i < slots.Length; i++) mounted.Add(None);

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

    // Np. GetMounted<EnginePart>() zwraca zamontowany silnik albo null
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

    public bool TryFindEmptySlot(Vector3 position, PartType type, out int index, out float distance)
    {
        index = -1;
        distance = float.MaxValue;

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
        if (slot < 0 || slot >= mounted.Count) return;
        if (mounted[slot] != None) return;
        if (!partRef.TryGet(out NetworkObject no)) return;

        CarPart part = no.GetComponent<CarPart>();
        if (part == null || part.type != slots[slot].acceptedType) return; // zły typ części
        if (mounted.Contains(no.NetworkObjectId)) return;

        NetworkPickup pickup = no.GetComponent<NetworkPickup>();
        if (pickup.holderId.Value != rpcParams.Receive.SenderClientId) return;

        pickup.holderId.Value = NetworkPickup.NoHolder;
        mounted[slot] = no.NetworkObjectId;
    }

    // ---------- U wszystkich graczy: ustaw części na miejscach ----------

    void LateUpdate()
    {
        if (!IsSpawned) return;

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

                WheelPart wheel = part.GetComponent<WheelPart>();
                if (wheel != null && slots[i].wheelCollider != null)
                    slots[i].wheelCollider.radius = wheel.radius;
            }

            if (slots[i].wheelCollider != null)
                slots[i].wheelCollider.enabled = (part != null);

            if (part != null)
                part.transform.SetPositionAndRotation(slots[i].transform.position, slots[i].transform.rotation);
        }
    }
    public CarPart GetMountedPart(PartType type)
    {
        foreach (CarPart part in mountedParts)
            if (part != null && part.type == type) return part;
        return null;
    }
}