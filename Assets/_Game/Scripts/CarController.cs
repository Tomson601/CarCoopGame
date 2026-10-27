using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class CarController : NetworkBehaviour
{
    public const ulong NoDriver = ulong.MaxValue;

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

    [Header("Miejsca")]
    public Transform driverSeat;
    public Transform exitPoint;

    // Kto prowadzi. Widzą to wszyscy, zmienia tylko host.
    public NetworkVariable<ulong> driverId = new NetworkVariable<ulong>(NoDriver);

    public bool HasDriver => driverId.Value != NoDriver;
    public bool IsDrivenByMe => IsOwner && driverId.Value == NetworkManager.LocalClientId;

    private Rigidbody rb;
    private FirstPersonController seatedPlayer;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0, -0.3f, 0);
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    public override void OnNetworkSpawn()
    {
        driverId.OnValueChanged += OnDriverChanged;
        if (IsServer) NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        driverId.OnValueChanged -= OnDriverChanged;
        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    // ---------- Wsiadanie i wysiadanie ----------

    [ServerRpc(RequireOwnership = false)]
    public void RequestEnterServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (HasDriver) return; // miejsce zajęte

        driverId.Value = sender;
        if (OwnerClientId != sender)
            NetworkObject.ChangeOwnership(sender);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestExitServerRpc(ServerRpcParams rpcParams = default)
    {
        if (driverId.Value == rpcParams.Receive.SenderClientId)
            driverId.Value = NoDriver;
    }

    void OnClientDisconnected(ulong clientId)
    {
        if (driverId.Value == clientId) driverId.Value = NoDriver;
    }

    void OnDriverChanged(ulong oldId, ulong newId)
    {
        // Kierowca wysiadł: wypuść go z fotela (u wszystkich graczy)
        if (seatedPlayer != null)
        {
            seatedPlayer.SetSeat(null, exitPoint);
            seatedPlayer = null;
        }
    }

    void LateUpdate()
    {
        // Posadź kierowcę w fotelu (u wszystkich, także u tych, którzy dołączyli później)
        if (HasDriver && seatedPlayer == null)
        {
            seatedPlayer = FindPlayer(driverId.Value);
            if (seatedPlayer != null) seatedPlayer.SetSeat(driverSeat);
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
        // Fizykę liczy tylko właściciel auta
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