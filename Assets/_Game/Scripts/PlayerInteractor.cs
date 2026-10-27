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

        // Przyciąganie przedmiotu do punktu przed kamerą
        Vector3 toTarget = holdPoint.position - held.rb.position;
        held.rb.linearVelocity = toTarget * followStrength;

        // Wygaszanie obrotu, żeby się nie kręcił
        held.rb.angularVelocity = Vector3.Lerp(held.rb.angularVelocity, Vector3.zero, 0.3f);

        // Jeśli przedmiot gdzieś utknął daleko, puść go
        if (toTarget.magnitude > reachDistance * 1.5f) Drop();
    }

    void TryPickup()
    {
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, reachDistance))
        {
            Pickupable p = hit.collider.GetComponentInParent<Pickupable>();
            if (p != null)
            {
                // Jeśli to zamontowane koło, najpierw je odkręć
                WheelPart wheel = p.GetComponent<WheelPart>();
                if (wheel != null && wheel.currentSlot != null)
                    wheel.currentSlot.Detach();

                held = p;
                held.rb.useGravity = false;
                SetIgnorePlayer(true);
            }
        }
    }
    bool TryMount()
    {
        WheelPart wheel = held.GetComponent<WheelPart>();
        if (wheel == null) return false;

        // Szukamy najbliższego pustego slotu w zasięgu
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

    void Drop()
    {
        held.rb.useGravity = true;
        SetIgnorePlayer(false);
        held = null;
    }

    void Throw()
    {
        Pickupable p = held;
        Drop();
        p.rb.AddForce(cameraTransform.forward * throwForce, ForceMode.VelocityChange);
    }

    void SetIgnorePlayer(bool ignore)
    {
        // Żeby nie dało się stanąć na trzymanym przedmiocie i odlecieć
        foreach (Collider c in held.GetComponentsInChildren<Collider>())
            Physics.IgnoreCollision(c, playerCollider, ignore);
    }
}