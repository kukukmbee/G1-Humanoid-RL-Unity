using UnityEngine;
using System.Collections.Generic;

public class DeliveryManager : MonoBehaviour
{
    public enum DeliveryState
    {
        WalkingToCube,
        CarryingBack,
        Finished
    }

    [Header("State")]
    public DeliveryState currentState = DeliveryState.WalkingToCube;

    [Header("Agent Settings")]
    [Tooltip("Ссылка на скрипт агента G1Agent. Должна быть привязана локально к этому роботу.")]
    public G1Agent agent;

    [Header("Cube Objects")]
    [Tooltip("Свободно висящий куб на сцене (цель, которую нужно забрать).")]
    public GameObject standaloneCube;
    
    [Tooltip("Визуальный куб в руках робота (прилинкован к его груди, изначально выключен).")]
    public GameObject visualCubeInHands;

    [Tooltip("Куб, который появится на финише при успешной доставке (необязательно).")]
    public GameObject deliveredCubeAtHome;

    [Header("Path To Cube (Путь туда)")]
    [Tooltip("Список вейпоинтов от старта до куба. Последняя точка в этом списке должна быть самим кубом (или точкой прямо перед ним).")]
    public List<Transform> pathToCube = new List<Transform>();

    [Header("Path Back Home (Путь обратно)")]
    [Tooltip("Список вейпоинтов от куба обратно домой. Последняя точка в этом списке — финальный финиш.")]
    public List<Transform> pathBackHome = new List<Transform>();

    [Header("Visual Path Settings")]
    public LineRenderer lineRenderer;
    public Color pathToCubeColor = Color.cyan;
    public Color pathBackHomeColor = Color.yellow;

    private int currentPathIndex = 0;

    private void Start()
    {
        if (agent == null)
        {
            agent = GetComponentInChildren<G1Agent>();
            if (agent == null && transform.parent != null)
            {
                agent = transform.parent.GetComponentInChildren<G1Agent>();
            }
        }

        // Автоматически связываем этого менеджера с агентом
        if (agent != null)
        {
            if (agent.deliveryManager == null)
            {
                agent.deliveryManager = this;
            }
        }
        else
        {
            Debug.LogError($"[{gameObject.name}] DeliveryManager: не найден локальный G1Agent!");
        }

        ResetDeliveryScenario();
    }

    /// <summary>
    /// Сброс всего сценария доставки в начальное состояние (или случайный старт с коробкой для обучения)
    /// </summary>
    public void ResetDeliveryScenario()
    {
        // Выключаем спавн на дальней точке, чтобы он шел весь путь целиком
        bool startWithCube = false;

        if (startWithCube && pathBackHome.Count > 0 && pathToCube.Count > 0)
        {
            currentState = DeliveryState.CarryingBack;
            currentPathIndex = 0;

            if (standaloneCube != null) standaloneCube.SetActive(false);
            if (visualCubeInHands != null) visualCubeInHands.SetActive(false); // Куб убрали
            if (deliveredCubeAtHome != null) deliveredCubeAtHome.SetActive(false);

            if (agent != null)
            {
                agent.demoMode = true;
                agent.lockArmsForDelivery = false; // Руки больше не блокируем
                agent.target = pathBackHome[0];
                Debug.Log($"[{gameObject.name}] CURRICULUM SPAWN: Спавн на обратном пути! Цель: {pathBackHome[0].name}");
            }
        }
        else
        {
            currentState = DeliveryState.WalkingToCube;
            currentPathIndex = 0;

            // Настройка кубов для обычного старта
            if (standaloneCube != null) standaloneCube.SetActive(true);
            if (visualCubeInHands != null) visualCubeInHands.SetActive(false);
            if (deliveredCubeAtHome != null) deliveredCubeAtHome.SetActive(false);

            // Настройка агента
            if (agent != null)
            {
                agent.demoMode = true;
                agent.lockArmsForDelivery = false; // Руки расслаблены на пути туда

                if (pathToCube.Count > 0)
                {
                    agent.target = pathToCube[0];
                    Debug.Log($"[{gameObject.name}] ОБЫЧНЫЙ СПАВН: Сценарий запущен. Цель: {pathToCube[0].name}");
                }
            }
        }

        UpdateLineRenderer();
    }

    /// <summary>
    /// Вызывается агентом G1Agent при достижении текущей цели
    /// </summary>
    public void OnWaypointReached(G1Agent reachedAgent)
    {
        if (currentState == DeliveryState.WalkingToCube)
        {
            Debug.Log($"[{gameObject.name}] {reachedAgent.name} достиг точки 'Туда': {pathToCube[currentPathIndex].name}");
            
            // Если пришли к последней точке пути «Туда» (к кубу)
            if (currentPathIndex >= pathToCube.Count - 1)
            {
                currentState = DeliveryState.Finished;

                // Физический сброс куба
                if (standaloneCube != null) standaloneCube.SetActive(false);

                // Финальный бонус за успешный проход лабиринта
                float finalReward = 15.0f + (pathToCube.Count * 3.0f);
                reachedAgent.AddReward(finalReward); 

                Debug.Log($"[{gameObject.name}] Лабиринт успешно пройден! Награда: {finalReward}. Завершение эпизода.");
                
                // Перезапускаем сцену для следующего демонстрационного раунда
                ResetDeliveryScenarioAndAgent();
            }
            else
            {
                // Прогрессивная нарастающая награда за промежуточную точку пути «Туда»
                float progressiveReward = 1.0f + (currentPathIndex * 2.0f);
                reachedAgent.AddReward(progressiveReward); 
                
                currentPathIndex++;
                reachedAgent.target = pathToCube[currentPathIndex];
                Debug.Log($"[{gameObject.name}] Точка {pathToCube[currentPathIndex - 1].name} пройдена! Получена нарастающая награда: {progressiveReward}. Идем к следующей: {pathToCube[currentPathIndex].name}");
            }
        }
        else if (currentState == DeliveryState.CarryingBack)
        {
            Debug.Log($"[{gameObject.name}] {reachedAgent.name} достиг точки 'Обратно': {pathBackHome[currentPathIndex].name}");

            // Если вернулись на базу (последняя точка пути «Обратно»)
            if (currentPathIndex >= pathBackHome.Count - 1)
            {
                currentState = DeliveryState.Finished;

                // Визуальный сброс куба на финише
                if (visualCubeInHands != null) visualCubeInHands.SetActive(false);
                if (deliveredCubeAtHome != null) deliveredCubeAtHome.SetActive(true);

                reachedAgent.lockArmsForDelivery = false; // Расслабляем руки
                
                // Финальный грандиозный бонус за успешное завершение доставки!
                float finalReward = 15.0f + (pathBackHome.Count * 3.0f);
                reachedAgent.AddReward(finalReward); 
                
                Debug.Log($"[{gameObject.name}] Успешная доставка куба домой! Финальная награда: {finalReward}. Завершение эпизода.");
                
                // Перезапуск сценария через 3 секунды
                Invoke(nameof(ResetDeliveryScenarioAndAgent), 3f);
            }
            else
            {
                // Идем к следующей точке пути «Обратно»
                currentPathIndex++;
                reachedAgent.target = pathBackHome[currentPathIndex];
                reachedAgent.AddReward(2.0f);
                Debug.Log($"[{gameObject.name}] Идем к следующей точке возврата: {pathBackHome[currentPathIndex].name}");
            }
        }

        UpdateLineRenderer();
    }

    public Transform GetCurrentWaypoint()
    {
        List<Transform> activePath = (currentState == DeliveryState.WalkingToCube) ? pathToCube : pathBackHome;
        if (activePath.Count > 0 && currentPathIndex < activePath.Count)
        {
            return activePath[currentPathIndex];
        }
        return null;
    }

    public bool IsCurrentWaypointFinal()
    {
        List<Transform> activePath = (currentState == DeliveryState.WalkingToCube) ? pathToCube : pathBackHome;
        if (activePath.Count > 0)
        {
            return currentPathIndex == activePath.Count - 1;
        }
        return false;
    }

    private void ResetDeliveryScenarioAndAgent()
    {
        ResetDeliveryScenario();
        if (agent != null)
        {
            agent.EndEpisode(); // Ресет агента на стартовую позицию
        }
    }

    private void UpdateLineRenderer()
    {
        if (lineRenderer == null) return;

        List<Transform> activePath = (currentState == DeliveryState.WalkingToCube) ? pathToCube : pathBackHome;
        lineRenderer.startColor = (currentState == DeliveryState.WalkingToCube) ? pathToCubeColor : pathBackHomeColor;
        lineRenderer.endColor = lineRenderer.startColor;

        if (activePath.Count > 0)
        {
            lineRenderer.positionCount = activePath.Count;
            for (int i = 0; i < activePath.Count; i++)
            {
                if (activePath[i] != null)
                {
                    lineRenderer.SetPosition(i, activePath[i].position);
                }
            }
        }
    }

    private void OnDrawGizmos()
    {
        // Отрисовка пути «Туда» синим цветом
        if (pathToCube.Count > 0)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < pathToCube.Count; i++)
            {
                if (pathToCube[i] != null)
                {
                    Gizmos.DrawWireSphere(pathToCube[i].position, 0.4f);
                    if (i < pathToCube.Count - 1 && pathToCube[i+1] != null)
                    {
                        Gizmos.DrawLine(pathToCube[i].position, pathToCube[i+1].position);
                    }
                }
            }
        }

        // Отрисовка пути «Обратно» желтым цветом
        if (pathBackHome.Count > 0)
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < pathBackHome.Count; i++)
            {
                if (pathBackHome[i] != null)
                {
                    Gizmos.DrawWireSphere(pathBackHome[i].position, 0.4f);
                    if (i < pathBackHome.Count - 1 && pathBackHome[i+1] != null)
                    {
                        Gizmos.DrawLine(pathBackHome[i].position, pathBackHome[i+1].position);
                    }
                }
            }
        }
    }
}
