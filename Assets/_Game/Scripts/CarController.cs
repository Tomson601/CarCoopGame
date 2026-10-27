using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class CarController : NetworkBehaviour
{
    public const ulong NoOne = ulong.MaxValue;

    [System.Serializable]
    public class Seat
    {
        public Transform point;      // gdzie siedzi postać
        public Transform exitPoint;  // gdzie się pojawia po wyjściu
    }

    [Header("Koła - fizyka")]
    public WheelCollider frontLeft;
    public WheelCollider frontRight;
    public WheelCollider rearLeft;
    public WheelCollider rearRight;

    [Header("Koła - wygląd")]
    public Transform frontLeftVisual;
    public Transform frontRightVisual;
    public Transform rearLeftVisual;
    public Transform rearRightVisual;

    [Header("Silnik i hamulce")]
    public float motorTorque = 1500f;
    public float brakeTorque = 3000f;
    public float maxSteerAngle = 30f;

    [Header("Fotele (0 = kierowca)")]
    public Seat[] seats;

    // Kto siedzi w którym fotelu. Widzą wszyscy, zmienia tylko host.
    private NetworkList<ulong> occupants;

    private FirstPersonController[] seatedPlayers;
    private Rigidbody rb;

    public ulong DriverId => GetOccupant(0);
    public bool HasDriver => DriverId != NoOne;
    public bool IsDrivenByMe => IsOwner && DriverId == NetworkManager.LocalClientId;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0, -0.3f, 0);
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        occupants = new NetworkList<ulong>(); // NetworkList musi powstać w Awake
        seatedPlayers = new FirstPersonController[seats.Length];
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            if (occupants.Count == 0)
                for (int i = 0; i < seats.Length; i++) occupants.Add(NoOne);

            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    ulong GetOccupant(int index)
    {
        // Lista może być jeszcze pusta, zanim dotrze od hosta
        return (occupants != null && index < occupants.Count) ? occupants[index] : NoOne;
    }

    int FindSeatOf(ulong clientId)
    {
        for (int i = 0; i < occupants.Count; i++)
            if (occupants[i] == clientId) return i;
        return -1;
    }

    // ---------- Fotele ----------

    public int GetNearestSeatIndex(Vector3 point)
    {
        int best = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < seats.Length; i++)
        {
            float d = Vector3.Distance(seats[i].point.position, point);
            if (d < bestDist) { best = i; bestDist = d; }
        }
        return best;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestSeatServerRpc(int preferredSeat, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (FindSeatOf(sender) != -1) return; // już siedzi

        // Najpierw fotel, na który patrzył, potem pierwszy wolny
        int seat = -1;
        if (preferredSeat >= 0 && preferredSeat < occupants.Count && occupants[preferredSeat] == NoOne)
            seat = preferredSeat;
        else
            seat = FindSeatOf(NoOne);

        if (seat == -1) return; // wszystkie zajęte

        occupants[seat] = sender;

        // Kierowca przejmuje auto
        if (seat == 0 && OwnerClientId != sender)
            NetworkObject.ChangeOwnership(sender);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestExitServerRpc(ServerRpcParams rpcParams = default)
    {
        int seat = FindSeatOf(rpcParams.Receive.SenderClientId);
        if (seat != -1) occupants[seat] = NoOne;
    }

    void OnClientDisconnected(ulong clientId)
    {
        int seat = FindSeatOf(clientId);
        if (seat != -1) occupants[seat] = NoOne;
    }

    void LateUpdate()
    {
        // U wszystkich graczy: posadź i wypuść postacie zgodnie z listą
        for (int i = 0; i < seats.Length; i++)
        {
            ulong occupant = GetOccupant(i);
            FirstPersonController player = seatedPlayers[i];

            // Ktoś wstał z fotela
            if (player != null && player.OwnerClientId != occupant)
            {
                player.SetSeat(null, seats[i].exitPoint);
                seatedPlayers[i] = null;
                player = null;
            }

            // Ktoś usiadł
            if (occupant != NoOne && player == null)
            {
                player = FindPlayer(occupant);
                if (player != null)
                {
                    player.SetSeat(seats[i].point);
                    seatedPlayers[i] = player;
                }
            }
        }
    }

    FirstPersonController FindPlayer(ulong clientId)
    {
        foreach (FirstPersonController p in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
            if (p.OwnerClientId == clientId) return p;
        return null;
    }

    // ---------- Jazda ----------

    void FixedUpdate()
    {
        if (!IsSpawned || !IsOwner) return;

        float throttle = 0f;
        float steer = 0f;
        bool handbrake = false;

        if (IsDrivenByMe)
        {
            Keyboard kb = Keyboard.current;
            if (kb.wKey.isPressed) throttle += 1f;
            if (kb.sKey.isPressed) throttle -= 1f;
            if (kb.dKey.isPressed) steer += 1f;
            if (kb.aKey.isPressed) steer -= 1f;
            handbrake = kb.spaceKey.isPressed;
        }

        float steerAngle = steer * maxSteerAngle;
        frontLeft.steerAngle = steerAngle;
        frontRight.steerAngle = steerAngle;

        rearLeft.motorTorque = throttle * motorTorque;
        rearRight.motorTorque = throttle * motorTorque;

        float brake = (handbrake || !HasDriver) ? brakeTorque : 0f;
        frontLeft.brakeTorque = brake;
        frontRight.brakeTorque = brake;
        rearLeft.brakeTorque = brake;
        rearRight.brakeTorque = brake;
    }

    void Update()
    {
        UpdateWheelVisual(frontLeft, frontLeftVisual);
        UpdateWheelVisual(frontRight, frontRightVisual);
        UpdateWheelVisual(rearLeft, rearLeftVisual);
        UpdateWheelVisual(rearRight, rearRightVisual);
    }

    void UpdateWheelVisual(WheelCollider col, Transform visual)
    {
        if (!col.enabled) return;
        col.GetWorldPose(out Vector3 pos, out Quaternion rot);
        visual.SetPositionAndRotation(pos, rot);
    }

    void OnGUI()
    {
        if (!IsDrivenByMe) return;
        float kmh = rb.linearVelocity.magnitude * 3.6f;
        GUI.Label(new Rect(20, 20, 200, 30), $"{kmh:0} km/h");
    }
}