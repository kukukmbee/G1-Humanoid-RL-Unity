using UnityEngine;
using System.Collections.Generic;

public class CheckpointManager : MonoBehaviour
{
    public static CheckpointManager Instance { get; private set; }

    [Header("Agent Settings")]
    [Tooltip("Ссылка на скрипт агента G1Agent.")]
    public G1Agent agent;

    [Header("Path Settings")]
    [Tooltip("Список точек-чекпоинтов (сфер/вейпоинтов) по порядку.")]
    public List<Transform> checkpoints = new List<Transform>();
    
    [Tooltip("Зациклить ли маршрут по кругу при достижении последней точки.")]
    public bool loopPath = true;

    [Header("Visual Settings")]
    [Tooltip("Использовать LineRenderer для визуализации пути в игре.")]
    public LineRenderer lineRenderer;
    [Tooltip("Цвет линии пути в редакторе.")]
    public Color pathColor = Color.cyan;

    private int currentCheckpointIndex = 0;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        if (agent == null)
        {
            agent = FindFirstObjectByType<G1Agent>();
        }

        if (checkpoints.Count > 0 && agent != null)
        {
            currentCheckpointIndex = 0;
            agent.target = checkpoints[currentCheckpointIndex];
            Debug.Log($"[CheckpointManager] Маршрут инициализирован. Первая цель: {checkpoints[0].name}");
        }
        else
        {
            Debug.LogWarning("[CheckpointManager] Не назначены чекпоинты или ссылка на G1Agent!");
        }

        SetupLineRenderer();
    }

    private void SetupLineRenderer()
    {
        if (lineRenderer == null)
        {
            lineRenderer = GetComponent<LineRenderer>();
        }

        if (lineRenderer != null && checkpoints.Count > 0)
        {
            lineRenderer.positionCount = checkpoints.Count + (loopPath ? 1 : 0);
            lineRenderer.startWidth = 0.15f;
            lineRenderer.endWidth = 0.15f;
            
            // Настройка красивого материала/цвета для линии
            lineRenderer.startColor = pathColor;
            lineRenderer.endColor = pathColor;

            UpdateLinePositions();
        }
    }

    private void UpdateLinePositions()
    {
        if (lineRenderer == null) return;

        for (int i = 0; i < checkpoints.Count; i++)
        {
            if (checkpoints[i] != null)
            {
                lineRenderer.SetPosition(i, checkpoints[i].position);
            }
        }

        if (loopPath && checkpoints.Count > 0 && checkpoints[0] != null)
        {
            lineRenderer.SetPosition(checkpoints.Count, checkpoints[0].position);
        }
    }

    private void Update()
    {
        // Динамическое обновление линии в рантайме (если точки двигаются)
        if (lineRenderer != null && Application.isPlaying)
        {
            UpdateLinePositions();
        }
    }

    /// <summary>
    /// Вызывается агентом G1Agent, когда он приближается к текущей цели.
    /// </summary>
    public void OnCheckpointReached(G1Agent reachedAgent)
    {
        if (checkpoints.Count == 0) return;

        Debug.Log($"[CheckpointManager] Точка {checkpoints[currentCheckpointIndex].name} успешно достигнута!");

        currentCheckpointIndex++;

        if (currentCheckpointIndex >= checkpoints.Count)
        {
            if (loopPath)
            {
                currentCheckpointIndex = 0;
                Debug.Log("[CheckpointManager] Маршрут завершен. Возврат к начальной точке.");
            }
            else
            {
                currentCheckpointIndex = checkpoints.Count - 1;
                Debug.Log("[CheckpointManager] Финальная точка маршрута достигнута! Маршрут завершен.");
                
                // В режиме демо при финише можем дать большую финальную награду и сбросить
                reachedAgent.AddReward(15.0f);
                reachedAgent.EndEpisode();
                return;
            }
        }

        // Перенаправляем агента на новый чекпоинт
        reachedAgent.target = checkpoints[currentCheckpointIndex];
        
        // Даем промежуточную награду за прогресс по маршруту
        reachedAgent.AddReward(3.0f);
        
        Debug.Log($"[CheckpointManager] Новая цель назначена: {checkpoints[currentCheckpointIndex].name}");
    }

    // Отрисовка красивых линий пути прямо в окне Scene в Unity
    private void OnDrawGizmos()
    {
        if (checkpoints.Count < 2) return;

        Gizmos.color = pathColor;
        for (int i = 0; i < checkpoints.Count - 1; i++)
        {
            if (checkpoints[i] != null && checkpoints[i+1] != null)
            {
                Gizmos.DrawLine(checkpoints[i].position, checkpoints[i+1].position);
                Gizmos.DrawWireSphere(checkpoints[i].position, 0.5f);
            }
        }

        if (loopPath && checkpoints[checkpoints.Count - 1] != null && checkpoints[0] != null)
        {
            Gizmos.DrawLine(checkpoints[checkpoints.Count - 1].position, checkpoints[0].position);
            Gizmos.DrawWireSphere(checkpoints[checkpoints.Count - 1].position, 0.5f);
        }
        else if (checkpoints[checkpoints.Count - 1] != null)
        {
            Gizmos.DrawWireSphere(checkpoints[checkpoints.Count - 1].position, 0.5f);
        }
    }
}
