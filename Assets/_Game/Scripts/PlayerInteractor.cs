using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteractor : MonoBehaviour
{
    [Header("Odniesienia")]
    public Transform cameraTransform;
    public Transform holdPoint;

    [Header("Ustawienia")]
    public float reachDistance = 3f;
    public float followStrength = 15f;
    public float throwForce = 10f;

    private Pickupable held;
    private NetworkPickup heldNet; // null = przedmiot tylko lokalny (np. koło)
    private Collider playerCollider;

    void Start()
    {
        playerCollider = GetComponent<CharacterController>();
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (kb.eKey.wasPressedThisFrame)
        {
            if (held == null) TryPickup();
            else if (!TryMount()) Drop();
        }

        if (held != null && mouse.leftButton.wasPressedThisFrame)
        {
            Throw();
        }
    }

    void FixedUpdate()
    {
        if (held == null) return;

        if (heldNet != null)
        {
            // Host odmówił, bo ktoś inny był szybszy
            if (heldNet.IsHeldByOther) { CancelHold(); return; }

            // Czekamy, aż host odda nam własność
            if (!heldNet.IsOwner) return;
        }

        Vector3 toTarget = holdPoint.position - held.rb.position;
        held.rb.linearVelocity = toTarget * followStrength;
        held.rb.angularVelocity = Vector3.Lerp(held.rb.angularVelocity, Vector3.zero, 0.3f);

        if (toTarget.magnitude > reachDistance * 1.5f) Drop();
    }

    void TryPickup()
    {
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, reachDistance)) return;

        Pickupable p = hit.collider.GetComponentInParent<Pickupable>();
        if (p == null) return;

        NetworkPickup np = p.GetComponent<NetworkPickup>();
        if (np != null && np.holderId.Value != NetworkPickup.NoHolder) return; // ktoś trzyma

        WheelPart wheel = p.GetComponent<WheelPart>();
        if (wheel != null && wheel.currentSlot != null)
            wheel.currentSlot.Detach();

        held = p;
        heldNet = np;
        if (np != null) np.RequestPickupServerRpc();

        held.rb.useGravity = false;
        SetIgnorePlayer(true);
    }

    void Drop()
    {
        if (heldNet != null) heldNet.ReleaseServerRpc();
        CancelHold();
    }

    void CancelHold()
    {
        held.rb.useGravity = true;
        SetIgnorePlayer(false);
        held = null;
        heldNet = null;
    }

    void Throw()
    {
        bool canThrow = heldNet == null || heldNet.IsOwner;
        Pickupable p = held;
        Drop();
        if (canThrow)
            p.rb.AddForce(cameraTransform.forward * throwForce, ForceMode.VelocityChange);
    }

    bool TryMount()
    {
        WheelPart wheel = held.GetComponent<WheelPart>();
        if (wheel == null) return false;

        WheelSlot best = null;
        float bestDist = float.MaxValue;
        foreach (WheelSlot slot in FindObjectsByType<WheelSlot>(FindObjectsSortMode.None))
        {
            if (!slot.IsEmpty) continue;
            float d = Vector3.Distance(slot.transform.position, held.transform.position);
            if (d < slot.snapDistance && d < bestDist)
            {
                best = slot;
                bestDist = d;
            }
        }

        if (best == null) return false;

        Drop();
        best.Mount(wheel);
        return true;
    }

    void SetIgnorePlayer(bool ignore)
    {
        foreach (Collider c in held.GetComponentsInChildren<Collider>())
            Physics.IgnoreCollision(c, playerCollider, ignore);
    }
}