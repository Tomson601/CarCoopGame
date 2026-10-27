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
    public float pourDistance = 1.5f;

    private Pickupable held;
    private NetworkPickup heldNet; // null = przedmiot tylko lokalny (np. koło)
    private Collider playerCollider;
    private float pourTimer;

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
        HandlePouring();
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
            // Koło jeszcze siedzi w aucie, czekamy na odkręcenie
            if (held.rb.isKinematic) return;
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

        held = p;
        heldNet = np;

        CarPart part = p.GetComponent<CarPart>();
        if (part != null && part.IsMounted)
            part.currentCar.RequestDetachServerRpc(part.slotIndex); // odkręć i daj mi
        else if (np != null)
            np.RequestPickupServerRpc();

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
        CarPart part = held.GetComponent<CarPart>();
        if (part == null || heldNet == null || !heldNet.IsOwner) return false;

        CarParts bestCar = null;
        int bestSlot = -1;
        float bestDist = float.MaxValue;

        foreach (CarParts car in FindObjectsByType<CarParts>(FindObjectsSortMode.None))
        {
            if (car.TryFindEmptySlot(held.transform.position, part.type, out int slot, out float dist) && dist < bestDist)
            {
                bestCar = car;
                bestSlot = slot;
                bestDist = dist;
            }
        }

        if (bestCar == null) return false;

        bestCar.RequestMountServerRpc(bestSlot, heldNet.NetworkObject);
        CancelHold();
        return true;
    }

    void SetIgnorePlayer(bool ignore)
    {
        foreach (Collider c in held.GetComponentsInChildren<Collider>())
            Physics.IgnoreCollision(c, playerCollider, ignore);
    }
    void HandlePouring()
    {
        if (held == null || heldNet == null || !heldNet.IsOwner) return;

        Jerrycan can = held.GetComponent<Jerrycan>();
        if (can == null) return;

        if (!Mouse.current.rightButton.isPressed)
        {
            pourTimer = 0f;
            return;
        }

        FuelTankPart tank = FindNearestTank(held.transform.position);
        if (tank == null) return;

        // Wysyłaj co ćwierć sekundy, a nie co klatkę
        pourTimer += Time.deltaTime;
        if (pourTimer >= 0.25f)
        {
            can.PourIntoServerRpc(tank.NetworkObject, can.pourRate * pourTimer);
            pourTimer = 0f;
        }
    }

    FuelTankPart FindNearestTank(Vector3 position)
    {
        FuelTankPart best = null;
        float bestDist = pourDistance;

        foreach (FuelTankPart tank in FindObjectsByType<FuelTankPart>(FindObjectsSortMode.None))
        {
            float d = Vector3.Distance(tank.transform.position, position);
            if (d < bestDist)
            {
                best = tank;
                bestDist = d;
            }
        }
        return best;
    }

    void OnGUI()
    {
        if (held == null) return;

        Jerrycan can = held.GetComponent<Jerrycan>();
        if (can == null) return;

        float x = Screen.width / 2f - 150f;
        float y = Screen.height / 2f + 40f;

        GUI.Label(new Rect(x, y, 300, 25), $"Kanister: {can.liters.Value:0.0} / {can.capacity:0} L");

        FuelTankPart tank = FindNearestTank(held.transform.position);
        if (tank != null)
            GUI.Label(new Rect(x, y + 25, 400, 25),
                $"Zbiornik: {tank.fuel.Value:0.0} / {tank.capacity:0} L   [PPM - wlej]");
    }
}