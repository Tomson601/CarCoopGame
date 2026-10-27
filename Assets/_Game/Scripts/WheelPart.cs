using UnityEngine;

[RequireComponent(typeof(Pickupable))]
[RequireComponent(typeof(NetworkPickup))]
public class WheelPart : MonoBehaviour
{
    public float radius = 0.35f;

    [HideInInspector] public CarWheels currentCar;
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
        // Zamontowane koło nigdy nie ma własnej fizyki,
        // nawet jeśli Netcode próbuje ją włączyć przy zmianie właściciela
        if (IsMounted && !pickupable.rb.isKinematic)
            pickupable.rb.isKinematic = true;
    }

    public void SetMounted(bool mounted, CarWheels car, int slot)
    {
        currentCar = mounted ? car : null;
        slotIndex = mounted ? slot : -1;

        // Po zdjęciu fizykę liczy tylko właściciel koła
        pickupable.rb.isKinematic = mounted || !net.IsOwner;
        pickupable.rb.interpolation = mounted
            ? RigidbodyInterpolation.None
            : RigidbodyInterpolation.Interpolate;

        foreach (Collider c in colliders)
            c.isTrigger = mounted;
    }
}