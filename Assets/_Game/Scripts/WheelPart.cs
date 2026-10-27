using UnityEngine;

[RequireComponent(typeof(Pickupable))]
public class WheelPart : MonoBehaviour
{
    public float radius = 0.35f;

    [HideInInspector] public WheelSlot currentSlot;
    [HideInInspector] public Pickupable pickupable;

    private Collider[] colliders;

    void Awake()
    {
        pickupable = GetComponent<Pickupable>();
        colliders = GetComponentsInChildren<Collider>();
    }

    public void SetMounted(bool mounted)
    {
        // Zamontowane koło: bez własnej fizyki, jedzie razem z autem
        pickupable.rb.isKinematic = mounted;
        pickupable.rb.interpolation = mounted
            ? RigidbodyInterpolation.None
            : RigidbodyInterpolation.Interpolate;

        // Trigger = nie zderza się z niczym, ale da się w nie "wycelować" i je zdjąć
        foreach (Collider c in colliders)
            c.isTrigger = mounted;
    }
}