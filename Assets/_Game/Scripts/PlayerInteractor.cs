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
    public float pumpDistance = 2f;
    private Pickupable held;
    private NetworkPickup heldNet; // null = przedmiot tylko lokalny (np. koło)
    private Collider playerCollider;
    private float pourTimer;
    private string infoMessage;
    private float infoMessageTime = -10f;
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
        
        if (held == null && kb.tKey.wasPressedThisFrame) TryRemoveTire();

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

        // Opona założona na felgę: E bierze całe koło. Oponę zdejmuje się klawiszem T.
        CarPart part = p.GetComponent<CarPart>();
        if (part != null && part.IsMounted && part.currentCar.OwnerPart != null)
        {
            part = part.currentCar.OwnerPart;
            p = part.pickupable;
        }

        NetworkPickup np = p.GetComponent<NetworkPickup>();
        if (np != null && np.holderId.Value != NetworkPickup.NoHolder) return; // ktoś trzyma

        held = p;
        heldNet = np;

        if (part != null && part.IsMounted)
            part.currentCar.RequestDetachServerRpc(part.slotIndex); // odkręć i daj mi
        else if (np != null)
            np.RequestPickupServerRpc();

        held.rb.useGravity = false;
        SetIgnorePlayer(true);
    }

    void TryRemoveTire()
    {
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, reachDistance)) return;

        Pickupable p = hit.collider.GetComponentInParent<Pickupable>();
        if (p == null) return;

        CarPart part = p.GetComponent<CarPart>();
        if (part == null) return;

        // Patrzymy na oponę albo na felgę z oponą
        CarParts rimSlots;
        int slot;
        if (part.type == PartType.Tire && part.IsMounted)
        {
            rimSlots = part.currentCar;
            slot = part.slotIndex;
        }
        else
        {
            rimSlots = part.GetComponent<CarParts>();
            slot = rimSlots != null ? rimSlots.FindMountedSlot(PartType.Tire) : -1;
        }

        if (rimSlots == null || slot < 0) return;

        if (rimSlots.LockedByParent)
        {
            ShowInfo("Najpierw zdejmij koło z auta");
            return;
        }

        CarPart tire = rimSlots.GetMountedPartAt(slot);
        if (tire == null || tire.net.holderId.Value != NetworkPickup.NoHolder) return;

        held = tire.pickupable;
        heldNet = tire.net;
        rimSlots.RequestDetachServerRpc(slot);

        held.rb.useGravity = false;
        SetIgnorePlayer(true);
    }

    void ShowInfo(string message)
    {
        infoMessage = message;
        infoMessageTime = Time.time;
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

        FuelContainer source = held.GetComponent<FuelContainer>();
        if (source == null || !source.canPourFrom) return;

        bool pour = Mouse.current.rightButton.isPressed;
        bool dump = Keyboard.current.gKey.isPressed;

        if (!pour && !dump)
        {
            pourTimer = 0f;
            return;
        }

        // Przy dystrybutorze PPM napełnia trzymany pojemnik, w innym miejscu przelewa
        FuelPump pump = pour ? FuelPump.FindNearest(source.transform.position, pumpDistance) : null;
        FuelContainer target = (pour && pump == null) ? FindPourTarget(source) : null;
        if (pour && pump == null && target == null) return;

        pourTimer += Time.deltaTime;
        if (pourTimer >= 0.25f)
        {
            if (pump != null) source.FillFromPumpServerRpc(pump.flowRate * pourTimer);
            else if (pour) source.PourIntoServerRpc(target.NetworkObject, source.pourRate * pourTimer);
            else source.DumpServerRpc(source.pourRate * pourTimer);
            pourTimer = 0f;
        }
    }

    FuelContainer FindPourTarget(FuelContainer source)
    {
        FuelContainer best = null;
        float bestDist = pourDistance;

        foreach (FuelContainer c in FindObjectsByType<FuelContainer>(FindObjectsSortMode.None))
        {
            if (c == source) continue;

            float d = Vector3.Distance(c.transform.position, source.transform.position);
            if (d < bestDist)
            {
                best = c;
                bestDist = d;
            }
        }
        return best;
    }

    void OnGUI()
    {
        if (Time.time - infoMessageTime < 2f)
            GUI.Label(new Rect(Screen.width / 2f - 150f, Screen.height / 2f + 100f, 300, 25), infoMessage);

        if (held == null) return;

        FuelContainer source = held.GetComponent<FuelContainer>();
        if (source == null) return;

        float x = Screen.width / 2f - 200f;
        float y = Screen.height / 2f + 40f;

        GUI.Label(new Rect(x, y, 500, 25),
            $"{held.displayName}: {source.Liters:0.00} / {source.capacity:0.#} L  ({source.contents.Value.Describe()})");

        if (!source.canPourFrom) return;

        FuelPump pump = FuelPump.FindNearest(source.transform.position, pumpDistance);
        if (pump != null)
        {
            GUI.Label(new Rect(x, y + 25, 500, 25), $"Dystrybutor: {pump.Label}   [PPM - nalej]");
        }
        else
        {
            FuelContainer target = FindPourTarget(source);
            if (target != null)
                GUI.Label(new Rect(x, y + 25, 500, 25),
                    $"Cel: {target.Liters:0.0} / {target.capacity:0} L  ({target.contents.Value.Describe()})   [PPM - wlej]");
        }

        GUI.Label(new Rect(x, y + 50, 500, 25), "[G - wylej na ziemię]");
    }
}