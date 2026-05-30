using UnityEngine;

public class FootSensor : MonoBehaviour
{
    public bool isGrounded { get; private set; }

    private void OnCollisionStay(Collision collision)
    {
        // Проверяем контакт с полом/землей
        isGrounded = true;
    }

    private void OnCollisionExit(Collision collision)
    {
        isGrounded = false;
    }

    private void FixedUpdate()
    {
        // Дополнительный сброс на случай резких физических телепортаций
        if (Physics.OverlapSphere(transform.position, 0.05f).Length <= 1)
        {
            isGrounded = false;
        }
    }
}
