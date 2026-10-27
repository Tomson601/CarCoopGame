using UnityEngine;

public class WheelSlot : MonoBehaviour
{
    public WheelCollider wheelCollider;
    public float snapDistance = 1f;

    [HideInInspector] public WheelPart mounted;

    public bool IsEmpty => mounted == null;

    void Start()
    {
        // Jeśli w prefabie auta koło już siedzi w slocie, zamontuj je
        WheelPart existing = GetComponentInChildren<WheelPart>();
        if (existing != null) Mount(existing);
        else wheelCollider.enabled = false;
    }

    public void Mount(WheelPart wheel)
    {
        mounted = wheel;
        wheel.currentSlot = this;
        wheel.SetMounted(true);

        wheel.transform.SetParent(transform);
        wheel.transform.localPosition = Vector3.zero;
        wheel.transform.localRotation = Quaternion.identity;

        wheelCollider.radius = wheel.radius;
        wheelCollider.enabled = true;
    }

    public WheelPart Detach()
    {
        WheelPart wheel = mounted;
        mounted = null;
        wheel.currentSlot = null;

        wheel.transform.SetParent(null);
        wheel.SetMounted(false);

        wheelCollider.enabled = false; // bez koła ten róg auta opada
        return wheel;
    }
}