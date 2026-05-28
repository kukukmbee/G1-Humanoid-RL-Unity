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
    private Transform headTransform; // Трансформ головы для контроля осанки прямохождения
    private float previousDistanceToTarget;
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

        // Автоматически находим трансформ головы для отслеживания осанки
        Transform[] allTransforms = GetComponentsInChildren<Transform>();
        foreach (var t in allTransforms)
        {
            string lowerName = t.name.ToLower();
            if (lowerName.Contains("head") || lowerName.Contains("neck"))
            {
                headTransform = t;
                Debug.Log($"[G1Agent] Успешно найден трансформ головы для контроля осанки: {headTransform.name}");
                break;
            }
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
                // Если мы хотим, чтобы руки висели красиво и естественно вдоль тела, мы переопределяем их дефолтные углы.
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
                // (Для переопределенных суставов переводим новые градусы в радианы, чтобы физика сбросилась без рывка)
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

            // БЕЗОПАСНЫЙ СПАВН: Приподнимаем робота на 3 сантиметра (0.03м) вверх от исходной точки,
            // чтобы ступни ног гарантированно не врезались под пол в момент телепортации!
            Vector3 safeSpawnPos = initialPelvisPosition + new Vector3(0f, 0.03f, 0f);
            rootBody.TeleportRoot(safeSpawnPos, initialPelvisRotation);
            
            // Повторно обнуляем скорости после телепортации
            rootBody.linearVelocity = Vector3.zero;
            rootBody.angularVelocity = Vector3.zero;
        }

        // Сбрасываем суставы к их исходным позам
        for (int i = 0; i < joints.Count; i++)
        {
            var joint = joints[i];
            
            // МГНОВЕННЫЙ СБРОС ФИЗИЧЕСКОГО УГЛА (в радианах):
            // Это мгновенно выпрямляет ноги и ставит руки в красивую позу в воздухе перед первым кадром физики
            if (joint.dofCount > 0 && i < initialJointPositions.Count)
            {
                joint.jointPosition = new ArticulationReducedSpace(initialJointPositions[i]);
            }
            
            joint.jointVelocity = new ArticulationReducedSpace(0f);
            
            var drive = joint.xDrive;
            // Сбрасываем к исходному таргету в градусах
            drive.target = initialJointTargetsDegrees[i];
            drive.targetVelocity = 0f;
            joint.xDrive = drive;
        }

        // Запоминаем начальную дистанцию до цели по реальному телу робота
        if (target != null && rootBody != null)
        {
            previousDistanceToTarget = Vector3.Distance(rootBody.transform.position, target.position);
        }

        Debug.Log("[G1Agent] Эпизод начат! Физические углы суставов мгновенно сброшены. Удар моторов устранен.");
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (rootBody == null)
        {
            // Резервный сбор пустых наблюдений, чтобы не ломать размерность
            for (int i = 0; i < 66; i++) sensor.AddObservation(0f);
            return;
        }

        // ЗАЩИТА ОТ NaN: если физика взорвалась, подставляем безопасные дефолтные значения
        float posY = float.IsNaN(rootBody.transform.position.y) ? initialPelvisPosition.y : rootBody.transform.position.y;
        Quaternion rot = float.IsNaN(rootBody.transform.rotation.x) ? initialPelvisRotation : rootBody.transform.rotation;
        
        Vector3 toTarget = Vector3.zero;
        if (target != null)
        {
            Vector3 rawToTarget = target.position - rootBody.transform.position;
            toTarget = float.IsNaN(rawToTarget.x) ? Vector3.zero : rawToTarget;
        }

        // 1. Реальная позиция таза по высоте и наклон корпуса (5 наблюдений)
        sensor.AddObservation(posY);
        sensor.AddObservation(rot);

        // 2. Вектор до цели от реального тела робота (3 наблюдения)
        sensor.AddObservation(toTarget);

        // 3. Углы суставов (58 наблюдений для 29 суставов)
        foreach (var joint in joints)
        {
            if (joint.dofCount > 0)
            {
                float jointPos = float.IsNaN(joint.jointPosition[0]) ? 0f : joint.jointPosition[0];
                float jointVel = float.IsNaN(joint.jointVelocity[0]) ? 0f : joint.jointVelocity[0];
                sensor.AddObservation(jointPos);
                sensor.AddObservation(jointVel);
            }
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        episodeSteps++;
        
        if (episodeSteps == 1)
        {
            Debug.Log("[G1Agent] Первое действие получено от нейросети в этом эпизоде!");
        }

        var continuousActions = actions.ContinuousActions;
        int actionIndex = 0;

        foreach (var joint in joints)
        {
            if (actionIndex < continuousActions.Length && joint.dofCount > 0)
            {
                var drive = joint.xDrive;
                float actionVal = Mathf.Clamp(continuousActions[actionIndex], -1f, 1f);
                
                // УПРАВЛЕНИЕ ЧЕРЕЗ СМЕЩЕНИЕ (Offset-based control):
                // Действие нейросети (-1..1) управляет отклонением от исходной позы стояния на +-65 градусов,
                // что гарантирует стабильный старт без мгновенного складывания коленей.
                float defaultAngle = initialJointTargetsDegrees[actionIndex];
                float maxDeviation = 65f; 
                
                float targetAngle = defaultAngle + actionVal * maxDeviation;
                // Не выходим за физические лимиты сустава
                targetAngle = Mathf.Clamp(targetAngle, drive.lowerLimit, drive.upperLimit);
                
                drive.target = targetAngle;
                joint.xDrive = drive;
                
                actionIndex++;
            }
        }

        if (rootBody == null) return;

        // Вычисляем показатели для проверки падения по РЕАЛЬНОМУ физическому телу робота
        float currentY = rootBody.transform.position.y;
        float uprightBonus = Vector3.Dot(rootBody.transform.up, Vector3.up);

        // ЗАЩИТА ОТ NaN: если физика взорвалась (NaN), немедленно перезапускаем эпизод
        if (float.IsNaN(currentY) || float.IsNaN(uprightBonus))
        {
            Debug.LogWarning("[G1Agent] ВНИМАНИЕ: Физика взорвалась (NaN)! Немедленный безопасный перезапуск.");
            SetReward(-2.0f);
            EndEpisode();
            return;
        }

        // Даем физике Unity 10 кадров на то, чтобы применить телепортацию и обновить координаты
        if (episodeSteps > 10)
        {
            // 1. Штраф, если упал (просел ниже динамического порога высоты)
            if (currentY < fallThresholdHeight)
            {
                Debug.Log($"[G1Agent] Эпизод завершен (Падение)! Шагов: {episodeSteps}, Y: {currentY:F2}м (порог {fallThresholdHeight:F2}м)");
                SetReward(-2.0f);
                EndEpisode();
                return;
            }

            // 2. Бонус за удержание равновесия (вертикальность корпуса)
            if (uprightBonus < 0.5f) // Если робот сильно завалился
            {
                Debug.Log($"[G1Agent] Эпизод завершен (Завалился)! Шагов: {episodeSteps}, Upright: {uprightBonus:F2}");
                SetReward(-1.0f);
                EndEpisode();
                return;
            }
            AddReward(uprightBonus * 0.002f); // Поощряем вертикальную осанку

            // 2.5 Бонус за правильное положение головы над центром тяжести (тазом) - прямохождение
            if (headTransform != null)
            {
                Vector3 pelvisPos = rootBody.transform.position;
                Vector3 headPos = headTransform.position;
                
                Vector3 robotForward = rootBody.transform.forward;
                Vector3 robotRight = rootBody.transform.right;
                Vector3 pelvisToHead = headPos - pelvisPos;
                
                float forwardOffset = Vector3.Dot(pelvisToHead, robotForward);
                float lateralOffset = Vector3.Dot(pelvisToHead, robotRight);
                
                // Бонус за удержание головы строго по центру (боковое отклонение не более 7 см)
                if (Mathf.Abs(lateralOffset) < 0.07f)
                {
                    AddReward(0.001f);
                }
                
                // Бонус за осанку прямохождения (голова над тазом или слегка впереди на 1-8 см для естественного наклона при ходьбе)
                if (forwardOffset >= -0.02f && forwardOffset <= 0.08f)
                {
                    AddReward(0.002f); // Поощряем красивую осанку прямохождения
                }
                else
                {
                    AddReward(-0.002f); // Наказываем за сильный «наклон» вперед или завал назад
                }
            }
        }

        if (target != null)
        {
            // 3. Награда за приближение к цели
            float currentDistance = Vector3.Distance(rootBody.transform.position, target.position);
            float progress = previousDistanceToTarget - currentDistance;
            previousDistanceToTarget = currentDistance;

            // Награждаем за скорость движения в сторону цели
            AddReward(progress * 15f);

            // 4. Достижение цели
            if (currentDistance < 1.0f)
            {
                Debug.Log($"[G1Agent] Эпизод завершен (Успех)! Робот дошел до цели! Шагов: {episodeSteps}");
                SetReward(10.0f);
                EndEpisode();
            }
        }

        // 5. Штраф за дерганье и резкие движения (регуляризация усилий)
        float actionPenalty = 0f;
        for (int i = 0; i < continuousActions.Length; i++)
        {
            actionPenalty += Mathf.Abs(continuousActions[i]);
        }
        // Небольшой штраф, чтобы робот не дергал суставами без необходимости
        AddReward(- (actionPenalty / joints.Count) * 0.001f);
    }
}
