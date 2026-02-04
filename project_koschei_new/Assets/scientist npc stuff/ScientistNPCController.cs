using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class NetworkScientistNPCController : NetworkBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 2.0f;          // match this to your walk animation speed
    public float rotationSpeed = 8f;        // how fast to turn toward movement direction
    public float wanderRadius = 10f;
    public float wanderInterval = 5f;

    [Header("Detection")]
    public RaycastConeSensor sensor;        // child sensor
    public float stopDistanceToPlayer = 2f;

    [Header("Patrol Area")]
    public Transform wanderCenter;
    public float navSampleRadius = 3f;

    [Header("Animation Parameters")]
    public string speedParam = "Speed";     // float
    public string talkingParam = "talking"; // bool
    public string deadParam = "dead";       // trigger

    private NavMeshAgent agent;
    private Animator animator;

    private float wanderTimer;
    private bool isDead = false;
    private Transform currentTargetPlayer;

    private NetworkVariable<bool> talkingNet = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();

        if (wanderCenter == null)
        {
            GameObject centerObj = new GameObject($"{name}_WanderCenter");
            centerObj.transform.position = transform.position;
            wanderCenter = centerObj.transform;
        }

        if (sensor == null)
        {
            sensor = GetComponentInChildren<RaycastConeSensor>();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Only decide agent control once spawned (IsServer valid here)
        agent.updatePosition = IsServer;
        agent.updateRotation = false;     // we rotate manually
        agent.speed = walkSpeed;
        agent.acceleration = walkSpeed * 2f;
        agent.angularSpeed = 0f;          // disable agent rotation

        if (IsServer)
        {
            wanderTimer = wanderInterval;
        }

        talkingNet.OnValueChanged += OnTalkingChanged;
    }

    void OnDestroy()
    {
        talkingNet.OnValueChanged -= OnTalkingChanged;
    }

    void Update()
    {
        if (!IsServer)
        {
            // Clients only animate based on velocity and talking flag
            UpdateAnimationsFromVelocity();
            return;
        }

        if (isDead)
        {
            UpdateAnimationsFromVelocity();
            return;
        }

        if (sensor == null)
        {
            sensor = GetComponentInChildren<RaycastConeSensor>();
        }

        currentTargetPlayer = sensor != null ? sensor.GetClosestTarget("Player") : null;

        if (currentTargetPlayer != null)
        {
            HandleChase();
        }
        else
        {
            HandleWander();
        }

        RotateTowardsMovement();
        UpdateAnimationsFromVelocity();
    }

    void HandleChase()
    {
        if (currentTargetPlayer == null) return;

        Vector3 targetPos = currentTargetPlayer.position;
        float dist = Vector3.Distance(transform.position, targetPos);

        if (dist > stopDistanceToPlayer)
        {
            agent.isStopped = false;
            agent.SetDestination(targetPos);
        }
        else
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }
    }

    void HandleWander()
    {
        wanderTimer -= Time.deltaTime;

        bool needNewPoint = !agent.hasPath || agent.remainingDistance < 0.2f || wanderTimer <= 0f;

        if (needNewPoint)
        {
            if (TryGetRandomPointAroundCenter(out Vector3 newPos))
            {
                agent.isStopped = false;
                agent.SetDestination(newPos);
            }
            wanderTimer = wanderInterval;
        }
    }

    bool TryGetRandomPointAroundCenter(out Vector3 result)
    {
        Vector3 center = wanderCenter.position;
        for (int i = 0; i < 8; i++)
        {
            Vector2 circle = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = center + new Vector3(circle.x, 0f, circle.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, navSampleRadius, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
        }

        result = transform.position;
        return false;
    }

    void RotateTowardsMovement()
    {
        Vector3 desired = agent.desiredVelocity;
        desired.y = 0f;

        if (desired.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(desired.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSpeed);
        }
    }

    void UpdateAnimationsFromVelocity()
    {
        if (animator == null) return;

        float speed = agent.velocity.magnitude;

        // Dead zone so tiny velocities don't keep walk playing
        if (speed < 0.05f) speed = 0f;

        float normalizedSpeed = Mathf.InverseLerp(0f, walkSpeed, speed);
        animator.SetFloat(speedParam, normalizedSpeed);
        animator.SetBool(talkingParam, talkingNet.Value);
    }

    void OnTalkingChanged(bool oldVal, bool newVal)
    {
        if (animator == null) return;
        animator.SetBool(talkingParam, newVal);
    }

    // -------- Public API (server-only) --------

    public void StartTalking()
    {
        if (!IsServer || isDead) return;
        talkingNet.Value = true;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }

    public void StopTalking()
    {
        if (!IsServer || isDead) return;
        talkingNet.Value = false;
        agent.isStopped = false;
    }

    public void Die()
    {
        if (!IsServer || isDead) return;
        isDead = true;

        agent.isStopped = true;
        agent.velocity = Vector3.zero;
        agent.ResetPath();
        agent.enabled = false;
        talkingNet.Value = false;

        if (animator != null)
        {
            animator.SetBool(talkingParam, false);
            animator.SetFloat(speedParam, 0f);
            animator.ResetTrigger(deadParam);
            animator.SetTrigger(deadParam);
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (wanderCenter == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(wanderCenter.position, wanderRadius);
    }
#endif
}
