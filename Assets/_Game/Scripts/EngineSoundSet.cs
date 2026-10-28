using UnityEngine;

// Zestaw dźwięków jednego silnika. Tworzysz go w Project: Create -> Gra -> Engine Sound Set.
[CreateAssetMenu(fileName = "EngineSounds", menuName = "Gra/Engine Sound Set")]
public class EngineSoundSet : ScriptableObject
{
    [System.Serializable]
    public class Loop
    {
        public AudioClip clip;
        [Tooltip("Obroty, przy których pętla została nagrana")]
        public float rpm;
    }

    [Tooltip("Pętle silnika OD NAJNIŻSZYCH obrotów do najwyższych")]
    public Loop[] loops;

    [Header("Pojedyncze dźwięki (mogą być puste)")]
    public AudioClip starterLoop;
    public AudioClip starterClick;
    public AudioClip engineStart;
    public AudioClip engineStop;
    public AudioClip[] misfires;

    [Range(0f, 1f)] public float volume = 0.8f;
}