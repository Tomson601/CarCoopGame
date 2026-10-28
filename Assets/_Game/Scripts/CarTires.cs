using UnityEngine;
using Unity.Netcode;

// Opony: przyczepność zależna od opony i nawierzchni, zużycie.
public class CarTires : NetworkBehaviour
{
    private CarParts parts;
    private WheelCollider[] wheels; // LP, PP, LT, PT
    private float[] pendingWear = new float[4];
    private SurfaceType[] surfaces = new SurfaceType[4];
    private float flushTimer;

    GameTuning Tuning => GameTuning.Instance;

    public SurfaceType RearSurface => surfaces[2];

    void Awake()
    {
        parts = GetComponent<CarParts>();
        CarController car = GetComponent<CarController>();
        wheels = new[] { car.frontLeft, car.frontRight, car.rearLeft, car.rearRight };
    }

    // Wywoływane co FixedUpdate u właściciela auta
    public void Simulate(float dt)
    {
        for (int i = 0; i < wheels.Length; i++)
            UpdateTire(i, dt);

        flushTimer += dt;
        if (flushTimer >= 1f)
        {
            SyncTireWearServerRpc(pendingWear[0], pendingWear[1], pendingWear[2], pendingWear[3]);
            for (int i = 0; i < pendingWear.Length; i++) pendingWear[i] = 0f;
            flushTimer = 0f;
        }
    }

    void UpdateTire(int index, float dt)
    {
        WheelCollider col = wheels[index];
        if (!col.enabled) return;

        TirePart tire = GetTireOn(col);
        bool grounded = col.GetGroundHit(out WheelHit hit);
        SurfaceType surface = grounded ? GetSurface(hit) : SurfaceType.Dirt;
        surfaces[index] = surface;

        float grip;
        if (tire == null) grip = Tuning.bareRimGrip;
        else if (tire.health.Value <= 0f) grip = Tuning.flatTireGrip;
        else grip = tire.GetGrip(surface);
        SetGrip(col, grip);

        if (tire != null && grounded)
        {
            float speed = Mathf.Abs(col.rpm) * 2f * Mathf.PI * col.radius / 60f;
            float slip = Mathf.Abs(hit.forwardSlip) + Mathf.Abs(hit.sidewaysSlip);
            pendingWear[index] += tire.wearRate * tire.GetWearMultiplier(surface)
                                  * (speed * Tuning.tireDistanceWear + slip * Tuning.tireSlipWear) * dt;
        }
    }

    TirePart GetTireOn(WheelCollider col)
    {
        CarPart part = parts.GetMountedOnCollider(col);
        if (part == null) return null;
        WheelPart rim = part.GetComponent<WheelPart>();
        if (rim == null) return null;
        return rim.GetTire();
    }

    SurfaceType GetSurface(WheelHit hit)
    {
        Surface surface = hit.collider.GetComponentInParent<Surface>();
        return surface != null ? surface.type : SurfaceType.Dirt;
    }

    void SetGrip(WheelCollider col, float grip)
    {
        WheelFrictionCurve forward = col.forwardFriction;
        forward.stiffness = grip;
        col.forwardFriction = forward;

        WheelFrictionCurve sideways = col.sidewaysFriction;
        sideways.stiffness = grip;
        col.sidewaysFriction = sideways;
    }

    [ServerRpc]
    void SyncTireWearServerRpc(float fl, float fr, float rl, float rr)
    {
        ApplyWear(0, fl);
        ApplyWear(1, fr);
        ApplyWear(2, rl);
        ApplyWear(3, rr);
    }

    void ApplyWear(int index, float wear)
    {
        if (wear <= 0f) return;
        TirePart tire = GetTireOn(wheels[index]);
        if (tire != null) tire.ServerWear(wear);
    }

    // ---------- Do HUD ----------

    public string GetTireText(int index, string label)
    {
        CarPart part = parts.GetMountedOnCollider(wheels[index]);
        if (part == null) return $"{label}: brak";

        TirePart tire = GetTireOn(wheels[index]);
        if (tire == null) return $"{label}: goła felga!";
        if (tire.health.Value <= 0f) return $"{label}: FLAK";
        return $"{label}: {tire.health.Value:0}%";
    }

    public static string GetSurfaceName(SurfaceType surface)
    {
        return surface switch
        {
            SurfaceType.Asphalt => "asfalt",
            SurfaceType.Grass => "trawa",
            _ => "ziemia"
        };
    }
}