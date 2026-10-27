using UnityEngine;
using UnityEngine.InputSystem;

public class VehicleInteractor : MonoBehaviour
{
    public Transform cameraTransform;
    public float reachDistance = 3f;

    private FirstPersonController fpc;
    private CharacterController cc;
    private CarController currentCar;

    void Start()
    {
        fpc = GetComponent<FirstPersonController>();
        cc = GetComponent<CharacterController>();
    }

    void Update()
    {
        if (!Keyboard.current.fKey.wasPressedThisFrame) return;

        if (currentCar == null) TryEnter();
        else Exit();
    }

    void TryEnter()
    {
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, reachDistance))
        {
            CarController car = hit.collider.GetComponentInParent<CarController>();
            if (car != null && !car.isDriven) Enter(car);
        }
    }

    void Enter(CarController car)
    {
        currentCar = car;
        cc.enabled = false;      // wyłącza kolizje i ruch postaci
        fpc.canMove = false;

        transform.SetParent(car.driverSeat);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        car.isDriven = true;
    }

    void Exit()
    {
        currentCar.isDriven = false;

        transform.SetParent(null);
        transform.position = currentCar.exitPoint.position;
        transform.rotation = Quaternion.Euler(0, currentCar.exitPoint.eulerAngles.y, 0);

        cc.enabled = true;       // włączamy dopiero PO ustawieniu pozycji
        fpc.canMove = true;
        currentCar = null;
    }
}