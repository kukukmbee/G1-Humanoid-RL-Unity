using UnityEngine;

public class WallCollisionSensor : MonoBehaviour
{
    private G1Agent agent;

    private void Start()
    {
        agent = GetComponentInParent<G1Agent>();
        if (agent == null && transform.parent != null)
        {
            agent = transform.parent.GetComponentInChildren<G1Agent>();
        }
        if (agent == null)
        {
            Debug.LogError($"[{gameObject.name}] WallCollisionSensor: не найден локальный G1Agent в иерархии родителя!");
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        string otherName = collision.gameObject.name.ToLower();
        if (otherName.Contains("wall") || collision.gameObject.CompareTag("wall"))
        {
            // Жесткий штраф за первоначальный удар о стену (больше, чем награда за коробку)
            if (agent != null)
            {
                agent.AddReward(-15.0f);
                Debug.Log($"[{agent.gameObject.name}] БУМ! Жесткое столкновение {gameObject.name} со стеной! Штраф: -15.0");
            }
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        string otherName = collision.gameObject.name.ToLower();
        if (otherName.Contains("wall") || collision.gameObject.CompareTag("wall"))
        {
            // Постоянный штраф за трение/прижимание к стене (препятствует абьюзу)
            if (agent != null)
            {
                agent.AddReward(-0.02f);
            }
        }
    }
}
