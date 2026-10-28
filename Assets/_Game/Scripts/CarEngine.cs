using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

// Napęd: silnik, skrzynia, rozruch, paliwo, temperatura, olej, zużycie.
// Liczony tylko u właściciela auta, wywoływany przez CarController.
public class CarEngine : NetworkBehaviour
{
    // Bieg: -1 = R, 0 = N, 1..n. Widoczny w sieci i zachowany po zmianie kierowcy.
    private NetworkVariable<int> gear = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Zapłon. Widoczny w sieci i zachowany po zmianie kierowcy.
    private NetworkVariable<bool> engineOn = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private CarController car;
    private CarParts parts;
    private CarElectrics electrics;

    // Stan liczony u właściciela auta
    private float engineRpm;
    private float rollEngineRpm;   // obroty wymuszane przez koła przez wrzucony bieg
    private float fuelQuality = 1f;
    private bool engineRunning;
    private bool gearHolding;      // zgaszony silnik na biegu trzyma stojące auto
    private float lastShiftTime;

    private float engineTemp = 20f;
    private float oilTemp = 20f;
    private EnginePart tempEngine; // silnik, dla którego mamy aktualne temperatury

    private bool isCranking;
    private bool ignitionLocked;   // żeby trzymanie R po odpaleniu/zgaszeniu nie przełączało dalej
    private float crankTimer;
    private float crankStrength;

    // Zmiany, które raz na sekundę idą do hosta
    private float pendingFuel;
    private float pendingDamage;
    private float pendingCoolantLoss;
    private float pendingOilDegrade;
    private float pendingOilBurn;
    private float flushTimer;

    GameTuning Tuning => GameTuning.Instance;

    // ---------- Do odczytu (HUD, CarController) ----------

    public float Rpm => engineRpm;
    public bool IsRunning => engineRunning;
    public bool IsCranking => isCranking;
    public float CrankStrength => crankStrength;
    public bool GearHolding => gearHolding;
    public float EngineTemp => engineTemp;
    public float OilTemp => oilTemp;
    public int Gear => gear.Value;

    // 0 = rozgrzany olej, 1 = zimny
    public float ColdFactor => Mathf.InverseLerp(Tuning.warmOilTemperature, Tuning.ambientTemperature, oilTemp);

    void Awake()
    {
        car = GetComponent<CarController>();
        parts = GetComponent<CarParts>();
        electrics = GetComponent<CarElectrics>();
    }

    // ---------- Części ----------

    public EnginePart GetEngine() => parts.GetMounted<EnginePart>();
    public GearboxPart GetGearbox() => parts.GetMounted<GearboxPart>();

    public FuelContainer GetFuelTank()
    {
        CarPart part = parts.GetMountedPart(PartType.FuelTank);
        return part != null ? part.GetComponent<FuelContainer>() : null;
    }

    public RadiatorPart GetRadiator()
    {
        CarPart part = parts.GetMountedPart(PartType.Radiator);
        return part != null ? part.GetComponent<RadiatorPart>() : null;
    }

    public string GetGearText()
    {
        int g = gear.Value;
        GearboxPart box = GetGearbox();
        if (g == -1) return "R";
        if (g == 0) return "N";
        return box != null && box.automatic ? "D" + g : g.ToString();
    }

    public string GetStateText()
    {
        if (engineRunning) return "";
        if (isCranking) return crankStrength < 0.25f ? "  |  KLIK..." : "  |  ROZRUCH...";
        return "  |  ZGASZONY";
    }

    // ---------- Wywoływane przez CarController ----------

    public void OnNotSimulating()
    {
        tempEngine = null; // gdy znowu przejmiemy auto, weźmiemy temperatury od hosta
    }

    public void StopCranking()
    {
        isCranking = false;
        crankStrength = 0f;
    }

    public void HandleInput()
    {
        HandleShiftInput();
        HandleIgnitionInput();
    }

    // Zwraca moment na tylnej osi (Nm)
    public float Simulate(float throttle)
    {
        float torque = CalculateDriveTorque(throttle);
        UpdateFuel(throttle);
        UpdateTemperature(throttle);
        UpdateWear(throttle);
        UpdateAlternator();
        FlushEngineState();
        electrics.Tick(Time.fixedDeltaTime);
        return torque;
    }

    // ---------- Napęd ----------

    float CalculateDriveTorque(float throttle)
    {
        EnginePart engine = GetEngine();
        GearboxPart box = GetGearbox();
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
        if (engineOn.Value && !canRun) engineOn.Value = false; // zgasł sam
        engineRunning = engineOn.Value && canRun;

        // Bieg
        int g = box == null ? 0 : Mathf.Clamp(gear.Value, -1, box.gearRatios.Length);
        if (g != gear.Value) gear.Value = g;

        // Przełożenie i obroty, jakie koła wymuszają na silniku (na luzie zero)
        float wheelRpm = (car.rearLeft.rpm + car.rearRight.rpm) * 0.5f;
        float totalRatio = 0f;
        if (g != 0)
        {
            float ratio = g > 0 ? box.gearRatios[g - 1] : -box.reverseRatio;
            totalRatio = ratio * box.finalDrive;
        }
        rollEngineRpm = Mathf.Abs(wheelRpm * totalRatio);
        float rollDir = wheelRpm > 1f ? 1f : (wheelRpm < -1f ? -1f : 0f);

        gearHolding = !engineRunning && g != 0 && rollEngineRpm < Tuning.gearHoldingRpm;

        // Silnik zgaszony
        if (!engineRunning)
        {
            float crankTarget = isCranking ? Tuning.crankRpm * crankStrength : 0f;
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

        // Automat
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

        // Hamowanie silnikiem
        float rpmFactor = Mathf.InverseLerp(engine.idleRpm, engine.maxRpm, engineRpm);
        float braking = engine.engineBraking * rpmFactor * (1f - throttle);
        if (engineRpm > engine.maxRpm)
            braking += engine.engineBraking * 3f;

        float driveAtWheels = torque * totalRatio * car.drivetrainEfficiency;
        float brakeAtWheels = braking * Mathf.Abs(totalRatio) * rollDir;

        return driveAtWheels - brakeAtWheels;
    }

    // ---------- Paliwo, temperatura, zużycie ----------

    void UpdateFuel(float throttle)
    {
        EnginePart engine = GetEngine();
        FuelContainer tank = GetFuelTank();
        if (!engineRunning || engine == null || tank == null) return;

        float load = 0.15f + 0.85f * throttle;
        float rpmFactor = engineRpm / engine.maxRpm;
        float litersPerHour = engine.maxFuelConsumption * load * rpmFactor;
        pendingFuel += litersPerHour / 3600f * Tuning.fuelGameplayMultiplier * Time.fixedDeltaTime;

        engine.EvaluateFuel(tank.contents.Value, out _, out float damage);
        pendingDamage += damage * Mathf.Max(rpmFactor, 0.2f) * Time.fixedDeltaTime;
    }

    void UpdateTemperature(float throttle)
    {
        EnginePart engine = GetEngine();
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
        float ambient = Tuning.ambientTemperature;

        float heat = 0f;
        if (engineRunning)
        {
            // Część ciepła powstaje zawsze (spalanie na jałowym), reszta rośnie z obrotami
            float rpmFactor = engineRpm / engine.maxRpm;
            float rpmHeat = Tuning.idleHeat + (1f - Tuning.idleHeat) * rpmFactor;
            heat = engine.heatOutput * (0.25f + 0.75f * throttle) * rpmHeat;
        }

        float cooling = (engineTemp - ambient) * Tuning.naturalCooling;

        RadiatorPart radiator = GetRadiator();
        if (radiator != null && engineTemp > Tuning.thermostatTemperature)
        {
            float speed = car.Body.linearVelocity.magnitude;
            float airflow = Tuning.fanAirflow + Mathf.Clamp01(speed / Tuning.fullAirflowSpeed);
            cooling += radiator.coolingPower * radiator.CoolantFactor * airflow
                       * (engineTemp - ambient) * Tuning.radiatorEfficiency;
        }

        // Płyn chłodniczy magazynuje ciepło. Bez niego silnik grzeje się (i stygnie) szybciej.
        float coolant = radiator != null ? radiator.CoolantFactor : 0f;
        float thermalMass = Mathf.Lerp(Tuning.thermalMassWithoutCoolant, 1f, coolant);
        engineTemp = Mathf.Max(ambient, engineTemp + (heat - cooling) / thermalMass * dt);
        oilTemp = Mathf.Max(ambient, oilTemp + (engineTemp - oilTemp) * Tuning.oilWarmUpRate * dt);

        if (engineTemp > Tuning.overheatTemperature)
            pendingDamage += (engineTemp - Tuning.overheatTemperature) * Tuning.overheatDamage * dt;

        if (radiator != null && engineTemp > Tuning.boilTemperature)
            pendingCoolantLoss += Tuning.coolantBoilRate * dt;
    }

    void UpdateWear(float throttle)
    {
        EnginePart engine = GetEngine();
        if (engine == null || !engineRunning) return;

        float dt = Time.fixedDeltaTime;
        float rpmFactor = engineRpm / engine.maxRpm;
        float rpm2 = rpmFactor * rpmFactor;
        float load = 0.15f + 0.85f * throttle;
        float lubrication = engine.GetLubrication();

        float wear = rpm2 * load * Tuning.normalWear
                   + rpm2 * ColdFactor * Tuning.coldWear
                   + rpm2 * (1f - lubrication) * Tuning.poorLubricationWear;

        if (lubrication < 0.05f) wear += Tuning.noOilWear;
        if (rpmFactor > 1.05f) wear += (rpmFactor - 1.05f) * Tuning.overrevWear;

        pendingDamage += wear * dt;

        if (engine.UsesSumpOil)
        {
            float heatFactor = engineTemp > Tuning.overheatTemperature ? 3f : 1f;
            pendingOilDegrade += engine.oilDegradeRate * load * rpmFactor * heatFactor * dt;

            float wornFactor = 1f + (1f - engine.health.Value / 100f) * 5f;
            pendingOilBurn += engine.oilBurnRate * load * wornFactor * dt;
        }
    }

    void UpdateAlternator()
    {
        EnginePart engine = GetEngine();
        if (engine == null || !engineRunning) return;

        float alternatorFactor = Mathf.Clamp01(engineRpm / (engine.idleRpm * 1.5f));
        electrics.Charge(engine.alternatorOutput * alternatorFactor * Time.fixedDeltaTime);
    }

    void FlushEngineState()
    {
        flushTimer += Time.fixedDeltaTime;
        if (flushTimer < 1f) return;

        bool hasEngine = GetEngine() != null;
        SyncEngineStateServerRpc(
            pendingFuel,
            pendingDamage,
            hasEngine ? engineTemp : -1f,
            hasEngine ? oilTemp : -1f,
            pendingCoolantLoss,
            pendingOilDegrade,
            pendingOilBurn);

        pendingFuel = 0f;
        pendingDamage = 0f;
        pendingCoolantLoss = 0f;
        pendingOilDegrade = 0f;
        pendingOilBurn = 0f;
        flushTimer = 0f;
    }

    [ServerRpc]
    void SyncEngineStateServerRpc(float fuelUsed, float engineDamage, float temperature, float oilTemperature,
                                  float coolantLost, float oilDegraded, float oilBurned)
    {
        FuelContainer tank = GetFuelTank();
        if (tank != null) tank.ServerTake(fuelUsed);

        EnginePart engine = GetEngine();
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
    }

    // ---------- Sterowanie: biegi i zapłon ----------

    void HandleShiftInput()
    {
        GearboxPart box = GetGearbox();
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
            StopCranking();
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

        EnginePart engine = GetEngine();

        // Rozrusznik z akumulatora albo koła przy pchaniu, co jest mocniejsze
        float batteryStrength = electrics.GetStarterStrength();
        float pushStrength = Mathf.Clamp01(rollEngineRpm / Tuning.pushStartRpm);
        crankStrength = Mathf.Max(batteryStrength, pushStrength);

        if (engine != null) electrics.Drain(engine.starterDrain * Time.deltaTime);

        // Za słabo, żeby obrócić silnik: tylko klika
        if (crankStrength < 0.25f)
        {
            crankTimer = 0f;
            return;
        }

        crankTimer += Time.deltaTime * crankStrength;

        if (CanStart(out float requiredTime) && crankTimer >= requiredTime)
        {
            engineOn.Value = true;
            crankTimer = 0f;
            ignitionLocked = true;
            StopCranking();
        }
    }

    bool CanStart(out float requiredCrankTime)
    {
        requiredCrankTime = float.MaxValue;

        EnginePart engine = GetEngine();
        FuelContainer tank = GetFuelTank();
        if (engine == null || engine.health.Value <= 0f) return false;
        if (tank == null || !tank.HasFuel) return false;

        // Benzyna potrzebuje prądu na iskrę, diesel nie
        bool hasSpark = engine.fuelType == EngineFuelType.Diesel || electrics.HasSpark;
        if (!hasSpark) return false;

        engine.EvaluateFuel(tank.contents.Value, out float quality, out _);
        if (quality < 0.2f) return false;

        float dieselFactor = engine.fuelType == EngineFuelType.Diesel ? Tuning.dieselColdFactor : 1f;
        requiredCrankTime = (Tuning.baseCrankTime + ColdFactor * Tuning.coldCrankTime * dieselFactor) / quality;
        return true;
    }
}