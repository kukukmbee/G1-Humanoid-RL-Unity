using UnityEngine;
using System.Collections.Generic;

public class G1MissionController : MonoBehaviour
{
    [Header("References")]
    public G1Agent agent;                  // Ссылка на агента G1
    public Transform boxObject;            // Ссылка на коробку, которую нужно принести
    public Transform robotAttachPoint;      // Точка на роботе (например, рука/грудь), куда прикрепится коробка
    public Transform returnTarget;         // Финальная точка базы (старт), куда нужно вернуть коробку

    [Header("Waypoints Path")]
    public List<Transform> waypointsToBox = new List<Transform>();  // Путь к коробке (повороты)
    public List<Transform> waypointsToHome = new List<Transform>(); // Путь обратно к базе (повороты)

    [Header("Settings")]
    public float waypointThreshold = 1.2f;  // Расстояние до точки, при котором она считается пройденной
    public float boxPickupRange = 1.0f;     // Расстояние, чтобы взять коробку

    private int currentWaypointIndex = 0;
    private bool isCarryingBox = false;
    private bool missionCompleted = false;

    // Переменные для запоминания исходного положения коробки для сброса
    private Vector3 initialBoxPosition;
    private Quaternion initialBoxRotation;

    private void Start()
    {
        if (agent == null) agent = GetComponent<G1Agent>();

        // Запоминаем стартовую позицию коробки, чтобы возвращать её на место при падении робота
        if (boxObject != null)
        {
            initialBoxPosition = boxObject.position;
            initialBoxRotation = boxObject.rotation;
        }
        
        // Задаем первую цель
        UpdateTarget();
    }

    private void FixedUpdate()
    {
        if (agent == null) return;

        // АВТО-СБРОС МИССИИ ПРИ ПАДЕНИИ РОБОТА:
        // Если агент упал и начался новый эпизод (StepCount сбросился в 0 или 1),
        // мы возвращаем коробку на место и начинаем миссию заново!
        if (agent.StepCount <= 1 && (isCarryingBox || currentWaypointIndex != 0 || missionCompleted))
        {
            ResetMission();
        }

        if (missionCompleted) return;

        // Находим реальную позицию физического тела робота (таза) для точного расчета
        ArticulationBody rootBody = agent.GetComponentInChildren<ArticulationBody>();
        Vector3 robotPos = rootBody != null ? rootBody.transform.position : agent.transform.position;

        if (!isCarryingBox)
        {
            // --- ЭТАП 1: ИДЕМ К КОРОБКЕ ---
            if (currentWaypointIndex < waypointsToBox.Count)
            {
                Transform currentWaypoint = waypointsToBox[currentWaypointIndex];
                float distToWaypoint = Vector3.Distance(robotPos, currentWaypoint.position);

                if (distToWaypoint < waypointThreshold)
                {
                    Debug.Log($"[G1Mission] Точка пути к коробке #{currentWaypointIndex} ({currentWaypoint.name}) пройдена!");
                    currentWaypointIndex++;
                    UpdateTarget();
                }
            }
            else
            {
                // Все промежуточные точки пройдены, идем вплотную к самой коробке
                float distToBox = Vector3.Distance(robotPos, boxObject.position);
                if (distToBox < boxPickupRange)
                {
                    PickUpBox();
                }
            }
        }
        else
        {
            // --- ЭТАП 2: НЕСЕМ КОРОБКУ ДОМОЙ ---
            if (currentWaypointIndex < waypointsToHome.Count)
            {
                Transform currentWaypoint = waypointsToHome[currentWaypointIndex];
                float distToWaypoint = Vector3.Distance(robotPos, currentWaypoint.position);

                if (distToWaypoint < waypointThreshold)
                {
                    Debug.Log($"[G1Mission] Точка пути домой #{currentWaypointIndex} ({currentWaypoint.name}) пройдена!");
                    currentWaypointIndex++;
                    UpdateTarget();
                }
            }
            else
            {
                // Финальная точка возврата (База)
                float distToHome = Vector3.Distance(robotPos, returnTarget.position);
                if (distToHome < waypointThreshold)
                {
                    CompleteMission();
                }
            }
        }
    }

    private void UpdateTarget()
    {
        if (!isCarryingBox)
        {
            if (currentWaypointIndex < waypointsToBox.Count)
            {
                agent.target = waypointsToBox[currentWaypointIndex];
                Debug.Log($"[G1Mission] Цель установлена на путевую точку к коробке: {agent.target.name}");
            }
            else if (boxObject != null)
            {
                agent.target = boxObject;
                Debug.Log($"[G1Mission] Все точки пройдены. Идем брать коробку: {boxObject.name}");
            }
        }
        else
        {
            if (currentWaypointIndex < waypointsToHome.Count)
            {
                agent.target = waypointsToHome[currentWaypointIndex];
                Debug.Log($"[G1Mission] Цель установлена на путевую точку домой: {agent.target.name}");
            }
            else if (returnTarget != null)
            {
                agent.target = returnTarget;
                Debug.Log($"[G1Mission] Идем на базу к финишу: {returnTarget.name}");
            }
        }
    }

    private void PickUpBox()
    {
        if (boxObject == null || isCarryingBox) return;

        Debug.Log("[G1Mission] 📦 Ура! Робот подошел к коробке. Производим захват!");
        isCarryingBox = true;
        currentWaypointIndex = 0; // Сбрасываем индекс для обратного пути

        // 1. Выключаем физику коробки, чтобы она не мешала ногам робота и не вызывала тряску
        Rigidbody boxRb = boxObject.GetComponent<Rigidbody>();
        if (boxRb != null)
        {
            boxRb.isKinematic = true;
            boxRb.useGravity = false;
        }
        Collider boxCollider = boxObject.GetComponent<Collider>();
        if (boxCollider != null)
        {
            boxCollider.enabled = false;
        }

        // 2. Делаем коробку дочерним объектом робота (крепим в точку захвата)
        if (robotAttachPoint != null)
        {
            boxObject.SetParent(robotAttachPoint);
            boxObject.localPosition = Vector3.zero;
            boxObject.localRotation = Quaternion.identity;
        }
        else
        {
            // Резервный вариант, если точка крепления не задана (вешаем перед грудью)
            boxObject.SetParent(agent.transform);
            boxObject.localPosition = new Vector3(0f, 0.4f, 0.35f); 
            boxObject.localRotation = Quaternion.identity;
        }

        // 3. Обновляем цель на первую точку пути домой
        UpdateTarget();
    }

    private void CompleteMission()
    {
        missionCompleted = true;
        Debug.Log("[G1Mission] 🎉 МИССИЯ УСПЕШНО ВЫПОЛНЕНА! Коробка возвращена на базу!");

        // Отпускаем коробку и возвращаем ей физику на финише
        if (boxObject != null)
        {
            boxObject.SetParent(null);
            Rigidbody boxRb = boxObject.GetComponent<Rigidbody>();
            if (boxRb != null)
            {
                boxRb.isKinematic = false;
                boxRb.useGravity = true;
            }
            Collider boxCollider = boxObject.GetComponent<Collider>();
            if (boxCollider != null)
            {
                boxCollider.enabled = true;
            }
        }

        // Останавливаем робота (направляем цель на самого себя, чтобы стоял смирно)
        agent.target = agent.transform;
    }

    private void ResetMission()
    {
        Debug.Log("[G1Mission] 🔄 Робот упал и сбросился. Возвращаем коробку на место и перезапускаем миссию.");
        isCarryingBox = false;
        missionCompleted = false;
        currentWaypointIndex = 0;

        if (boxObject != null)
        {
            boxObject.SetParent(null);
            boxObject.position = initialBoxPosition;
            boxObject.rotation = initialBoxRotation;

            Rigidbody boxRb = boxObject.GetComponent<Rigidbody>();
            if (boxRb != null)
            {
                boxRb.isKinematic = false;
                boxRb.useGravity = true;
                boxRb.linearVelocity = Vector3.zero;
                boxRb.angularVelocity = Vector3.zero;
            }
            Collider boxCollider = boxObject.GetComponent<Collider>();
            if (boxCollider != null)
            {
                boxCollider.enabled = true;
            }
        }

        UpdateTarget();
    }
}
