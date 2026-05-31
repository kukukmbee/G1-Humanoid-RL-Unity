using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Collections.Generic;

public class G1Agent : Agent
{
    [Header("Robot Parts")]
    [Tooltip("Список управляемых суставов робота (ArticulationBody). Заполняется автоматически.")]
    public List<ArticulationBody> joints = new List<ArticulationBody>();
    [Tooltip("Целевая точка назначения (сфера B).")]
    public Transform target; 

    private ArticulationBody rootBody; // Корневое физическое тело (таз)
    
    [Header("Posture Control")]
    [Tooltip("Трансформ головы для контроля осанки прямохождения. Если пустой, выполнится автопоиск по имени.")]
    public Transform headTransform;
    
    [Header("Foot Ground Sensors")]
    [Tooltip("Датчик касания левой стопы.")]
    public FootSensor leftFoot;
    [Tooltip("Датчик касания правой стопы.")]
    public FootSensor rightFoot;

    private int episodeSteps = 0;
    private int obsSize; // Кэшированный размер вектора наблюдений (74)

    // Исходные трансформации контейнера и физического таза робота
    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;
    private Vector3 initialPelvisPosition;
    private Quaternion initialPelvisRotation;
    
    // Исходные угловые параметры суставов для точного сброса
    private List<float> initialJointTargetsDegrees = new List<float>(); // Углы приводов xDrive (градусы)
    private List<float> initialJointPositions = new List<float>();       // Физические углы звеньев (радианы)
    private float fallThresholdHeight; // Динамический порог высоты падения (60% от стартовой)

    public override void Initialize()
    {
        // Очистка списков для предотвращения дублирования данных при перезапусках Play Mode в редакторе
        joints.Clear();
        initialJointTargetsDegrees.Clear();
        initialJointPositions.Clear();

        // Автоматическое добавление DecisionRequester при его отсутствии
        DecisionRequester dr = GetComponent<DecisionRequester>();
        if (dr == null)
        {
            dr = gameObject.AddComponent<DecisionRequester>();
            dr.DecisionPeriod = 5;
        }

        // Запоминание стартового положения родительского контейнера
        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;

        // Поиск корневого тела робота (ArticulationBody с флагом isRoot)
        ArticulationBody[] allBodies = GetComponentsInChildren<ArticulationBody>();
        foreach (var body in allBodies)
        {
            if (body.isRoot)
            {
                rootBody = body;
                break;
            }
        }

        if (rootBody == null && allBodies.Length > 0)
        {
            rootBody = allBodies[0];
        }

        if (rootBody != null)
        {
            initialPelvisPosition = rootBody.transform.position;
            initialPelvisRotation = rootBody.transform.rotation;
            
            // Динамический расчет критической высоты падения (60% от исходной высоты таза)
            fallThresholdHeight = initialPelvisPosition.y * 0.6f;
        }

        // Автоматический поиск головы для контроля осанки
        if (headTransform == null)
        {
            Transform[] allTransforms = GetComponentsInChildren<Transform>();
            foreach (var t in allTransforms)
            {
                string lowerName = t.name.ToLower();
                if (lowerName.Contains("head") || lowerName.Contains("neck"))
                {
                    headTransform = t;
                    break;
                }
            }
        }

        // Настройка физических параметров звеньев и сбор управляемых суставов
        foreach (var body in allBodies)
        {
            // Повышение стабильности физического симулятора (итерации расчетов сочленений)
            body.solverIterations = 30;
            body.solverVelocityIterations = 12;

            string lowerName = body.name.ToLower();
            // Включение Continuous Speculative для стоп для предотвращения проваливания сквозь пол
            if (lowerName.Contains("ankle_roll") || lowerName.Contains("foot") || lowerName.Contains("toe"))
            {
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }

            if (body == rootBody || body.isRoot) continue;
            
            // Сбор подвижных суставов (исключая фиксированные сочленения)
            if (body.jointType != ArticulationJointType.FixedJoint)
            {
                joints.Add(body);
                
                float defaultTarget = body.xDrive.target;
                
                // Программный override дефолтных углов для формирования пружинящей стартовой позы
                if (lowerName.Contains("elbow"))
                {
                    defaultTarget = 15f; // Естественное легкое сгибание локтей
                }
                else if (lowerName.Contains("shoulder_pitch"))
                {
                    defaultTarget = 0f; // Руки опущены вниз
                }
                else if (lowerName.Contains("shoulder_roll"))
                {
                    defaultTarget = 0f; // Руки прижаты к бокам
                }
                else if (lowerName.Contains("shoulder_yaw"))
                {
                    defaultTarget = 0f; 
                }
                else if (lowerName.Contains("knee"))
                {
                    defaultTarget = 25f; // Подсогнутые колени для амортизации шага
                }
                
                initialJointTargetsDegrees.Add(defaultTarget);
                // Сохранение углов в радианах для жесткого сброса физического состояния
                initialJointPositions.Add(defaultTarget * Mathf.Deg2Rad);
                
                // Настройка параметров PD-контроллера приводов суставов
                var drive = body.xDrive;
                drive.stiffness = 200f; 
                drive.damping = 20f;    
                body.xDrive = drive;
            }
        }

        // Кэширование размера наблюдений для исключения лишних калькуляций во время шагов
        obsSize = 16 + joints.Count * 2;
    }

    public override void OnEpisodeBegin()
    {
        episodeSteps = 0;

        // СЛУЧАЙНЫЙ ПОВОРОТ ПРИ СПАВНЕ: генерируем yaw строго в задней полусфере,
        // чтобы заставить робота тренировать развороты из положения "спиной к цели"
        float randomYaw = Random.value > 0.5f ? Random.Range(90f, 180f) : Random.Range(-180f, -90f);
        transform.localPosition = initialLocalPosition;
        transform.localRotation = initialLocalRotation * Quaternion.Euler(0f, randomYaw, 0f);
        
        if (rootBody != null)
        {
            // Обнуление физических скоростей перед телепортацией
            rootBody.linearVelocity = Vector3.zero;
            rootBody.angularVelocity = Vector3.zero;

            // Сброс положения таза с безопасным зазором по высоте (+3 см)
            Vector3 safeSpawnPos = initialPelvisPosition + new Vector3(0f, 0.03f, 0f);
            Quaternion randomRotation = initialPelvisRotation * Quaternion.Euler(0f, randomYaw, 0f);
            rootBody.TeleportRoot(safeSpawnPos, randomRotation);
            
            // Вторичное обнуление скоростей для предотвращения физических артефактов
            rootBody.linearVelocity = Vector3.zero;
            rootBody.angularVelocity = Vector3.zero;
        }

        // Сброс всех суставов к исходной позе с обнулением их угловых скоростей
        for (int i = 0; i < joints.Count; i++)
        {
            var joint = joints[i];
            if (joint.dofCount > 0 && i < initialJointPositions.Count)
            {
                // Принудительная установка физического угла звена в радианах
                joint.jointPosition = new ArticulationReducedSpace(initialJointPositions[i]);
            }
            joint.jointVelocity = new ArticulationReducedSpace(0f);
            
            // Сброс целевых значений PD-контроллера xDrive
            var drive = joint.xDrive;
            drive.target = initialJointTargetsDegrees[i];
            drive.targetVelocity = 0f;
            joint.xDrive = drive;
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Заглушка при критическом отсутствии корневого тела
        if (rootBody == null)
        {
            for (int i = 0; i < obsSize; i++) sensor.AddObservation(0f);
            return;
        }

        // Безопасный перезапуск при возникновении NaN в координатах или ротации
        if (float.IsNaN(rootBody.transform.position.y) || float.IsNaN(rootBody.transform.rotation.x))
        {
            for (int i = 0; i < obsSize; i++) sensor.AddObservation(0f);
            return;
        }

        // 1. Координата таза по высоте и глобальный кватернион вращения (5 наблюдений)
        sensor.AddObservation(rootBody.transform.position.y);
        sensor.AddObservation(rootBody.transform.rotation);

        // 2. Линейная и угловая скорости таза (6 наблюдений)
        sensor.AddObservation(rootBody.linearVelocity);
        sensor.AddObservation(rootBody.angularVelocity);

        // 3. Направление до цели, спроецированное в локальную систему координат робота (3 наблюдения)
        Vector3 worldToTarget = (target != null) ? (target.position - rootBody.transform.position) : Vector3.zero;
        Vector3 localToTarget = rootBody.transform.InverseTransformDirection(worldToTarget);
        sensor.AddObservation(localToTarget);

        // 4. Текущий угол и угловая скорость каждого управляемого сустава (58 наблюдений при 29 суставах)
        foreach (var joint in joints)
        {
            if (joint.dofCount > 0)
            {
                sensor.AddObservation(joint.jointPosition[0]);
                sensor.AddObservation(joint.jointVelocity[0]);
            }
        }

        // 5. Бинарные сигналы контакта левой и правой стопы с землей (2 наблюдения)
        sensor.AddObservation(leftFoot != null && leftFoot.isGrounded ? 1f : 0f);
        sensor.AddObservation(rightFoot != null && rightFoot.isGrounded ? 1f : 0f);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        episodeSteps++;

        var continuousActions = actions.ContinuousActions;
        int actionIndex = 0;

        // Преобразование нормализованных действий сети (-1..1) в целевые углы суставов
        foreach (var joint in joints)
        {
            if (actionIndex < continuousActions.Length && joint.dofCount > 0)
            {
                var drive = joint.xDrive;
                float actionVal = Mathf.Clamp(continuousActions[actionIndex], -1f, 1f);
                
                // Расчет целевого угла как смещения относительно дефолтной стартовой позы
                float defaultAngle = initialJointTargetsDegrees[actionIndex];
                float maxDeviation = 65f; // Максимальная амплитуда движения сустава
                
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

        // Мгновенное завершение эпизода при критическом сбое физической симуляции (NaN)
        if (float.IsNaN(currentY) || float.IsNaN(uprightBonus))
        {
            SetReward(-2.0f);
            EndEpisode();
            return;
        }

        // Расчет вектора направления на цель в горизонтальной плоскости
        Vector3 dirToTarget = (target != null) ? (target.position - rootBody.transform.position).normalized : rootBody.transform.forward;
        float facingDot = Vector3.Dot(rootBody.transform.forward, dirToTarget);
        float postureMultiplier = 1.0f; // Коэффициент штрафа за плохую осанку

        if (episodeSteps > 3)
        {
            // 1. Штраф при падении ниже критической высоты
            if (currentY < fallThresholdHeight)
            {
                SetReward(-2.0f);
                EndEpisode();
                return;
            }

            // 2. Удержание баланса (жесткое наказание за критический завал корпуса)
            if (uprightBonus < 0.5f) 
            {
                SetReward(-1.0f);
                EndEpisode();
                return;
            }
            // Поощрение стабильного вертикального положения таза
            AddReward(uprightBonus * 0.015f); 

            // 3. Контроль глобального наклона корпуса вперед по направлению к цели
            if (headTransform != null)
            {
                Vector3 pelvisPos = rootBody.transform.position;
                Vector3 headPos = headTransform.position;
                Vector3 pelvisToHead = headPos - pelvisPos;
                
                // Проекция вектора осанки на горизонтальную плоскость движения
                Vector3 pelvisToHeadFlat = new Vector3(pelvisToHead.x, 0, pelvisToHead.z);
                Vector3 dirToTargetFlat = new Vector3(dirToTarget.x, 0, dirToTarget.z).normalized;
                
                float globalLeanForward = Vector3.Dot(pelvisToHeadFlat, dirToTargetFlat);
                
                if (globalLeanForward < -0.02f) 
                {
                    // Жесткое снижение награды за скорость при отклонении спины назад
                    postureMultiplier = 0.1f; 
                    AddReward(-0.02f); 
                }
                else if (globalLeanForward >= 0.05f && globalLeanForward <= 0.2f)
                {
                    // Поощрение естественного наклона корпуса вперед при ходьбе (5-20 см)
                    AddReward(0.004f); 
                }
            }

            // 4. Штраф за некорректное направление движения (приставной шаг, боковое скольжение)
            if (facingDot < 0.5f) 
            {
                AddReward(-0.02f); 
            }
        }

        if (target != null)
        {
            // 5. Награда за мгновенную проекцию линейной скорости на вектор к цели
            float speedTowardTarget = Vector3.Dot(rootBody.linearVelocity, dirToTarget);
            // Исключение поощрения за движение задом наперед (facingDot <= 0)
            float directionMultiplier = Mathf.Clamp01(facingDot);

            if (speedTowardTarget > 0)
            {
                AddReward(speedTowardTarget * 0.2f * directionMultiplier * postureMultiplier);
            }
            else
            {
                // Мягкий штраф при отдалении от цели для стимуляции разворота на месте
                AddReward(speedTowardTarget * 0.1f);
            }

            // 6. Успешное достижение целевой зоны (сфера радиусом 1 метр)
            float currentDistance = Vector3.Distance(rootBody.transform.position, target.position);
            if (currentDistance < 1.0f)
            {
                Debug.Log($"[G1Agent] Эпизод завершен (Успех)! Шагов: {episodeSteps}");
                // Использование AddReward для сохранения накопленного качественного сигнала
                AddReward(10.0f); 
                EndEpisode();
                return;
            }
        }

        // 7. Штраф за резкие рывки и нерациональный расход энергии (минимизация микровибраций суставов)
        float actionPenalty = 0f;
        for (int i = 0; i < continuousActions.Length; i++)
        {
            actionPenalty += Mathf.Abs(continuousActions[i]);
        }
        AddReward(- (actionPenalty / continuousActions.Length) * 0.025f);

        // Наказание за избыточную угловую скорость таза (устраняет резкий занос при разворотах)
        float yawSpeed = Mathf.Abs(rootBody.angularVelocity.y);
        AddReward(-yawSpeed * 0.003f);
    }
}