using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Collections.Generic;

public class G1Agent : Agent
{
    [Header("Robot Parts")]
    public List<ArticulationBody> joints = new List<ArticulationBody>();
    public Transform target; 

    private ArticulationBody rootBody;
    
    [Header("Posture Control")]
    [Tooltip("Трансформ головы для контроля осанки. Если пустой, выполнится автопоиск.")]
    public Transform headTransform;
    
    [Header("Foot Ground Sensors")]
    public FootSensor leftFoot;
    public FootSensor rightFoot;

    private int episodeSteps = 0;
    private int obsSize;

    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;
    private Vector3 initialPelvisPosition;
    private Quaternion initialPelvisRotation;
    
    private List<float> initialJointTargetsDegrees = new List<float>();
    private List<float> initialJointPositions = new List<float>();
    private float fallThresholdHeight;

    public override void Initialize()
    {
        joints.Clear();
        initialJointTargetsDegrees.Clear();
        initialJointPositions.Clear();

        DecisionRequester dr = GetComponent<DecisionRequester>();
        if (dr == null)
        {
            dr = gameObject.AddComponent<DecisionRequester>();
            dr.DecisionPeriod = 5;
        }

        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;

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
            fallThresholdHeight = initialPelvisPosition.y * 0.6f;
        }

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

        foreach (var body in allBodies)
        {
            body.solverIterations = 30;
            body.solverVelocityIterations = 12;

            string lowerName = body.name.ToLower();
            if (lowerName.Contains("ankle_roll") || lowerName.Contains("foot") || lowerName.Contains("toe"))
            {
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }

            if (body == rootBody || body.isRoot) continue;
            
            if (body.jointType != ArticulationJointType.FixedJoint)
            {
                joints.Add(body);
                
                float defaultTarget = body.xDrive.target;
                if (lowerName.Contains("elbow"))
                {
                    defaultTarget = 15f; 
                }
                else if (lowerName.Contains("shoulder_pitch"))
                {
                    defaultTarget = 0f; 
                }
                else if (lowerName.Contains("shoulder_roll"))
                {
                    defaultTarget = 0f; 
                }
                else if (lowerName.Contains("shoulder_yaw"))
                {
                    defaultTarget = 0f; 
                }
                else if (lowerName.Contains("knee"))
                {
                    defaultTarget = 25f; 
                }
                
                initialJointTargetsDegrees.Add(defaultTarget);
                initialJointPositions.Add(defaultTarget * Mathf.Deg2Rad);
                
                var drive = body.xDrive;
                drive.stiffness = 200f; 
                drive.damping = 20f;    
                body.xDrive = drive;
            }
        }

        obsSize = 16 + joints.Count * 2;
    }

    public override void OnEpisodeBegin()
    {
        episodeSteps = 0;

        float randomYaw = Random.Range(-180f, 180f);
        transform.localPosition = initialLocalPosition;
        transform.localRotation = initialLocalRotation * Quaternion.Euler(0f, randomYaw, 0f);
        
        if (rootBody != null)
        {
            rootBody.linearVelocity = Vector3.zero;
            rootBody.angularVelocity = Vector3.zero;

            Vector3 safeSpawnPos = initialPelvisPosition + new Vector3(0f, 0.03f, 0f);
            Quaternion randomRotation = initialPelvisRotation * Quaternion.Euler(0f, randomYaw, 0f);
            rootBody.TeleportRoot(safeSpawnPos, randomRotation);
            
            rootBody.linearVelocity = Vector3.zero;
            rootBody.angularVelocity = Vector3.zero;
        }

        for (int i = 0; i < joints.Count; i++)
        {
            var joint = joints[i];
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
        if (rootBody == null)
        {
            for (int i = 0; i < obsSize; i++) sensor.AddObservation(0f);
            return;
        }

        if (float.IsNaN(rootBody.transform.position.y) || float.IsNaN(rootBody.transform.rotation.x))
        {
            for (int i = 0; i < obsSize; i++) sensor.AddObservation(0f);
            return;
        }

        // 1. Позиция таза и наклон корпуса (5)
        sensor.AddObservation(rootBody.transform.position.y);
        sensor.AddObservation(rootBody.transform.rotation);

        // 2. Линейная и угловая скорости таза (6)
        sensor.AddObservation(rootBody.linearVelocity);
        sensor.AddObservation(rootBody.angularVelocity);

        // 3. Локальный вектор направления на цель (3)
        Vector3 worldToTarget = (target != null) ? (target.position - rootBody.transform.position) : Vector3.zero;
        Vector3 localToTarget = rootBody.transform.InverseTransformDirection(worldToTarget);
        sensor.AddObservation(localToTarget);

        // 4. Позиции и скорости суставов (joints * 2)
        foreach (var joint in joints)
        {
            if (joint.dofCount > 0)
            {
                sensor.AddObservation(joint.jointPosition[0]);
                sensor.AddObservation(joint.jointVelocity[0]);
            }
        }

        // 5. Состояния контакта стоп с землей (2)
        sensor.AddObservation(leftFoot != null && leftFoot.isGrounded ? 1f : 0f);
        sensor.AddObservation(rightFoot != null && rightFoot.isGrounded ? 1f : 0f);
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

        if (float.IsNaN(currentY) || float.IsNaN(uprightBonus))
        {
            SetReward(-2.0f);
            EndEpisode();
            return;
        }

        Vector3 dirToTarget = (target != null) ? (target.position - rootBody.transform.position).normalized : rootBody.transform.forward;
        float facingDot = Vector3.Dot(rootBody.transform.forward, dirToTarget);
        float postureMultiplier = 1.0f; 

        if (episodeSteps > 3)
        {
            // Штраф при падении
            if (currentY < fallThresholdHeight)
            {
                SetReward(-2.0f);
                EndEpisode();
                return;
            }

            // Штраф при потере баланса
            if (uprightBonus < 0.5f) 
            {
                SetReward(-1.0f);
                EndEpisode();
                return;
            }
            AddReward(uprightBonus * 0.015f); 

            // Контроль глобального наклона корпуса вперед
            if (headTransform != null)
            {
                Vector3 pelvisPos = rootBody.transform.position;
                Vector3 headPos = headTransform.position;
                Vector3 pelvisToHead = headPos - pelvisPos;
                
                Vector3 pelvisToHeadFlat = new Vector3(pelvisToHead.x, 0, pelvisToHead.z);
                Vector3 dirToTargetFlat = new Vector3(dirToTarget.x, 0, dirToTarget.z).normalized;
                
                float globalLeanForward = Vector3.Dot(pelvisToHeadFlat, dirToTargetFlat);
                
                if (globalLeanForward < -0.02f) 
                {
                    postureMultiplier = 0.1f; 
                    AddReward(-0.02f); 
                }
                else if (globalLeanForward >= 0.05f && globalLeanForward <= 0.2f)
                {
                    AddReward(0.004f); 
                }
            }

            // Штраф за движение приставным шагом
            if (facingDot < 0.5f) 
            {
                AddReward(-0.02f); 
            }
        }

        if (target != null)
        {
            // Награда за скорость по вектору к цели
            float speedTowardTarget = Vector3.Dot(rootBody.linearVelocity, dirToTarget);
            float directionMultiplier = Mathf.Clamp01(facingDot);

            if (speedTowardTarget > 0)
            {
                AddReward(speedTowardTarget * 0.2f * directionMultiplier * postureMultiplier);
            }
            else
            {
                AddReward(speedTowardTarget * 0.1f);
            }

            // Достижение целевой зоны
            float currentDistance = Vector3.Distance(rootBody.transform.position, target.position);
            if (currentDistance < 1.0f)
            {
                Debug.Log($"[G1Agent] Эпизод завершен (Успех)! Шагов: {episodeSteps}");
                AddReward(10.0f); 
                EndEpisode();
                return;
            }
        }

        // Наказание за резкие управляющие воздействия (минимизация рывков)
        float actionPenalty = 0f;
        for (int i = 0; i < continuousActions.Length; i++)
        {
            actionPenalty += Mathf.Abs(continuousActions[i]);
        }
        AddReward(- (actionPenalty / continuousActions.Length) * 0.025f);
    }
}