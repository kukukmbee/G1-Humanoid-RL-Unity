using UnityEngine;

public class FreeCamera : MonoBehaviour
{
    [Header("Camera Settings")]
    public float movementSpeed = 10f;
    public float lookSensitivity = 2f;
    
    private float rotationX = 0f;
    private float rotationY = 0f;

    void Start()
    {
        // Считываем начальные углы поворота камеры
        Vector3 rot = transform.localRotation.eulerAngles;
        rotationX = rot.y;
        rotationY = -rot.x;
    }

    void Update()
    {
        // Камера летает и вращается только при зажатой ПРАВОЙ кнопке мыши
        if (Input.GetMouseButton(1))
        {
            // Вращение мыши
            rotationX += Input.GetAxis("Mouse X") * lookSensitivity;
            rotationY += Input.GetAxis("Mouse Y") * lookSensitivity;
            rotationY = Mathf.Clamp(rotationY, -90f, 90f);

            transform.localRotation = Quaternion.AngleAxis(rotationX, Vector3.up) * Quaternion.AngleAxis(rotationY, Vector3.left);

            // Движение (WASD + Q/E для вертикального перемещения)
            float moveForward = Input.GetAxis("Vertical");
            float moveSideways = Input.GetAxis("Horizontal");
            float moveUpDown = 0f;

            if (Input.GetKey(KeyCode.E)) moveUpDown = 1f;  // Вверх
            if (Input.GetKey(KeyCode.Q)) moveUpDown = -1f; // Вниз

            Vector3 moveDirection = transform.forward * moveForward + transform.right * moveSideways + Vector3.up * moveUpDown;
            
            // Используем Time.unscaledDeltaTime, чтобы скорость камеры оставалась нормальной (1х),
            // даже когда симуляция ML-Agents ускорена до 20х!
            transform.position += moveDirection * movementSpeed * Time.unscaledDeltaTime;
        }
    }
}
