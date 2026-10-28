using UnityEngine;

// Dźwięk silnika: miksowanie pętli według obrotów, rozrusznik, odpalanie, gaszenie, przerywanie.
// Działa u wszystkich graczy na podstawie stanu z CarEngine.
public class CarEngineAudio : MonoBehaviour
{
    [Tooltip("Skąd dochodzi dźwięk (np. EngineSlot). Puste = środek auta.")]
    public Transform soundOrigin;
    public float minDistance = 3f;
    public float maxDistance = 80f;
    [Tooltip("Jak szybko głośność pętli nadąża za zmianami")]
    public float fadeSpeed = 6f;

    private CarEngine engine;
    private EngineSoundSet currentSet;
    private AudioSource[] loopSources;
    private AudioSource starterSource;
    private AudioSource oneShotSource;

    private float smoothRpm;
    private float engineVolume;   // 0 = zgaszony, 1 = pracuje (płynnie)
    private bool lastEngineOn;
    private bool initialized;
    private float nextClickTime;

    Transform Origin => soundOrigin != null ? soundOrigin : transform;

    void Awake()
    {
        engine = GetComponent<CarEngine>();
        starterSource = CreateSource("Starter", true);
        oneShotSource = CreateSource("OneShots", false);
    }

    void OnEnable() { engine.OnMisfire += PlayMisfire; }
    void OnDisable() { engine.OnMisfire -= PlayMisfire; }

    AudioSource CreateSource(string name, bool loop)
    {
        GameObject go = new GameObject("Audio_" + name);
        go.transform.SetParent(Origin, false);

        AudioSource source = go.AddComponent<AudioSource>();
        source.loop = loop;
        source.playOnAwake = false;
        source.spatialBlend = 1f;      // dźwięk 3D
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.dopplerLevel = 0f;      // efekt Dopplera dziwnie brzmi w multiplayerze
        return source;
    }

    void Update()
    {
        // Zestaw dźwięków zależy od zamontowanego silnika
        EnginePart part = engine.GetEngine();
        EngineSoundSet set = part != null ? part.sounds : null;
        if (set != currentSet) BuildLoops(set);

        if (currentSet == null)
        {
            if (starterSource.isPlaying) starterSource.Stop();
            return;
        }

        UpdateStartStop();
        UpdateLoops();
        UpdateStarter();
    }

    void BuildLoops(EngineSoundSet set)
    {
        if (loopSources != null)
            foreach (AudioSource s in loopSources)
                if (s != null) Destroy(s.gameObject);

        loopSources = null;
        currentSet = set;
        if (set == null || set.loops == null) return;

        loopSources = new AudioSource[set.loops.Length];
        for (int i = 0; i < set.loops.Length; i++)
        {
            AudioSource s = CreateSource("Loop_" + set.loops[i].rpm, true);
            s.clip = set.loops[i].clip;
            s.volume = 0f;
            if (s.clip != null) s.Play();
            loopSources[i] = s;
        }
    }

    void UpdateStartStop()
    {
        bool on = engine.IsEngineOn;

        // Przy pierwszej klatce (np. po dołączeniu do gry) bez dźwięku odpalania
        if (!initialized)
        {
            lastEngineOn = on;
            engineVolume = on ? 1f : 0f;
            initialized = true;
            return;
        }

        if (on != lastEngineOn)
        {
            AudioClip clip = on ? currentSet.engineStart : currentSet.engineStop;
            if (clip != null) oneShotSource.PlayOneShot(clip, currentSet.volume);
            lastEngineOn = on;
        }
    }

    void UpdateLoops()
    {
        if (loopSources == null || loopSources.Length == 0) return;

        smoothRpm = Mathf.Lerp(smoothRpm, engine.AudioRpm, Time.deltaTime * 10f);

        // Po zgaszeniu pętle cichną w ok. pół sekundy, a ich wysokość opada z obrotami
        float target = engine.IsEngineOn ? 1f : 0f;
        engineVolume = Mathf.MoveTowards(engineVolume, target, Time.deltaTime * 2f);

        // Pod obciążeniem silnik jest głośniejszy
        float load = Mathf.Lerp(0.65f, 1f, engine.AudioThrottle);

        // Dwie pętle najbliższe aktualnym obrotom
        EngineSoundSet.Loop[] loops = currentSet.loops;
        int lower = 0;
        for (int i = 0; i < loops.Length; i++)
            if (smoothRpm >= loops[i].rpm) lower = i;
        int upper = Mathf.Min(lower + 1, loops.Length - 1);

        float t = upper == lower
            ? 0f
            : Mathf.Clamp01(Mathf.InverseLerp(loops[lower].rpm, loops[upper].rpm, smoothRpm));

        for (int i = 0; i < loopSources.Length; i++)
        {
            // Przejście "equal power": bez spadku głośności w połowie drogi
            float weight = 0f;
            if (i == lower) weight = Mathf.Cos(t * Mathf.PI * 0.5f);
            else if (i == upper) weight = Mathf.Sin(t * Mathf.PI * 0.5f);

            AudioSource s = loopSources[i];
            float targetVolume = weight * engineVolume * load * currentSet.volume;
            s.volume = Mathf.MoveTowards(s.volume, targetVolume, Time.deltaTime * fadeSpeed);
            s.pitch = Mathf.Clamp(Mathf.Max(smoothRpm, 1f) / loops[i].rpm, 0.3f, 2.5f);
        }
    }

    void UpdateStarter()
    {
        float crank = engine.AudioCrank;
        bool spinning = crank >= 0.25f;

        // Rozrusznik kręci: pętla, słabszy akumulator = niższy dźwięk
        if (spinning && currentSet.starterLoop != null)
        {
            if (starterSource.clip != currentSet.starterLoop) starterSource.clip = currentSet.starterLoop;
            if (!starterSource.isPlaying) starterSource.Play();
            starterSource.pitch = Mathf.Lerp(0.7f, 1f, crank);
            starterSource.volume = currentSet.volume;
        }
        else if (starterSource.isPlaying)
        {
            starterSource.Stop();
        }

        // Za słaby akumulator: kliknięcia
        if (crank > 0f && !spinning && currentSet.starterClick != null && Time.time >= nextClickTime)
        {
            oneShotSource.PlayOneShot(currentSet.starterClick, currentSet.volume);
            nextClickTime = Time.time + 0.35f;
        }
    }

    void PlayMisfire()
    {
        if (currentSet == null || currentSet.misfires == null || currentSet.misfires.Length == 0) return;

        AudioClip clip = currentSet.misfires[Random.Range(0, currentSet.misfires.Length)];
        if (clip != null) oneShotSource.PlayOneShot(clip, currentSet.volume);
    }
}