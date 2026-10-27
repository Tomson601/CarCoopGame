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
        public Transform point;
        public Transform exitPoint;
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

    [Header("Hamulce i skręt")]
    public float brakeTorque = 3000f;
    public float handbrakeTorque = 5000f;
    public float maxSteerAngle = 30f;

    [Header("Napęd")]
    [Range(0.5f, 1f)] public float drivetrainEfficiency = 0.85f;
    [Header("Opory")]
    [Tooltip("Opór powietrza. Większy = niższa prędkość maksymalna.")]
    public float airDrag = 0.4f;

    [Header("Fotele (0 = kierowca)")]
    public Seat[] seats;

    private NetworkList<ulong> occupants;
    private NetworkVariable<float> syncedSteer = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private FirstPersonController[] seatedPlayers;
    private Rigidbody rb;
    private CarParts parts;
    private float[] wheelSpin = new float[4];
    private Vector3 lastPosition;

    // Stan napędu, liczony u właściciela auta
    // Bieg widoczny w sieci i zachowany po zmianie kierowcy: -1 = R, 0 = N, 1..n
    private NetworkVariable<int> gear = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private float engineRpm = 0f;
    private float lastShiftTime;

    public ulong DriverId => GetOccupant(0);
    public bool HasDriver => DriverId != NoOne;
    public bool IsDrivenByMe => IsOwner && DriverId == NetworkManager.LocalClientId;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        parts = GetComponent<CarParts>();
        rb.centerOfMass = new Vector3(0, -0.3f, 0);
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        occupants = new NetworkList<ulong>();
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
        if (FindSeatOf(sender) != -1) return;

        int seat = -1;
        if (preferredSeat >= 0 && preferredSeat < occupants.Count && occupants[preferredSeat] == NoOne)
            seat = preferredSeat;
        else
            seat = FindSeatOf(NoOne);

        if (seat == -1) return;

        occupants[seat] = sender;

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
        for (int i = 0; i < seats.Length; i++)
        {
            ulong occupant = GetOccupant(i);
            FirstPersonController player = seatedPlayers[i];

            if (player != null && player.OwnerClientId != occupant)
            {
                player.SetSeat(null, seats[i].exitPoint);
                seatedPlayers[i] = null;
                player = null;
            }

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
        float footBrake = 0f;
        float steer = 0f;
        bool handbrake = false;

        if (IsDrivenByMe)
        {
            Keyboard kb = Keyboard.current;
            if (kb.wKey.isPressed) throttle = 1f;
            if (kb.sKey.isPressed) footBrake = 1f;
            if (kb.dKey.isPressed) steer += 1f;
            if (kb.aKey.isPressed) steer -= 1f;
            handbrake = kb.spaceKey.isPressed;
        }

        // Skręt
        float steerAngle = steer * maxSteerAngle;
        frontLeft.steerAngle = steerAngle;
        frontRight.steerAngle = steerAngle;

        if (Mathf.Abs(syncedSteer.Value - steerAngle) > 0.5f)
            syncedSteer.Value = steerAngle;

        // Napęd na tylne koła
        float driveTorque = CalculateDriveTorque(throttle);
        rearLeft.motorTorque = driveTorque * 0.5f;
        rearRight.motorTorque = driveTorque * 0.5f;

        // Hamulce: zwykły na wszystkie koła, ręczny na tył
        float brake = HasDriver ? footBrake * brakeTorque : brakeTorque;
        frontLeft.brakeTorque = brake;
        frontRight.brakeTorque = brake;

        float rearBrake = handbrake ? handbrakeTorque : brake;
        rearLeft.brakeTorque = rearBrake;
        rearRight.brakeTorque = rearBrake;

        // Opór powietrza rośnie z kwadratem prędkości
        Vector3 v = rb.linearVelocity;
        rb.AddForce(-v * v.magnitude * airDrag);
    }

    float CalculateDriveTorque(float throttle)
    {
        EnginePart engine = parts.GetMounted<EnginePart>();
        GearboxPart box = parts.GetMounted<GearboxPart>();

        if (engine == null)
        {
            engineRpm = 0f;
            return 0f;
        }

        // Bez skrzyni zawsze luz. Przy skrzyni z mniejszą liczbą biegów - przytnij bieg.
        int g = box == null ? 0 : Mathf.Clamp(gear.Value, -1, box.gearRatios.Length);
        if (g != gear.Value) gear.Value = g;

        // Luz: silnik kręci się swobodnie od gazu
        if (g == 0)
        {
            float target = engine.idleRpm + throttle * (engine.maxRpm - engine.idleRpm);
            engineRpm = Mathf.Lerp(engineRpm, target, Time.fixedDeltaTime * 3f);
            return 0f;
        }

        float ratio = g > 0 ? box.gearRatios[g - 1] : -box.reverseRatio;
        float totalRatio = ratio * box.finalDrive;

        float wheelRpm = (rearLeft.rpm + rearRight.rpm) * 0.5f;
        engineRpm = Mathf.Max(Mathf.Abs(wheelRpm * totalRatio), engine.idleRpm);

        // Automat sam zmienia biegi do przodu
        if (box.automatic && g >= 1 && Time.time - lastShiftTime > 0.8f)
        {
            if (engineRpm > box.shiftUpRpm && g < box.gearRatios.Length)
            {
                gear.Value = g + 1;
                lastShiftTime = Time.time;
            }
            else if (engineRpm < box.shiftDownRpm && g > 1)
            {
                gear.Value = g - 1;
                lastShiftTime = Time.time;
            }
        }

        // Moment od gazu. Ogranicznik: powyżej max RPM silnik odcina paliwo.
        float torque = engineRpm < engine.maxRpm ? engine.GetTorque(engineRpm) * throttle : 0f;

        // Hamowanie silnikiem: tym mocniejsze, im wyższe obroty i mniej gazu
        float rpmFactor = Mathf.InverseLerp(engine.idleRpm, engine.maxRpm, engineRpm);
        float braking = engine.engineBraking * rpmFactor * (1f - throttle);
        if (engineRpm > engine.maxRpm)
            braking += engine.engineBraking * 3f; // przekręcony silnik po redukcji hamuje mocno

        // Hamowanie działa przeciwnie do kierunku obrotu kół
        float rollDir = wheelRpm > 1f ? 1f : (wheelRpm < -1f ? -1f : 0f);

        float driveAtWheels = torque * totalRatio * drivetrainEfficiency;
        float brakeAtWheels = braking * Mathf.Abs(totalRatio) * rollDir;

        return driveAtWheels - brakeAtWheels;
    }

    void HandleShiftInput()
    {
        GearboxPart box = parts.GetMounted<GearboxPart>();
        if (box == null) return;

        Keyboard kb = Keyboard.current;
        int maxGear = box.gearRatios.Length;
        int g = gear.Value;

        if (kb.leftShiftKey.wasPressedThisFrame)
        {
            if (box.automatic) { if (g <= 0) g++; }   // R -> N -> D
            else g = Mathf.Min(g + 1, maxGear);
        }

        if (kb.leftCtrlKey.wasPressedThisFrame)
        {
            if (box.automatic) g = g >= 1 ? 0 : -1;    // D -> N -> R
            else g = Mathf.Max(g - 1, -1);
        }

        if (g != gear.Value) gear.Value = g;
    }

    // ---------- Wygląd kół ----------

    void Update()
    {
        if (IsSpawned && IsDrivenByMe) HandleShiftInput();

        Vector3 velocity = (transform.position - lastPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
        lastPosition = transform.position;
        float forwardSpeed = Vector3.Dot(velocity, transform.forward);

        bool simulated = !IsSpawned || IsOwner;

        UpdateWheelVisual(frontLeft, frontLeftVisual, 0, true, simulated, forwardSpeed);
        UpdateWheelVisual(frontRight, frontRightVisual, 1, true, simulated, forwardSpeed);
        UpdateWheelVisual(rearLeft, rearLeftVisual, 2, false, simulated, forwardSpeed);
        UpdateWheelVisual(rearRight, rearRightVisual, 3, false, simulated, forwardSpeed);
    }

    void UpdateWheelVisual(WheelCollider col, Transform visual, int index, bool isFront, bool simulated, float forwardSpeed)
    {
        if (!col.enabled) return;

        col.GetWorldPose(out Vector3 pos, out Quaternion rot);

        if (!simulated)
        {
            wheelSpin[index] += forwardSpeed / col.radius * Mathf.Rad2Deg * Time.deltaTime;
            float steer = isFront ? syncedSteer.Value : 0f;
            rot = transform.rotation * Quaternion.Euler(wheelSpin[index], steer, 0f);
        }

        visual.SetPositionAndRotation(pos, rot);
    }

    // ---------- HUD ----------

    void OnGUI()
    {
        if (!IsDrivenByMe) return;

        EnginePart engine = parts.GetMounted<EnginePart>();
        GearboxPart box = parts.GetMounted<GearboxPart>();

        float kmh = rb.linearVelocity.magnitude * 3.6f;
        int g = gear.Value;
        string gearText = g == -1 ? "R"
                        : g == 0 ? "N"
                        : (box != null && box.automatic ? "D" + g : g.ToString());

        GUI.Label(new Rect(20, 20, 400, 25), $"{kmh:0} km/h");

        // Obroty na czerwono przy redline
        if (engine != null && engineRpm > engine.maxRpm * 0.9f) GUI.color = Color.red;
        GUI.Label(new Rect(20, 45, 400, 25),
            engine != null ? $"{engine.engineName}  |  {engineRpm:0} RPM" : "BRAK SILNIKA");
        GUI.color = Color.white;

        GUI.Label(new Rect(20, 70, 400, 25),
            box != null ? $"{box.gearboxName}  |  Bieg: {gearText}" : "BRAK SKRZYNI");
        GUI.Label(new Rect(20, 95, 500, 25),
            "W gaz, S hamulec, Spacja ręczny, Shift/Ctrl bieg góra/dół");
    }
}