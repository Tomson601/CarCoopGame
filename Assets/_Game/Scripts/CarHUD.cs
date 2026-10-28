using UnityEngine;

// Prowizoryczny HUD kierowcy. Niczego nie liczy, tylko czyta stan z innych komponentów auta.
public class CarHUD : MonoBehaviour
{
    private CarController car;
    private CarSeats seats;
    private CarEngine engine;
    private CarElectrics electrics;
    private CarTires tires;

    void Awake()
    {
        car = GetComponent<CarController>();
        seats = GetComponent<CarSeats>();
        engine = GetComponent<CarEngine>();
        electrics = GetComponent<CarElectrics>();
        tires = GetComponent<CarTires>();
    }

    void OnGUI()
    {
        if (!seats.IsDrivenByMe) return;

        GameTuning tuning = GameTuning.Instance;
        EnginePart eng = engine.GetEngine();
        GearboxPart box = engine.GetGearbox();
        FuelContainer tank = engine.GetFuelTank();
        RadiatorPart radiator = engine.GetRadiator();

        // Prędkość i ręczny
        GUI.Label(new Rect(20, 20, 400, 25), $"{car.SpeedKmh:0} km/h");
        if (car.HandbrakeOn)
        {
            GUI.color = Color.red;
            GUI.Label(new Rect(120, 20, 200, 25), "(P) RĘCZNY");
            GUI.color = Color.white;
        }

        // Silnik
        if (eng != null)
        {
            if (engine.Rpm > eng.maxRpm * 0.9f) GUI.color = Color.red;
            GUI.Label(new Rect(20, 45, 500, 25),
                $"{eng.engineName}  |  {engine.Rpm:0} RPM  |  stan {eng.health.Value:0}%{engine.GetStateText()}");
            GUI.color = Color.white;
        }
        else
        {
            GUI.Label(new Rect(20, 45, 400, 25), "BRAK SILNIKA");
        }

        // Skrzynia
        GUI.Label(new Rect(20, 70, 400, 25),
            box != null ? $"{box.gearboxName}  |  Bieg: {engine.GetGearText()}" : "BRAK SKRZYNI");

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
        if (eng != null)
        {
            if (engine.EngineTemp > tuning.overheatTemperature) GUI.color = Color.red;
            else if (engine.EngineTemp > tuning.overheatTemperature - 8f) GUI.color = Color.yellow;
            else if (engine.EngineTemp < 60f) GUI.color = Color.cyan;

            string radiatorText = radiator != null
                ? $"{radiator.radiatorName}: {radiator.liquid.Liters:0.0} / {radiator.liquid.capacity:0} L"
                : "BRAK CHŁODNICY";
            GUI.Label(new Rect(20, 120, 500, 25), $"Temp: {engine.EngineTemp:0}°C  |  {radiatorText}");
            GUI.color = Color.white;
        }

        // Olej
        float lubrication = eng != null ? eng.GetLubrication() : 1f;
        float freshPercent = 100f;
        if (eng != null && eng.UsesSumpOil && eng.sump != null)
        {
            FuelMix m = eng.sump.contents.Value;
            float anyOil = m.oil + m.usedOil;
            freshPercent = anyOil > 0f ? m.oil / anyOil * 100f : 0f;

            if (lubrication < 0.5f) GUI.color = Color.red;
            else if (engine.OilTemp < 60f) GUI.color = Color.cyan;

            GUI.Label(new Rect(20, 145, 500, 25),
                $"Olej: {anyOil:0.0} / {eng.sump.capacity:0.0} L  (świeży {freshPercent:0}%)  |  {engine.OilTemp:0}°C");
            GUI.color = Color.white;
        }
        else if (eng != null)
        {
            GUI.Label(new Rect(20, 145, 500, 25), $"Olej: w paliwie (dwusuw)  |  {engine.OilTemp:0}°C");
        }

        // Akumulator
        BatteryPart battery = electrics.GetBattery();
        if (battery != null)
        {
            float charge = electrics.GetCharge();
            if (charge < 20f) GUI.color = Color.red;
            else if (charge < 40f) GUI.color = Color.yellow;

            string charging = engine.IsRunning ? "  (ładowanie)" : "";
            GUI.Label(new Rect(20, 170, 500, 25), $"{battery.batteryName}: {charge:0}%{charging}");
            GUI.color = Color.white;
        }
        else
        {
            GUI.Label(new Rect(20, 170, 500, 25), "BRAK AKUMULATORA");
        }

        // Ostrzeżenia
        DrawWarning(eng, tank, lubrication, freshPercent, tuning);

        // Opony
        GUI.Label(new Rect(20, 220, 700, 25),
            $"Opony: {tires.GetTireText(0, "LP")} | {tires.GetTireText(1, "PP")} | " +
            $"{tires.GetTireText(2, "LT")} | {tires.GetTireText(3, "PT")}  |  " +
            $"nawierzchnia: {CarTires.GetSurfaceName(tires.RearSurface)}");

        GUI.Label(new Rect(20, 245, 700, 25),
            "R (przytrzymaj) odpal/zgaś, W gaz, S hamulec, Spacja ręczny (przełącz), Shift/Ctrl bieg");
    }

    void DrawWarning(EnginePart eng, FuelContainer tank, float lubrication, float freshPercent, GameTuning tuning)
    {
        string warning = null;
        Color color = Color.yellow;
        float rpmFactor = eng != null ? engine.Rpm / eng.maxRpm : 0f;

        if (engine.IsCranking && engine.CrankStrength < 0.25f)
        {
            warning = "Akumulator za słaby! Rozrusznik tylko klika";
            color = Color.red;
        }
        else if (eng != null && engine.EngineTemp > tuning.overheatTemperature)
        {
            warning = "PRZEGRZANIE! Silnik się niszczy!";
            color = Color.red;
        }
        else if (eng != null && eng.UsesSumpOil && lubrication < 0.3f)
        {
            warning = "NISKI POZIOM OLEJU! Silnik się zaciera!";
            color = Color.red;
        }
        else if (eng != null && engine.IsRunning && rpmFactor > 1.05f)
        {
            warning = "Przekręcasz silnik!";
            color = Color.red;
        }
        else if (eng != null && engine.IsRunning && engine.ColdFactor > 0.3f && rpmFactor > 0.6f)
        {
            warning = "Zimny olej, nie kręć wysoko!";
        }
        else if (eng != null && tank != null && tank.HasFuel)
        {
            eng.EvaluateFuel(tank.contents.Value, out float quality, out float damage);

            if (!engine.IsRunning && quality < 0.2f)
                warning = "Silnik nie chce pracować na tym paliwie";
            else if (engine.IsRunning && damage > 0.05f)
            {
                warning = "Silnik się niszczy! Złe paliwo!";
                color = Color.red;
            }
            else if (engine.IsRunning && quality < 0.85f)
                warning = "Silnik przerywa, złe paliwo";
        }

        if (warning == null && eng != null && eng.UsesSumpOil && freshPercent < 40f)
            warning = "Olej do wymiany";

        if (warning == null) return;

        GUI.color = color;
        GUI.Label(new Rect(20, 195, 500, 25), warning);
        GUI.color = Color.white;
    }
}