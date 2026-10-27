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
    [Tooltip("Współczynnik oporu toczenia opon. Typowo 0.01-0.02 na asfalcie, więcej na ziemi i piasku.")]
    public float rollingResistance = 0.015f;
    public float maxSteerAngle = 30f;

    [Header("Napęd")]
    [Range(0.5f, 1f)] public float drivetrainEfficiency = 0.85f;
    [Header("Opory")]
    [Tooltip("Opór powietrza. Większy = niższa prędkość maksymalna.")]
    public float airDrag = 0.4f;
    [Header("Paliwo")]
    [Tooltip("1 = realistyczne spalanie (bardzo wolno), 10 = szybciej, wygodne do testów")]
    public float fuelGameplayMultiplier = 10f;
    [Header("Temperatura (°C)")]
    public float ambientTemperature = 20f;
    public float thermostatTemperature = 88f;
    public float overheatTemperature = 110f;
    public float boilTemperature = 120f;

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
    // Zapłon: widoczny w sieci i zachowany po zmianie kierowcy
    private NetworkVariable<bool> engineOn = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    // Ręczny: widoczny w sieci i zachowany po zmianie kierowcy. Auta startują z zaciągniętym.
    private NetworkVariable<bool> handbrakeOn = new NetworkVariable<bool>(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private float engineRpm = 0f;
    private float lastShiftTime;
    private float pendingFuel;
    private float fuelFlushTimer;
    private float pendingDamage;
    private float fuelQuality = 1f;
    private bool engineRunning;
    private float engineTemp = 20f;
    private EnginePart tempEngine;
    private float pendingCoolantLoss;
    private float oilTemp = 20f;
    private float pendingOilDegrade;
    private float pendingOilBurn;
    private bool isCranking;
    private float crankTimer;
    private bool ignitionLocked; // żeby trzymanie R po odpaleniu/zgaszeniu nie przełączało dalej
    private float crankStrength;       // 0-1, jak mocno kręci rozrusznik (albo koła przy pchaniu)
    private float rollEngineRpm;       // obroty, jakie koła wymuszają na silniku przez wrzucony bieg
    private float pendingBatteryDelta; // zmiana naładowania, która jeszcze nie poszła do hosta
    private bool gearHolding; // zgaszony silnik na biegu trzyma stojące auto
    public ulong DriverId => GetOccupant(0);
    public bool HasDriver => DriverId != NoOne;
    public bool IsDrivenByMe => IsOwner && DriverId == NetworkManager.LocalClientId;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        parts = GetComponent<CarParts>();
        rb.centerOfMass = new Vector3(0, -0.3f, 0);
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.sleepThreshold = 0f; // auto nigdy nie zasypia

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

    FuelContainer GetFuelTank()
    {
        CarPart part = parts.GetMountedPart(PartType.FuelTank);
        return part != null ? part.GetComponent<FuelContainer>() : null;
    }

    RadiatorPart GetRadiator()
    {
        CarPart part = parts.GetMountedPart(PartType.Radiator);
        return part != null ? part.GetComponent<RadiatorPart>() : null;
    }
    BatteryPart GetBattery()
    {
        CarPart part = parts.GetMountedPart(PartType.Battery);
        return part != null ? part.GetComponent<BatteryPart>() : null;
    }

    // Naładowanie wraz ze zmianami, których host jeszcze nie dostał
    float GetCharge(BatteryPart battery)
    {
        return Mathf.Clamp(battery.charge.Value + pendingBatteryDelta, 0f, 100f);
    }

    // Wywoływane przez pchającego gracza, wykonywane u właściciela auta (to on liczy fizykę)
    [Rpc(SendTo.Owner)]
    public void PushRpc(Vector3 impulse)
    {
        rb.AddForce(impulse, ForceMode.Impulse);
    }

    // ---------- Jazda ----------

    void FixedUpdate()
    {
        if (!IsSpawned || !IsOwner)
        {
            tempEngine = null;
            return;
        }

        float throttle = 0f;
        float footBrake = 0f;
        float steer = 0f;

        if (IsDrivenByMe)
        {
            Keyboard kb = Keyboard.current;
            if (kb.wKey.isPressed) throttle = 1f;
            if (kb.sKey.isPressed) footBrake = 1f;
            if (kb.dKey.isPressed) steer += 1f;
            if (kb.aKey.isPressed) steer -= 1f;
        }

        // Skręt
        float steerAngle = steer * maxSteerAngle;
        frontLeft.steerAngle = steerAngle;
        frontRight.steerAngle = steerAngle;

        if (Mathf.Abs(syncedSteer.Value - steerAngle) > 0.5f)
            syncedSteer.Value = steerAngle;

        // Napęd i systemy silnika
        float driveTorque = CalculateDriveTorque(throttle);
        UpdateFuel(throttle);
        UpdateTemperature(throttle);
        UpdateWear(throttle);
        UpdateElectrics();
        FlushEngineState();

        rearLeft.motorTorque = driveTorque * 0.5f;
        rearRight.motorTorque = driveTorque * 0.5f;

        // Moment na kołach nie budzi uśpionego Rigidbody, więc budzimy je sami
        if (Mathf.Abs(driveTorque) > 1f || Mathf.Abs(steerAngle) > 0.1f)
            rb.WakeUp();

        // Hamulce: zwykły na wszystkie koła, ręczny i "trzymanie na biegu" na tył
        float brake = footBrake * brakeTorque;
        frontLeft.brakeTorque = brake;
        frontRight.brakeTorque = brake;

        float rearBrake = brake;
        if (handbrakeOn.Value) rearBrake = Mathf.Max(rearBrake, handbrakeTorque);
        if (gearHolding) rearBrake = Mathf.Max(rearBrake, brakeTorque);
        rearLeft.brakeTorque = rearBrake;
        rearRight.brakeTorque = rearBrake;

        // Opór powietrza
        Vector3 v = rb.linearVelocity;

        // Opór toczenia: stała siła przeciwna do ruchu (masa × g × współczynnik)
        if (v.magnitude > 0.1f)
            rb.AddForce(-v.normalized * rollingResistance * rb.mass * 9.81f);

        // Opór powietrza rośnie z kwadratem prędkości
        rb.AddForce(-v * v.magnitude * airDrag);
    }

    float CalculateDriveTorque(float throttle)
    {
        EnginePart engine = parts.GetMounted<EnginePart>();
        GearboxPart box = parts.GetMounted<GearboxPart>();
        FuelContainer tank = GetFuelTank();

        if (engine == null)
        {
            if (engineOn.Value) engineOn.Value = false;
            engineRpm = 0f;
            rollEngineRpm = 0f;
            engineRunning = false;
            gearHolding = false;
            return 0f;
        }

        // Czy silnik może pracować
        fuelQuality = 0f;
        if (tank != null && tank.HasFuel)
            engine.EvaluateFuel(tank.contents.Value, out fuelQuality, out _);

        bool canRun = fuelQuality >= 0.2f && engine.health.Value > 0f;
        if (engineOn.Value && !canRun) engineOn.Value = false;
        engineRunning = engineOn.Value && canRun;

        // Bieg
        int g = box == null ? 0 : Mathf.Clamp(gear.Value, -1, box.gearRatios.Length);
        if (g != gear.Value) gear.Value = g;

        // Przełożenie i obroty, jakie koła wymuszają na silniku (na luzie zero)
        float wheelRpm = (rearLeft.rpm + rearRight.rpm) * 0.5f;
        float totalRatio = 0f;
        if (g != 0)
        {
            float ratio = g > 0 ? box.gearRatios[g - 1] : -box.reverseRatio;
            totalRatio = ratio * box.finalDrive;
        }
        rollEngineRpm = Mathf.Abs(wheelRpm * totalRatio);
        float rollDir = wheelRpm > 1f ? 1f : (wheelRpm < -1f ? -1f : 0f);
        // Zgaszony silnik na biegu trzyma auto w miejscu, ale tylko gdy prawie stoi
        // (dzięki temu odpalanie na pych z jadącego auta dalej działa)
        gearHolding = !engineRunning && g != 0 && rollEngineRpm < 100f;
        // Silnik zgaszony
        if (!engineRunning)
        {
            // Obroty od rozrusznika albo od toczących się kół
            float crankTarget = isCranking ? 250f * crankStrength : 0f;
            engineRpm = Mathf.Lerp(engineRpm, Mathf.Max(crankTarget, rollEngineRpm), Time.fixedDeltaTime * 4f);

            // Zgaszony silnik na biegu hamuje kompresją
            float compression = engine.engineBraking * 1.5f * Mathf.Clamp01(rollEngineRpm / engine.maxRpm);
            return -compression * Mathf.Abs(totalRatio) * rollDir;
        }

        float healthFactor = engine.health.Value / 100f;
        float powerFactor = fuelQuality * Mathf.Lerp(0.4f, 1f, healthFactor);
        bool misfire = fuelQuality < 0.85f && Random.value < (0.85f - fuelQuality) * 0.5f;

        // Luz
        if (g == 0)
        {
            float target = engine.idleRpm + throttle * powerFactor * (engine.maxRpm - engine.idleRpm);
            if (misfire) target *= 0.7f;
            engineRpm = Mathf.Lerp(engineRpm, target, Time.fixedDeltaTime * 3f);
            return 0f;
        }

        engineRpm = Mathf.Max(rollEngineRpm, engine.idleRpm);

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

        float torque = engineRpm < engine.maxRpm
            ? engine.GetTorque(engineRpm) * throttle * powerFactor
            : 0f;
        if (misfire) torque = 0f;

        float rpmFactor = Mathf.InverseLerp(engine.idleRpm, engine.maxRpm, engineRpm);
        float braking = engine.engineBraking * rpmFactor * (1f - throttle);
        if (engineRpm > engine.maxRpm)
            braking += engine.engineBraking * 3f;

        float driveAtWheels = torque * totalRatio * drivetrainEfficiency;
        float brakeAtWheels = braking * Mathf.Abs(totalRatio) * rollDir;

        return driveAtWheels - brakeAtWheels;
    }

    void UpdateFuel(float throttle)
    {
        EnginePart engine = parts.GetMounted<EnginePart>();
        FuelContainer tank = GetFuelTank();

        if (!engineRunning || engine == null || tank == null) return;

        float load = 0.15f + 0.85f * throttle;
        float rpmFactor = engineRpm / engine.maxRpm;
        float litersPerHour = engine.maxFuelConsumption * load * rpmFactor;
        pendingFuel += litersPerHour / 3600f * fuelGameplayMultiplier * Time.fixedDeltaTime;

        engine.EvaluateFuel(tank.contents.Value, out _, out float damage);
        pendingDamage += damage * Mathf.Max(rpmFactor, 0.2f) * Time.fixedDeltaTime;
    }

    void UpdateTemperature(float throttle)
    {
        EnginePart engine = parts.GetMounted<EnginePart>();
        if (engine == null)
        {
            tempEngine = null;
            return;
        }

        // Nowy silnik albo właśnie przejęliśmy auto: zacznij od temperatur z sieci
        if (engine != tempEngine)
        {
            tempEngine = engine;
            engineTemp = engine.temperature.Value;
            oilTemp = engine.oilTemperature.Value;
        }

        float dt = Time.fixedDeltaTime;

        float heat = 0f;
        if (engineRunning)
        {
            float rpmFactor = engineRpm / engine.maxRpm;
            heat = engine.heatOutput * (0.2f + 0.8f * throttle) * rpmFactor;
        }

        float cooling = (engineTemp - ambientTemperature) * 0.004f;

        RadiatorPart radiator = GetRadiator();
        if (radiator != null && engineTemp > thermostatTemperature)
        {
            float speed = rb.linearVelocity.magnitude;
            float airflow = 0.35f + Mathf.Clamp01(speed / 25f);
            cooling += radiator.coolingPower * radiator.CoolantFactor * airflow
                       * (engineTemp - ambientTemperature) * 0.035f;
        }

        engineTemp = Mathf.Max(ambientTemperature, engineTemp + (heat - cooling) * dt);

        // Olej powoli "goni" temperaturę silnika
        oilTemp = Mathf.Max(ambientTemperature, oilTemp + (engineTemp - oilTemp) * 0.02f * dt);

        if (engineTemp > overheatTemperature)
            pendingDamage += (engineTemp - overheatTemperature) * 0.1f * dt;

        if (radiator != null && engineTemp > boilTemperature)
            pendingCoolantLoss += 0.05f * dt;
    }
    void UpdateElectrics()
    {
        BatteryPart battery = GetBattery();
        EnginePart engine = parts.GetMounted<EnginePart>();
        if (battery == null || engine == null || !engineRunning) return;

        // Alternator ładuje: na jałowym trochę słabiej, od 1.5x jałowych pełną mocą
        float alternatorFactor = Mathf.Clamp01(engineRpm / (engine.idleRpm * 1.5f));
        pendingBatteryDelta += engine.alternatorOutput * alternatorFactor * Time.fixedDeltaTime;
    }

    void FlushEngineState()
    {
        fuelFlushTimer += Time.fixedDeltaTime;
        if (fuelFlushTimer < 1f) return;

        bool hasEngine = parts.GetMounted<EnginePart>() != null;
        SyncEngineStateServerRpc(
            pendingFuel,
            pendingDamage,
            hasEngine ? engineTemp : -1f,
            hasEngine ? oilTemp : -1f,
            pendingCoolantLoss,
            pendingOilDegrade,
            pendingOilBurn,
            pendingBatteryDelta);

        pendingFuel = 0f;
        pendingDamage = 0f;
        pendingCoolantLoss = 0f;
        pendingOilDegrade = 0f;
        pendingOilBurn = 0f;
        pendingBatteryDelta = 0f;
        fuelFlushTimer = 0f;
    }

    [ServerRpc]
    void SyncEngineStateServerRpc(float fuelUsed, float engineDamage, float temperature, float oilTemperature,
                                  float coolantLost, float oilDegraded, float oilBurned, float batteryDelta)
    {
        FuelContainer tank = GetFuelTank();
        if (tank != null) tank.ServerTake(fuelUsed);

        EnginePart engine = parts.GetMounted<EnginePart>();
        if (engine != null)
        {
            engine.ServerDamage(engineDamage);
            if (temperature >= 0f) engine.temperature.Value = temperature;
            if (oilTemperature >= 0f) engine.oilTemperature.Value = oilTemperature;

            if (engine.sump != null)
            {
                engine.sump.ServerConvertOil(oilDegraded);
                engine.sump.ServerTake(oilBurned);
            }
        }

        RadiatorPart radiator = GetRadiator();
        if (radiator != null && coolantLost > 0f) radiator.liquid.ServerTake(coolantLost);

        BatteryPart battery = GetBattery();
        if (battery != null && batteryDelta != 0f) battery.ServerAddCharge(batteryDelta);
    }

    // ---------- Wygląd kół ----------
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

    void HandleIgnitionInput()
    {
        Keyboard kb = Keyboard.current;

        if (kb.rKey.wasReleasedThisFrame) ignitionLocked = false;

        // Silnik pracuje: krótkie R gasi
        if (engineOn.Value)
        {
            isCranking = false;
            crankStrength = 0f;
            if (kb.rKey.wasPressedThisFrame)
            {
                engineOn.Value = false;
                ignitionLocked = true;
            }
            return;
        }

        isCranking = kb.rKey.isPressed && !ignitionLocked;
        if (!isCranking)
        {
            crankTimer = 0f;
            crankStrength = 0f;
            return;
        }

        BatteryPart battery = GetBattery();
        EnginePart engine = parts.GetMounted<EnginePart>();

        // Siła rozrusznika zależy od akumulatora: pełna od 50%, zero poniżej 10%
        float batteryStrength = battery != null ? Mathf.Clamp01((GetCharge(battery) - 10f) / 40f) : 0f;

        // Na pych: koła kręcą silnikiem przez wrzucony bieg
        float pushStrength = Mathf.Clamp01(rollEngineRpm / 600f);

        crankStrength = Mathf.Max(batteryStrength, pushStrength);

        // Rozrusznik pobiera prąd
        if (battery != null && engine != null && GetCharge(battery) > 0f)
            pendingBatteryDelta -= engine.starterDrain * Time.deltaTime;

        // Za słabo, żeby obrócić silnik: rozrusznik tylko klika
        if (crankStrength < 0.25f)
        {
            crankTimer = 0f;
            return;
        }

        // Słabszy rozrusznik = dłuższy rozruch
        crankTimer += Time.deltaTime * crankStrength;

        if (CanStart(out float requiredTime) && crankTimer >= requiredTime)
        {
            engineOn.Value = true;
            crankTimer = 0f;
            ignitionLocked = true;
            isCranking = false;
            crankStrength = 0f;
        }
    }

    bool CanStart(out float requiredCrankTime)
    {
        requiredCrankTime = float.MaxValue;

        EnginePart engine = parts.GetMounted<EnginePart>();
        FuelContainer tank = GetFuelTank();
        if (engine == null || engine.health.Value <= 0f) return false;
        if (tank == null || !tank.HasFuel) return false;

        // Benzyna potrzebuje prądu na iskrę, diesel nie
        BatteryPart battery = GetBattery();
        bool hasSpark = engine.fuelType == EngineFuelType.Diesel
                        || (battery != null && GetCharge(battery) > 2f);
        if (!hasSpark) return false;

        engine.EvaluateFuel(tank.contents.Value, out float quality, out _);
        if (quality < 0.2f) return false;

        float cold = Mathf.InverseLerp(70f, ambientTemperature, oilTemp);
        float dieselFactor = engine.fuelType == EngineFuelType.Diesel ? 1.6f : 1f;
        requiredCrankTime = (0.6f + cold * 1.4f * dieselFactor) / quality;
        return true;
    }

    string GetEngineStateText()
    {
        if (engineRunning) return "";
        if (isCranking) return crankStrength < 0.25f ? "  |  KLIK..." : "  |  ROZRUCH...";
        return "  |  ZGASZONY";
    }

    void Update()
    {
        if (IsSpawned && IsDrivenByMe)
        {
            HandleShiftInput();
            HandleIgnitionInput();
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
                handbrakeOn.Value = !handbrakeOn.Value;
        }
        else
        {
            isCranking = false;
        }

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
    void UpdateWear(float throttle)
    {
        EnginePart engine = parts.GetMounted<EnginePart>();
        if (engine == null || !engineRunning) return;

        float dt = Time.fixedDeltaTime;
        float rpmFactor = engineRpm / engine.maxRpm;
        float rpm2 = rpmFactor * rpmFactor; // zużycie rośnie szybko z obrotami
        float load = 0.15f + 0.85f * throttle;

        float cold = Mathf.InverseLerp(70f, ambientTemperature, oilTemp); // 0 = ciepły olej, 1 = zimny
        float lubrication = engine.GetLubrication();

        float wear = rpm2 * load * 0.02f            // normalne zużycie
                   + rpm2 * cold * 0.3f              // ostra jazda na zimnym oleju
                   + rpm2 * (1f - lubrication) * 3f; // mało albo zużyty olej

        if (lubrication < 0.05f) wear += 5f;                       // brak oleju = zatarcie
        if (rpmFactor > 1.05f) wear += (rpmFactor - 1.05f) * 20f;  // przekręcony silnik

        pendingDamage += wear * dt;

        // Starzenie i spalanie oleju
        if (engine.UsesSumpOil)
        {
            float heatFactor = engineTemp > overheatTemperature ? 3f : 1f;
            pendingOilDegrade += engine.oilDegradeRate * load * rpmFactor * heatFactor * dt;

            float wornFactor = 1f + (1f - engine.health.Value / 100f) * 5f;
            pendingOilBurn += engine.oilBurnRate * load * wornFactor * dt;
        }
    }
    // ---------- HUD ----------
    void OnGUI()
    {
        if (!IsDrivenByMe) return;

        EnginePart engine = parts.GetMounted<EnginePart>();
        GearboxPart box = parts.GetMounted<GearboxPart>();
        FuelContainer tank = GetFuelTank();
        RadiatorPart radiator = GetRadiator();

        float kmh = rb.linearVelocity.magnitude * 3.6f;
        int g = gear.Value;
        string gearText = g == -1 ? "R"
                        : g == 0 ? "N"
                        : (box != null && box.automatic ? "D" + g : g.ToString());

        GUI.Label(new Rect(20, 20, 400, 25), $"{kmh:0} km/h");
        if (handbrakeOn.Value)
        {
            GUI.color = Color.red;
            GUI.Label(new Rect(120, 20, 200, 25), "(P) RĘCZNY");
            GUI.color = Color.white;
        }
        // Silnik
        if (engine != null)
        {
            if (engineRpm > engine.maxRpm * 0.9f) GUI.color = Color.red;
            string state = GetEngineStateText();
            GUI.Label(new Rect(20, 45, 500, 25),
                $"{engine.engineName}  |  {engineRpm:0} RPM  |  stan {engine.health.Value:0}%{state}");
            GUI.color = Color.white;
        }
        else
        {
            GUI.Label(new Rect(20, 45, 400, 25), "BRAK SILNIKA");
        }

        // Skrzynia
        GUI.Label(new Rect(20, 70, 400, 25),
            box != null ? $"{box.gearboxName}  |  Bieg: {gearText}" : "BRAK SKRZYNI");

        // Paliwo
        if (tank != null)
        {
            if (tank.Liters < tank.capacity * 0.15f) GUI.color = Color.yellow;
            GUI.Label(new Rect(20, 95, 500, 25),
                $"Paliwo: {tank.Liters:0.0} / {tank.capacity:0} L  ({tank.contents.Value.Describe()})");
            GUI.color = Color.white;
        }
        else
        {
            GUI.Label(new Rect(20, 95, 400, 25), "BRAK ZBIORNIKA");
        }

        // Temperatura i chłodnica
        if (engine != null)
        {
            if (engineTemp > overheatTemperature) GUI.color = Color.red;
            else if (engineTemp > overheatTemperature - 8f) GUI.color = Color.yellow;
            else if (engineTemp < 60f) GUI.color = Color.cyan;

            string radiatorText = radiator != null
                ? $"{radiator.radiatorName}: {radiator.liquid.Liters:0.0} / {radiator.liquid.capacity:0} L"
                : "BRAK CHŁODNICY";
            GUI.Label(new Rect(20, 120, 500, 25), $"Temp: {engineTemp:0}°C  |  {radiatorText}");
            GUI.color = Color.white;
        }

        // Olej
        float lubrication = engine != null ? engine.GetLubrication() : 1f;
        float freshPercent = 100f;
        if (engine != null && engine.UsesSumpOil && engine.sump != null)
        {
            FuelMix m = engine.sump.contents.Value;
            float anyOil = m.oil + m.usedOil;
            freshPercent = anyOil > 0f ? m.oil / anyOil * 100f : 0f;

            if (lubrication < 0.5f) GUI.color = Color.red;
            else if (oilTemp < 60f) GUI.color = Color.cyan;

            GUI.Label(new Rect(20, 145, 500, 25),
                $"Olej: {anyOil:0.0} / {engine.sump.capacity:0.0} L  (świeży {freshPercent:0}%)  |  {oilTemp:0}°C");
            GUI.color = Color.white;
        }
        else if (engine != null)
        {
            GUI.Label(new Rect(20, 145, 500, 25), $"Olej: w paliwie (dwusuw)  |  {oilTemp:0}°C");
        }
        // Akumulator
        BatteryPart battery = GetBattery();
        if (battery != null)
        {
            float charge = GetCharge(battery);
            if (charge < 20f) GUI.color = Color.red;
            else if (charge < 40f) GUI.color = Color.yellow;

            string charging = engineRunning ? "  (ładowanie)" : "";
            GUI.Label(new Rect(20, 170, 500, 25), $"{battery.batteryName}: {charge:0}%{charging}");
            GUI.color = Color.white;
        }
        else
        {
            GUI.Label(new Rect(20, 170, 500, 25), "BRAK AKUMULATORA");
        }
        // Ostrzeżenia, od najważniejszego
        string warning = null;
        Color warningColor = Color.yellow;
        float rpmFactor = engine != null ? engineRpm / engine.maxRpm : 0f;
        float cold = Mathf.InverseLerp(70f, ambientTemperature, oilTemp);

        if (isCranking && crankStrength < 0.25f)
        {
            warning = "Akumulator za słaby! Rozrusznik tylko klika";
            warningColor = Color.red;
        }
        else if (engine != null && engineTemp > overheatTemperature)
        {
            warning = "PRZEGRZANIE! Silnik się niszczy!";
            warningColor = Color.red;
        }
        else if (engine != null && engine.UsesSumpOil && lubrication < 0.3f)
        {
            warning = "NISKI POZIOM OLEJU! Silnik się zaciera!";
            warningColor = Color.red;
        }
        else if (engine != null && engineRunning && rpmFactor > 1.05f)
        {
            warning = "Przekręcasz silnik!";
            warningColor = Color.red;
        }
        else if (engine != null && engineRunning && cold > 0.3f && rpmFactor > 0.6f)
        {
            warning = "Zimny olej, nie kręć wysoko!";
        }
        else if (engine != null && tank != null && tank.HasFuel)
        {
            engine.EvaluateFuel(tank.contents.Value, out float quality, out float damage);

            if (!engineRunning && quality < 0.2f)
                warning = "Silnik nie chce pracować na tym paliwie";
            else if (engineRunning && damage > 0.05f)
            {
                warning = "Silnik się niszczy! Złe paliwo!";
                warningColor = Color.red;
            }
            else if (engineRunning && quality < 0.85f)
                warning = "Silnik przerywa, złe paliwo";
        }

        if (warning == null && engine != null && engine.UsesSumpOil && freshPercent < 40f)
            warning = "Olej do wymiany";

        if (warning != null)
        {
            GUI.color = warningColor;
            GUI.Label(new Rect(20, 195, 500, 25), warning);
            GUI.color = Color.white;
        }

        GUI.Label(new Rect(20, 220, 600, 25),
            "R (przytrzymaj) odpal/zgaś, W gaz, S hamulec, Spacja ręczny (przełącz), Shift/Ctrl bieg");

        // DIAGNOSTYKA - usuniemy, gdy znajdziemy problem
        GUI.color = Color.magenta;
        GUI.Label(new Rect(20, 250, 900, 25),
            $"motor: {rearLeft.motorTorque:0} Nm | hamulec tył: {rearLeft.brakeTorque:0} Nm | " +
            $"rpm koła: {rearLeft.rpm:0} | na ziemi: {rearLeft.isGrounded} | " +
            $"śpi: {rb.IsSleeping()} | kinematyczne: {rb.isKinematic}");
        GUI.color = Color.white;
        //////
    }
}