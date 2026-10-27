using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
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

    [Header("Silnik i hamulce")]
    public float motorTorque = 1500f;
    public float brakeTorque = 3000f;
    public float maxSteerAngle = 30f;

    [Header("Miejsca")]
    public Transform driverSeat;
    public Transform exitPoint;

    [HideInInspector] public bool isDriven;

    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0, -0.3f, 0); // niżej = trudniej przewrócić
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    void FixedUpdate()
    {
        float throttle = 0f;
        float steer = 0f;
        bool handbrake = false;

        if (isDriven)
        {
            Keyboard kb = Keyboard.current;
            if (kb.wKey.isPressed) throttle += 1f;
            if (kb.sKey.isPressed) throttle -= 1f;
            if (kb.dKey.isPressed) steer += 1f;
            if (kb.aKey.isPressed) steer -= 1f;
            handbrake = kb.spaceKey.isPressed;
        }

        // Skręt przednimi kołami
        float steerAngle = steer * maxSteerAngle;
        frontLeft.steerAngle = steerAngle;
        frontRight.steerAngle = steerAngle;

        // Napęd na tył
        rearLeft.motorTorque = throttle * motorTorque;
        rearRight.motorTorque = throttle * motorTorque;

        // Hamulec ręczny, a gdy nikt nie prowadzi, auto stoi
        float brake = (handbrake || !isDriven) ? brakeTorque : 0f;
        frontLeft.brakeTorque = brake;
        frontRight.brakeTorque = brake;
        rearLeft.brakeTorque = brake;
        rearRight.brakeTorque = brake;
    }

    void Update()
    {
        UpdateWheelVisual(frontLeft, frontLeftVisual);
        UpdateWheelVisual(frontRight, frontRightVisual);
        UpdateWheelVisual(rearLeft, rearLeftVisual);
        UpdateWheelVisual(rearRight, rearRightVisual);
    }

    void UpdateWheelVisual(WheelCollider col, Transform visual)
    {
        // Wyłączony collider (brak koła) - slot zostaje tam, gdzie był
        if (!col.enabled) return;

        col.GetWorldPose(out Vector3 pos, out Quaternion rot);
        visual.SetPositionAndRotation(pos, rot);
    }

    void OnGUI()
    {
        if (!isDriven) return;
        float kmh = rb.linearVelocity.magnitude * 3.6f;
        GUI.Label(new Rect(20, 20, 200, 30), $"{kmh:0} km/h");
    }
}