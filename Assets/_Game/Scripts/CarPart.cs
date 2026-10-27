using UnityEngine;

public enum PartType
{
    Wheel,
    Engine,
    Gearbox,
    FuelTank,
    Radiator
}

[RequireComponent(typeof(Pickupable))]
[RequireComponent(typeof(NetworkPickup))]
public class CarPart : MonoBehaviour
{
    public PartType type;

    [HideInInspector] public CarParts currentCar;
    [HideInInspector] public int slotIndex = -1;
    [HideInInspector] public Pickupable pickupable;
    [HideInInspector] public NetworkPickup net;

    private Collider[] colliders;

    public bool IsMounted => currentCar != null;

    void Awake()
    {
        pickupable = GetComponent<Pickupable>();
        net = GetComponent<NetworkPickup>();
        colliders = GetComponentsInChildren<Collider>();
    }

    void FixedUpdate()
    {
        if (IsMounted && !pickupable.rb.isKinematic)
            pickupable.rb.isKinematic = true;
    }

    public void SetMounted(bool mounted, CarParts car, int slot)
    {
        currentCar = mounted ? car : null;
        slotIndex = mounted ? slot : -1;

        pickupable.rb.isKinematic = mounted || !net.IsOwner;
        pickupable.rb.interpolation = mounted
            ? RigidbodyInterpolation.None
            : RigidbodyInterpolation.Interpolate;

        foreach (Collider c in colliders)
            c.isTrigger = mounted;
    }
}