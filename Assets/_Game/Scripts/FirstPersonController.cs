using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : NetworkBehaviour
{
    [Header("Ruch")]
    public float walkSpeed = 4f;
    public float sprintSpeed = 7f;
    public float jumpHeight = 1.2f;
    public float gravity = -20f;

    [Header("Kamera")]
    public Transform cameraTransform;
    public float mouseSensitivity = 0.1f;
    public float maxLookAngle = 85f;

    [HideInInspector] public bool canMove = true;

    private CharacterController controller;
    private float verticalVelocity;
    private float cameraPitch;
    private Transform seat;
    private float seatYaw;
    [Header("Pchanie")]
    public float pushForce = 1500f; // N, mniej więcej siła jednego człowieka pchającego auto

    private Vector3 pushImpulse;
    private CarController pushCar;
    private float lastPushSend;
    public bool IsSeated => seat != null;

    public override void OnNetworkSpawn()
    {
        controller = GetComponent<CharacterController>();

        if (!IsOwner)
        {
            // Cudza postać: wyłączamy jej kamerę, dźwięk i interakcje
            cameraTransform.GetComponent<Camera>().enabled = false;
            cameraTransform.GetComponent<AudioListener>().enabled = false;
            GetComponent<PlayerInteractor>().enabled = false;
            GetComponent<VehicleInteractor>().enabled = false;
            return;
        }

        // Nasza postać: wyłącz kamerę z menu
        GameObject lobbyCam = GameObject.Find("LobbyCamera");
        if (lobbyCam != null) lobbyCam.SetActive(false);

        // Losowy punkt startu, żeby gracze nie pojawili się w sobie
        controller.enabled = false;
        transform.position = new Vector3(Random.Range(-3f, 3f), 1f, Random.Range(-3f, 3f));
        controller.enabled = true;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (!IsOwner) return;

        HandleLook();
        if (canMove) HandleMovement();
        SendPush();
    }
    void LateUpdate()
    {
        if (seat == null) return;

        // Postać zawsze dokładnie w fotelu, u wszystkich graczy
        transform.position = seat.position;

        // Własny gracz: obrót = obrót auta + rozglądanie się na boki
        if (IsOwner)
            transform.rotation = seat.rotation * Quaternion.Euler(0f, seatYaw, 0f);
    }

    public void SetSeat(Transform newSeat, Transform exitPoint = null)
    {
        // Wysiadanie: przenieś się obok auta, zanim włączysz kolizje
        if (newSeat == null && IsOwner && exitPoint != null)
        {
            transform.position = exitPoint.position;
            transform.rotation = Quaternion.Euler(0f, exitPoint.eulerAngles.y, 0f);
        }

        seat = newSeat;
        seatYaw = 0f;
        controller.enabled = (seat == null);
        canMove = (seat == null);
    }
    // Unity wywołuje to, gdy CharacterController wpada na collider
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!IsOwner) return;

        CarController car = hit.collider.GetComponentInParent<CarController>();
        if (car == null) return;

        Vector3 dir = hit.moveDirection;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;

        pushImpulse += dir.normalized * pushForce * Time.deltaTime;
        pushCar = car;
    }

    void SendPush()
    {
        // Wysyłaj 10 razy na sekundę, a nie co klatkę
        if (pushCar == null || Time.time - lastPushSend < 0.1f) return;

        pushCar.PushRpc(pushImpulse);
        pushImpulse = Vector3.zero;
        pushCar = null;
        lastPushSend = Time.time;
    }
    void HandleLook()
    {
        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * mouseSensitivity;

        if (seat != null)
            seatYaw = Mathf.Clamp(seatYaw + mouseDelta.x, -120f, 120f); // w aucie: głowa do 120° w bok
        else
            transform.Rotate(Vector3.up * mouseDelta.x);

        cameraPitch -= mouseDelta.y;
        cameraPitch = Mathf.Clamp(cameraPitch, -maxLookAngle, maxLookAngle);
        cameraTransform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
    }

    void HandleMovement()
    {
        Keyboard kb = Keyboard.current;

        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed) input.y += 1;
        if (kb.sKey.isPressed) input.y -= 1;
        if (kb.dKey.isPressed) input.x += 1;
        if (kb.aKey.isPressed) input.x -= 1;
        input = Vector2.ClampMagnitude(input, 1f);

        float speed = kb.leftShiftKey.isPressed ? sprintSpeed : walkSpeed;
        Vector3 move = (transform.right * input.x + transform.forward * input.y) * speed;

        if (controller.isGrounded)
        {
            if (verticalVelocity < 0) verticalVelocity = -2f;
            if (kb.spaceKey.wasPressedThisFrame)
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        verticalVelocity += gravity * Time.deltaTime;
        move.y = verticalVelocity;

        controller.Move(move * Time.deltaTime);

        if (kb.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}