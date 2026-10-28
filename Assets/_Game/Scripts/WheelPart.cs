using UnityEngine;

[RequireComponent(typeof(CarPart))]
public class WheelPart : MonoBehaviour
{
    public string rimName = "Felga stalowa";

    [Tooltip("Promień samej felgi, czyli jazda bez opony")]
    public float rimRadius = 0.25f;

    [Tooltip("Collider wielkości opony, włączany, gdy opona jest założona")]
    public Collider tireCollider;

    private CarParts rimSlots;

    void Awake()
    {
        rimSlots = GetComponent<CarParts>();
    }

    void Update()
    {
        // Koło z oponą toczy się po ziemi na oponie, a nie na feldze
        if (tireCollider != null) tireCollider.enabled = GetTire() != null;
    }

    public TirePart GetTire()
    {
        return rimSlots != null ? rimSlots.GetMounted<TirePart>() : null;
    }

    public float GetEffectiveRadius()
    {
        TirePart tire = GetTire();
        if (tire == null) return rimRadius;                // goła felga
        if (tire.health.Value <= 0f) return rimRadius + 0.03f; // flak
        return tire.outerRadius;
    }
}