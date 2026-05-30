using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Collections.Generic;

public class G1Agent : Agent
{
    [Header("Robot Parts")]
    public List<ArticulationBody> joints = new List<ArticulationBody>();
    public Transform target; // Цель (Точка B)

    private ArticulationBody rootBody; // Корневое физическое тело (таз/pelvis)
    [Header("Posture Control")]
    [Tooltip("Трансформ головы для контроля осанки прямохождения. Если оставить пустым, выполнится автоматический поиск по имени.")]
    public Transform headTransform; // Трансформ головы для контроля осанки прямохождения
    private int episodeSteps = 0;

    // Переменные для запоминания стартовой позы и дефолтных таргетов суставов
    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;
    private Vector3 initialPelvisPosition;
    private Quaternion initialPelvisRotation;
    private List<float> initialJointTargetsDegrees = new List<float>(); // Дефолтные углы суставов в градусах
    private List<float> initialJointPositions = new List<float>();       // Дефолтные углы суставов в радианах (для жесткого сброса физики)
    private float fallThresholdHeight; // Динамический порог высоты падения

    public override void Initialize()
    {
        // Очищаем списки для предотвращения дублирования суставов при перезапусках Play Mode в редакторе Unity
        joints.Clear();
        initialJointTargetsDegrees.Clear();
        initialJointPositions.Clear();

        // Проверяем наличие DecisionRequester, без него OnActionReceived не будет вызываться
        DecisionRequester dr = GetComponent<DecisionRequester>();
        if (dr == null)
        {
            dr = gameObject.AddComponent<DecisionRequester>();
            dr.DecisionPeriod = 5; // Период принятия решений по умолчанию
            Debug.LogWarning("[G1Agent] ВНИМАНИЕ: Компонент DecisionRequester не найден! Добавил его автоматически с периодом 5.");
        }
        else
        {
            Debug.Log($"[G1Agent] Найден существующий DecisionRequester с периодом {dr.DecisionPeriod}.");
        }

        // Запоминаем исходное локальное положение самого контейнера робота
        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;

        // Автоматически находим все суставы робота при запуске
        ArticulationBody[] allBodies = GetComponentsInChildren<ArticulationBody>();
        
        // Ищем корневое тело напрямую в массиве по флагу isRoot
        foreach (var body in allBodies)
        {
            if (body.isRoot)
            {
                rootBody = body;
                break;
            }
        }

        // Если по какой-то причине флаг isRoot не сработал, берем первый попавшийся ArticulationBody в качестве корня
        if (rootBody == null && allBodies.Length > 0)
        {
            rootBody = allBodies[0];
            Debug.LogWarning($"[G1Agent] Предупреждение: Не найден ArticulationBody с флагом isRoot, выбран первый доступный: {rootBody.name}");
        }

        if (rootBody != null)
        {
            Debug.Log($"[G1Agent] Успешно найден корневой физический сустав (таз): {rootBody.name}");
            
            // Запоминаем идеальную стартовую позицию и ротацию таза, настроенную в редакторе Unity
            initialPelvisPosition = rootBody.transform.position;
            initialPelvisRotation = rootBody.transform.rotation;
            
            // Динамически рассчитываем порог падения (60% от исходной высоты таза)
            fallThresholdHeight = initialPelvisPosition.y * 0.6f;
            Debug.Log($"[G1Agent] Стартовая высота таза: {initialPelvisPosition.y:F3}м. Порог падения установлен на Y = {fallThresholdHeight:F3}м.");
        }
        else
        {
            Debug.LogError("[G1Agent] КРИТИЧЕСКАЯ ОШИБКА: На роботе вообще не найдено ни одного компонента ArticulationBody!");
        }

        // Автоматически находим трансформ головы для отслеживания осанки, если он не задан вручную
        if (headTransform == null)
        {
            Transform[] allTransforms = GetComponentsInChildren<Transform>();
            foreach (var t in allTransforms)
            {
                string lowerName = t.name.ToLower();
                if (lowerName.Contains("head") || lowerName.Contains("neck"))
                {
                    headTransform = t;
                    Debug.Log($"[G1Agent] Успешно найден трансформ головы для контроля осанки (автопоиск): {headTransform.name}");
                    break;
                }
            }
        }
        else
        {
            Debug.Log($"[G1Agent] Используется заданный вручную трансформ головы: {headTransform.name}");
        }

        // Заполняем список управляемых суставов и настраиваем их физику программно
        foreach (var body in allBodies)
        {
            // ПРОГРАММНАЯ НАСТРОЙКА ТОЧНОСТИ
            body.solverIterations = 30;
            body.solverVelocityIterations = 12;

            // АВТОМАТИЧЕСКАЯ НАСТРОЙКА CCD ДЛЯ СТУПНЕЙ И НОСКОВ
            string lowerName = body.name.ToLower();
            if (lowerName.Contains("ankle_roll") || lowerName.Contains("foot") || lowerName.Contains("toe"))
            {
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                Debug.Log($"[G1Agent] Авто-настройка: Включен Continuous Speculative для сустава: {body.name}");
            }

            // Пропускаем главный корень
            if (body == rootBody || body.isRoot) continue;
            
            // Если это подвижный сустав, добавляем в список управления
            if (body.jointType != ArticulationJointType.FixedJoint)
            {
                joints.Add(body);
                
                // ПРОГРАММНАЯ НАСТРОЙКА ЕСТЕСТВЕННОЙ ПОЗЫ РУК (OVERRIDE):
                float defaultTarget = body.xDrive.target;

                if (lowerName.Contains("elbow"))
                {
                    defaultTarget = 15f; // Естественное легкое сгибание локтей (15 градусов)
                }
                else if (lowerName.Contains("shoulder_pitch"))
                {
                    defaultTarget = 0f; // Руки висят строго вертикально (0 градусов)
                }
                else if (lowerName.Contains("shoulder_roll"))
                {
                    defaultTarget = 0f; // Руки плотно прижаты к бокам (0 градусов)
                }
                else if (lowerName.Contains("shoulder_yaw"))
                {
                    defaultTarget = 0f; // Направление предплечья (0 градусов)
                }
                else if (lowerName.Contains("knee"))
                {
                    defaultTarget = 25f; // Слегка сгибаем колени на 25 градусов по умолчанию для пружинящей позы
                }
                
                initialJointTargetsDegrees.Add(defaultTarget);
                
                // Запоминаем исходный физический угол сустава в радианах
                if (lowerName.Contains("elbow") || lowerName.Contains("shoulder") || lowerName.Contains("knee"))
                {
                    initialJointPositions.Add(defaultTarget * Mathf.Deg2Rad);
                }
                else if (body.dofCount > 0)
                {
                    initialJointPositions.Add(body.jointPosition[0]);
                }
                else
                {
                    initialJointPositions.Add(0f);
                }
                
                // Настраиваем жесткость сустава (PD контроллер)
                var drive = body.xDrive;
                drive.stiffness = 200f; // Умеренная жесткость
                drive.damping = 20f;    // Демпфирование
                body.xDrive = drive;
            }
        }
        Debug.Log($"[G1Agent] Инициализация завершена. Найдено {joints.Count} подвижных суставов.");
    }

    public override void OnEpisodeBegin()
    {
        episodeSteps = 0;

        // Восстанавливаем исходное локальное положение контейнера
        transform.localPosition = initialLocalPosition;
        transform.localRotation = initialLocalRotation;
        
        if (rootBody != null)
        {
            // Обнуляем все остаточные скорости перед телепортацией
            rootBody.linearVelocity = Vector3.zero;
            rootBody.angularVelocity = Vector3.zero;

            // БЕЗОПАСНЫЙ СПАВН: Приподнимаем робота на 3 сантиметра (0.03м) вверх от исходной точки
            Vector3 safeSpawnPos = initialPelvisPosition + new Vector3(0f, 0.03f, 0f);
            
            // СЛУЧАЙНЫЙ ПОВОРОТ ПРИ СПАВНЕ: рандомизация направления (yaw),
            // чтобы агент не запоминал конкретный маршрут, а учил универсальную стратегию ходьбы
            float randomYaw = Random.Range(-180f, 180f);
            Quaternion randomRotation = initialPelvisRotation * Quaternion.Euler(0f, randomYaw, 0f);
            rootBody.TeleportRoot(safeSpawnPos, randomRotation);
            
            // Повторно обнуляем скорости после телепортации
            rootBody.linearVelocity = Vector3.zero;
            rootBody.angularVelocity = Vector3.zero;
        }

        // Сбрасываем суставы к их исходным позам
        for (int i = 0; i < joints.Count; i++)
        {
            var joint = joints[i];
            
            // МГНОВЕННЫЙ СБРОС ФИЗИЧЕСКОГО УГЛА
            if (joint.dofCount > 0 && i < initialJointPositions.Count)
            {
                joint.jointPosition = new ArticulationReducedSpace(initialJointPositions[i]);
            }
            
            joint.jointVelocity = new ArticulationReducedSpace(0f);
            
            var drive = joint.xDrive;
            drive.target = initialJointTargetsDegrees[i];
            drive.targetVelocity = 0f;
            joint.xDrive = drive;
        }

    }

    public override void CollectObservations(VectorSensor sensor)
    {
        int obsSize = 14 + joints.Count * 2; // Динамически вычисляем точный размер наблюдений (72 при 29 суставах)
        if (rootBody == null)
        {
            for (int i = 0; i < obsSize; i++) sensor.AddObservation(0f);
            return;
        }

        // ГЛОБАЛЬНАЯ ЗАЩИТА ОТ NaN
        if (float.IsNaN(rootBody.transform.position.y) || float.IsNaN(rootBody.transform.rotation.x))
        {
            for (int i = 0; i < obsSize; i++) sensor.AddObservation(0f);
            return;
        }

        // 1. Позиция таза по высоте и наклон корпуса (5 наблюдений)
        sensor.AddObservation(rootBody.transform.position.y);
        sensor.AddObservation(rootBody.transform.rotation);

        // 2. Скорость таза: линейная и угловая (6 наблюдений)
        // Позволяет агенту различать «стою» и «падаю/иду» — критический сигнал для баланса
        sensor.AddObservation(rootBody.linearVelocity);
        sensor.AddObservation(rootBody.angularVelocity);

        // 3. Вектор до цели в ЛОКАЛЬНОМ пространстве робота (3 наблюдения)
        // InverseTransformDirection обобщает опыт при любом стартовом повороте
        Vector3 worldToTarget = (target != null) ? (target.position - rootBody.transform.position) : Vector3.zero;
        Vector3 localToTarget = rootBody.transform.InverseTransformDirection(worldToTarget);
        sensor.AddObservation(localToTarget);

        // 4. Углы суставов (58 наблюдений для 29 суставов)
        foreach (var joint in joints)
        {
            if (joint.dofCount > 0)
            {
                sensor.AddObservation(joint.jointPosition[0]);
                sensor.AddObservation(joint.jointVelocity[0]);
            }
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        episodeSteps++;

        var continuousActions = actions.ContinuousActions;
        int actionIndex = 0;

        foreach (var joint in joints)
        {
            if (actionIndex < continuousActions.Length && joint.dofCount > 0)
            {
                var drive = joint.xDrive;
                float actionVal = Mathf.Clamp(continuousActions[actionIndex], -1f, 1f);
                
                // УПРАВЛЕНИЕ ЧЕРЕЗ СМЕЩЕНИЕ
                float defaultAngle = initialJointTargetsDegrees[actionIndex];
                float maxDeviation = 65f; 
                
                float targetAngle = defaultAngle + actionVal * maxDeviation;
                targetAngle = Mathf.Clamp(targetAngle, drive.lowerLimit, drive.upperLimit);
                
                drive.target = targetAngle;
                joint.xDrive = drive;
                
                actionIndex++;
            }
        }

        if (rootBody == null) return;

        float currentY = rootBody.transform.position.y;
        float uprightBonus = Vector3.Dot(rootBody.transform.up, Vector3.up);

        // ЗАЩИТА ОТ NaN: если физика взорвалась, немедленно перезапускаем эпизод
        if (float.IsNaN(currentY) || float.IsNaN(uprightBonus))
        {
            Debug.LogWarning("[G1Agent] ВНИМАНИЕ: Физика взорвалась (NaN)! Немедленный безопасный перезапуск.");
            SetReward(-2.0f);
            EndEpisode();
            return;
        }

        // Вычисляем вектор направления на цель и facingDot (Вынесено в правильную область видимости!)
        Vector3 dirToTarget = (target != null) ? (target.position - rootBody.transform.position).normalized : rootBody.transform.forward;
        float facingDot = Vector3.Dot(rootBody.transform.forward, dirToTarget);

        float postureMultiplier = 1.0f; // По умолчанию множитель награды за скорость 100%

        if (episodeSteps > 3)
        {
            // 1. Штраф, если упал
            if (currentY < fallThresholdHeight)
            {
                SetReward(-2.0f);
                EndEpisode();
                return;
            }

            // 2. Удержание равновесия (Критически важный баланс)
            if (uprightBonus < 0.5f) 
            {
                SetReward(-1.0f);
                EndEpisode();
                return;
            }
            // Поднят upright коэффициент с 0.002 до 0.015 для повышения приоритета баланса
            AddReward(uprightBonus * 0.015f); 

            // 2.5 Контроль осанки (ГЛОБАЛЬНЫЙ)
            if (headTransform != null)
            {
                Vector3 pelvisPos = rootBody.transform.position;
                Vector3 headPos = headTransform.position;
                Vector3 pelvisToHead = headPos - pelvisPos;
                
                // Проецируем смещение головы чисто на горизонтальную плоскость по направлению к цели
                Vector3 pelvisToHeadFlat = new Vector3(pelvisToHead.x, 0, pelvisToHead.z);
                Vector3 dirToTargetFlat = new Vector3(dirToTarget.x, 0, dirToTarget.z).normalized;
                
                // Если голова ближе к цели, чем таз - это положительное число (наклон вперед).
                float globalLeanForward = Vector3.Dot(pelvisToHeadFlat, dirToTargetFlat);
                
                // Штраф за отклонение назад (Matrix dodge)
                if (globalLeanForward < -0.02f) 
                {
                    postureMultiplier = 0.1f; // Срезаем 90% награды за скорость!
                    AddReward(-0.02f); // Небольшой прямой штраф
                }
                // Поощряем наклон вперед, как у бегущего человека (от 5 до 20 см)
                // Снижен posture коэффицент с 0.02 до 0.004 для правильной иерархии (баланс важнее осанки)
                else if (globalLeanForward >= 0.05f && globalLeanForward <= 0.2f)
                {
                    AddReward(0.004f); 
                }
            }

            // 2.6 Штраф за движение "приставным шагом" (вынесен за блок headTransform)
            if (facingDot < 0.5f) 
            {
                AddReward(-0.02f); 
            }
        }

        if (target != null)
        {
            // 3. Награда за скорость движения к цели (мгновенная проекция linearVelocity)
            float speedTowardTarget = Vector3.Dot(rootBody.linearVelocity, dirToTarget);
            // Изменен directionMultiplier на Clamp01: робот получает 0% награды за скорость, если идет спиной (facingDot <= 0)
            float directionMultiplier = Mathf.Clamp01(facingDot);

            if (speedTowardTarget > 0)
            {
                // Награда скорректирована с 0.3 до 0.2 для баланса с остальными членами наград
                AddReward(speedTowardTarget * 0.2f * directionMultiplier * postureMultiplier);
            }
            else
            {
                // Мягкий штраф за отдаление — позволяет роботу маневрировать и разворачиваться
                AddReward(speedTowardTarget * 0.1f);
            }

            // 4. Достижение цели
            float currentDistance = Vector3.Distance(rootBody.transform.position, target.position);
            if (currentDistance < 1.0f)
            {
                Debug.Log($"[G1Agent] Эпизод завершен (Успех)! Шагов: {episodeSteps}");
                AddReward(10.0f); // Заменено SetReward на AddReward, чтобы не стирать историю обучения эпизода!
                EndEpisode();
                return;
            }
        }

        // 5. Штраф за дерганье и резкие движения
        float actionPenalty = 0f;
        for (int i = 0; i < continuousActions.Length; i++)
        {
            actionPenalty += Mathf.Abs(continuousActions[i]);
        }
        // Деление переведено на continuousActions.Length, штраф поднят в 10 раз (до 0.01) для реального влияния на плавность
        AddReward(- (actionPenalty / continuousActions.Length) * 0.01f);
    }
}