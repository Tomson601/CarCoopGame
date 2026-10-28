using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

// Fizyka jazdy: sterowanie, hamulce, opory, wygląd kół, pchanie.
// Napęd liczy CarEngine, opony CarTires, fotele CarSeats, a HUD wyświetla CarHUD.
[RequireComponent(typeof(Rigidbody))]
public class CarController : NetworkBehaviour
{
    [Header("Koła - fizyka")]
    public WheelCollider frontLeft;
    public WheelCollider frontRight;
    public WheelCollider rearLeft;
    public WheelCollider rearRight;

    [Header("Koła - wygląd")]
    public Transform frontLeftVisual;
    public Transform frontRightVisual;
    public Transform rearLeftVisual;
    public Transform rearRightVisual;

    [Header("Hamulce i skręt")]
    public float brakeTorque = 3000f;
    public float handbrakeTorque = 5000f;
    public float maxSteerAngle = 30f;

    [Header("Napęd i opory")]
    [Range(0.5f, 1f)] public float drivetrainEfficiency = 0.85f;
    [Tooltip("Opór powietrza. Większy = niższa prędkość maksymalna.")]
    public float airDrag = 0.4f;
    [Tooltip("Współczynnik oporu toczenia. Typowo 0.01-0.02 na asfalcie.")]
    public float rollingResistance = 0.015f;

    // Kąt skrętu przesyłany do pozostałych (tylko do wyglądu kół)
    private NetworkVariable<float> syncedSteer = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Ręczny: widoczny w sieci i zachowany po zmianie kierowcy. Auta startują z zaciągniętym.
    private NetworkVariable<bool> handbrakeOn = new NetworkVariable<bool>(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private Rigidbody rb;
    private CarSeats seats;
    private CarEngine engine;
    private CarTires tires;

    private float[] wheelSpin = new float[4];
    private Vector3 lastPosition;

    public Rigidbody Body => rb;
    public float SpeedKmh => rb.linearVelocity.magnitude * 3.6f;
    public bool HandbrakeOn => handbrakeOn.Value;
    public bool IsDrivenByMe => seats.IsDrivenByMe;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        seats = GetComponent<CarSeats>();
        engine = GetComponent<CarEngine>();
        tires = GetComponent<CarTires>();

        rb.centerOfMass = new Vector3(0, -0.3f, 0);
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.sleepThreshold = 0f;
    }

    // ---------- Jazda (tylko u właściciela auta) ----------

    void FixedUpdate()
    {
        if (!IsSpawned || !IsOwner)
        {
            engine.OnNotSimulating();
            return;
        }

        float throttle = 0f;
        float footBrake = 0f;
        float steer = 0f;

        if (IsDrivenByMe)
        {
            Keyboard kb = Keyboard.current;
            if (kb.wKey.isPressed) throttle = 1f;
            if (kb.sKey.isPressed) footBrake = 1f;
            if (kb.dKey.isPressed) steer += 1f;
            if (kb.aKey.isPressed) steer -= 1f;
        }

        // Skręt
        float steerAngle = steer * maxSteerAngle;
        frontLeft.steerAngle = steerAngle;
        frontRight.steerAngle = steerAngle;

        if (Mathf.Abs(syncedSteer.Value - steerAngle) > 0.5f)
            syncedSteer.Value = steerAngle;

        // Napęd i opony
        float driveTorque = engine.Simulate(throttle);
        tires.Simulate(Time.fixedDeltaTime);

        rearLeft.motorTorque = driveTorque * 0.5f;
        rearRight.motorTorque = driveTorque * 0.5f;

        if (Mathf.Abs(driveTorque) > 1f || Mathf.Abs(steerAngle) > 0.1f)
            rb.WakeUp();

        // Hamulce. Bez minimalnego hamulca: WheelCollider blokuje stojące koło nawet małym brakeTorque.
        float brake = footBrake * brakeTorque;
        frontLeft.brakeTorque = brake;
        frontRight.brakeTorque = brake;

        float rearBrake = brake;
        if (handbrakeOn.Value) rearBrake = Mathf.Max(rearBrake, handbrakeTorque);
        if (engine.GearHolding) rearBrake = Mathf.Max(rearBrake, brakeTorque);
        rearLeft.brakeTorque = rearBrake;
        rearRight.brakeTorque = rearBrake;

        // Opory
        Vector3 v = rb.linearVelocity;
        if (v.magnitude > 0.1f)
            rb.AddForce(-v.normalized * rollingResistance * rb.mass * 9.81f);
        rb.AddForce(-v * v.magnitude * airDrag);
    }

    // ---------- Sterowanie i wygląd ----------

    void Update()
    {
        if (IsDrivenByMe)
        {
            engine.HandleInput();
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
                handbrakeOn.Value = !handbrakeOn.Value;
        }
        else
        {
            engine.StopCranking();
        }

        Vector3 velocity = (transform.position - lastPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
        lastPosition = transform.position;
        float forwardSpeed = Vector3.Dot(velocity, transform.forward);

        bool simulated = !IsSpawned || IsOwner;

        UpdateWheelVisual(frontLeft, frontLeftVisual, 0, true, simulated, forwardSpeed);
        UpdateWheelVisual(frontRight, frontRightVisual, 1, true, simulated, forwardSpeed);
        UpdateWheelVisual(rearLeft, rearLeftVisual, 2, false, simulated, forwardSpeed);
        UpdateWheelVisual(rearRight, rearRightVisual, 3, false, simulated, forwardSpeed);
    }

    void UpdateWheelVisual(WheelCollider col, Transform visual, int index, bool isFront, bool simulated, float forwardSpeed)
    {
        if (!col.enabled) return;

        col.GetWorldPose(out Vector3 pos, out Quaternion rot);

        if (!simulated)
        {
            wheelSpin[index] += forwardSpeed / col.radius * Mathf.Rad2Deg * Time.deltaTime;
            float steer = isFront ? syncedSteer.Value : 0f;
            rot = transform.rotation * Quaternion.Euler(wheelSpin[index], steer, 0f);
        }

        visual.SetPositionAndRotation(pos, rot);
    }

    // Wywoływane przez pchającego gracza, wykonywane u właściciela auta
    [Rpc(SendTo.Owner)]
    public void PushRpc(Vector3 impulse)
    {
        rb.AddForce(impulse, ForceMode.Impulse);
    }
}