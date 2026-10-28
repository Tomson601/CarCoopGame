using UnityEngine;
using UnityEngine.InputSystem;

public class VehicleInteractor : MonoBehaviour
{
    public Transform cameraTransform;
    public float reachDistance = 3f;

    private FirstPersonController fpc;
    private CarSeats currentCar;

    void Start()
    {
        fpc = GetComponent<FirstPersonController>();
    }

    void Update()
    {
        if (!Keyboard.current.fKey.wasPressedThisFrame) return;

        if (fpc.IsSeated)
        {
            if (currentCar != null) currentCar.RequestExitServerRpc();
        }
        else
        {
            TryEnter();
        }
    }

    void TryEnter()
    {
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, reachDistance)) return;

        CarSeats car = hit.collider.GetComponentInParent<CarSeats>();
        if (car == null) return;

        currentCar = car;
        car.RequestSeatServerRpc(car.GetNearestSeatIndex(hit.point));
    }
}