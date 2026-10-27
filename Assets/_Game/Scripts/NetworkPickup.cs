using Unity.Netcode;

[UnityEngine.RequireComponent(typeof(Pickupable))]
public class NetworkPickup : NetworkBehaviour
{
    public const ulong NoHolder = ulong.MaxValue;

    // Kto trzyma przedmiot. Widzą to wszyscy, zmienia tylko host.
    public NetworkVariable<ulong> holderId = new NetworkVariable<ulong>(NoHolder);

    public bool IsHeldByOther =>
        holderId.Value != NoHolder && holderId.Value != NetworkManager.LocalClientId;

    public override void OnNetworkSpawn()
    {
        if (IsServer) NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestPickupServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (holderId.Value != NoHolder) return; // ktoś już trzyma

        holderId.Value = sender;
        if (OwnerClientId != sender)
            NetworkObject.ChangeOwnership(sender);
    }

    [ServerRpc(RequireOwnership = false)]
    public void ReleaseServerRpc(ServerRpcParams rpcParams = default)
    {
        if (holderId.Value == rpcParams.Receive.SenderClientId)
            holderId.Value = NoHolder;
    }

    // Gracz wyszedł z gry, trzymając przedmiot: zwolnij go
    void OnClientDisconnected(ulong clientId)
    {
        if (holderId.Value == clientId) holderId.Value = NoHolder;
    }
}