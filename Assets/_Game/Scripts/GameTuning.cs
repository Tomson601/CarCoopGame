using UnityEngine;

// Cały balans gry w jednym miejscu. Plik leży w Assets/_Game/Resources/GameTuning.asset.
// Zmiany w trybie Play zostają po jego wyłączeniu.
[CreateAssetMenu(fileName = "GameTuning", menuName = "Gra/Game Tuning")]
public class GameTuning : ScriptableObject
{
    private static GameTuning instance;

    public static GameTuning Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Resources.Load<GameTuning>("GameTuning");
                if (instance == null)
                {
                    Debug.LogWarning("Brak pliku Resources/GameTuning - używam wartości domyślnych");
                    instance = CreateInstance<GameTuning>();
                }
            }
            return instance;
        }
    }

    [Header("Paliwo")]
    [Tooltip("1 = realistyczne spalanie (bardzo wolno), 10 = szybciej")]
    public float fuelGameplayMultiplier = 10f;

    [Header("Temperatura (°C)")]
    public float ambientTemperature = 20f;
    public float thermostatTemperature = 88f;
    public float overheatTemperature = 110f;
    public float boilTemperature = 120f;

    [Header("Chłodzenie")]
    [Tooltip("Naturalne stygnięcie silnika")]
    public float naturalCooling = 0.004f;
    [Tooltip("Skuteczność chłodnicy")]
    public float radiatorEfficiency = 0.035f;
    [Tooltip("Przepływ powietrza na postoju (wentylator)")]
    public float fanAirflow = 0.35f;
    [Tooltip("Prędkość (m/s), przy której pęd powietrza chłodzi z pełną siłą")]
    public float fullAirflowSpeed = 25f;
    [Tooltip("% stanu silnika na sekundę na każdy stopień powyżej przegrzania")]
    public float overheatDamage = 0.1f;
    [Tooltip("Litry płynu na sekundę wygotowywane powyżej temperatury wrzenia")]
    public float coolantBoilRate = 0.05f;
    [Tooltip("Ile ciepła silnik produkuje na jałowym w porównaniu z max obrotami (0-1)")]
    public float idleHeat = 0.3f;
    [Tooltip("Bezwładność cieplna bez płynu chłodniczego. Mniej = szybciej się grzeje i stygnie.")]
    public float thermalMassWithoutCoolant = 0.5f;

    [Header("Olej")]
    [Tooltip("Jak szybko olej dogania temperaturę silnika")]
    public float oilWarmUpRate = 0.02f;
    [Tooltip("Powyżej tej temperatury olej jest rozgrzany")]
    public float warmOilTemperature = 70f;

    [Header("Zużycie silnika (% stanu na sekundę przy max obrotach)")]
    public float normalWear = 0.02f;
    public float coldWear = 0.3f;
    public float poorLubricationWear = 3f;
    public float noOilWear = 5f;
    public float overrevWear = 20f;

    [Header("Rozruch")]
    public float baseCrankTime = 0.6f;
    public float coldCrankTime = 1.4f;
    public float dieselColdFactor = 1.6f;
    [Tooltip("Obroty silnika kręconego rozrusznikiem")]
    public float crankRpm = 250f;
    [Tooltip("Obroty z kół, przy których rozruch na pych działa z pełną siłą")]
    public float pushStartRpm = 600f;
    [Tooltip("Poniżej tych obrotów zgaszony silnik na biegu trzyma auto w miejscu")]
    public float gearHoldingRpm = 100f;

    [Header("Akumulator (%)")]
    [Tooltip("Poniżej tego poziomu rozrusznik tylko klika")]
    public float batteryEmptyLevel = 10f;
    [Tooltip("Od tego poziomu rozrusznik kręci z pełną siłą")]
    public float batteryFullCrankLevel = 50f;
    [Tooltip("Minimum prądu na iskrę w silniku benzynowym")]
    public float minSparkCharge = 2f;

    [Header("Opony")]
    public float tireDistanceWear = 0.001f;
    public float tireSlipWear = 2f;
    public float bareRimGrip = 0.3f;
    public float flatTireGrip = 0.35f;
}