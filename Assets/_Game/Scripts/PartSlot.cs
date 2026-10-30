using UnityEngine;

public class PartSlot : MonoBehaviour
{
    public PartType acceptedType = PartType.Wheel;
    public float snapDistance = 1f;

    [Tooltip("Tylko dla slotów kół")]
    public WheelCollider wheelCollider;
    [Tooltip("Obróć część o 180° (np. koła po lewej stronie, żeby felga patrzyła na zewnątrz)")]
    public bool mirrorPart;
}