using UnityEngine;
using UnityEngine.AI;

public class AILocomotion : MonoBehaviour
{
    enum AIState { Patrol, Chase, Search }
    [SerializeField] AIState state = AIState.Patrol;

    [Header("Sensor")]
    public float viewDistance = 15f;
    [Range(0, 180)] public float viewAngle = 90f;
    public float proximityRange = 3f;
    public LayerMask sightMask;   // Player + Environment
    public string playerTag = "Player";

    [Header("Movement")]
    public float patrolSpeed = 2f;
    public float chaseSpeed = 5f;
    public float patrolRadius = 10f;
    public float attackRange = 2f;

    [Header("Animation")]
    public string speedParam = "Speed";
    public string attackTrigger = "Attack1Trigger";

    NavMeshAgent agent;
    Animator animator;

    Vector3 homePosition;
    Vector3 lastSeenPlayerPos;
    float searchTimer;

    Transform currentTarget; // 🔥 dynamic target

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        homePosition = transform.position;
        state = AIState.Patrol;
    }

    void Update()
    {
        Transform detectedPlayer = DetectPlayer();

        if (detectedPlayer != null)
        {
            currentTarget = detectedPlayer;
            lastSeenPlayerPos = detectedPlayer.position;
            state = AIState.Chase;
        }

        switch (state)
        {
            case AIState.Patrol:
                UpdatePatrol();
                break;

            case AIState.Chase:
                UpdateChase(detectedPlayer != null);
                break;

            case AIState.Search:
                UpdateSearch();
                break;
        }

        animator.SetFloat(speedParam, agent.velocity.magnitude);
    }

    // ===================== SENSOR =====================
    Transform DetectPlayer()
    {
        Collider[] nearby = Physics.OverlapSphere(transform.position, viewDistance, sightMask);

        foreach (var col in nearby)
        {
            if (!col.CompareTag(playerTag)) continue;

            Vector3 dir = col.transform.position - transform.position;
            float distance = dir.magnitude;

            // Proximity
            if (distance <= proximityRange)
                return col.transform;

            float angle = Vector3.Angle(transform.forward, dir);
            if (angle > viewAngle * 0.5f)
                continue;

            if (Physics.Raycast(
                transform.position + Vector3.up,
                dir.normalized,
                out RaycastHit hit,
                viewDistance,
                sightMask))
            {
                if (hit.collider.CompareTag(playerTag))
                    return hit.transform;
            }
        }

        return null;
    }

    // ===================== CHASE =====================
    void UpdateChase(bool seesPlayer)
    {
        if (currentTarget == null)
        {
            state = AIState.Search;
            searchTimer = 5f;
            return;
        }

        agent.speed = chaseSpeed;

        float dist = Vector3.Distance(transform.position, currentTarget.position);

        if (dist <= attackRange)
        {
            agent.isStopped = true;
            agent.ResetPath();
            FaceTarget(currentTarget.position);

            if (Time.frameCount % 60 == 0)
                animator.SetTrigger(attackTrigger);
        }
        else
        {
            agent.isStopped = false;
            agent.SetDestination(currentTarget.position);
        }

        if (!seesPlayer)
        {
            state = AIState.Search;
            searchTimer = 5f;
        }
    }

    // ===================== PATROL =====================
    void UpdatePatrol()
    {
        agent.speed = patrolSpeed;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f)
        {
            SetRandomPatrolPoint();
        }
    }

    // ===================== SEARCH =====================
    void UpdateSearch()
    {
        agent.SetDestination(lastSeenPlayerPos);

        if (agent.remainingDistance <= 0.5f)
        {
            searchTimer -= Time.deltaTime;
            transform.Rotate(Vector3.up, 150f * Time.deltaTime);

            if (searchTimer <= 0f)
            {
                currentTarget = null;
                state = AIState.Patrol;
            }
        }
    }

    // ===================== HELPERS =====================
    void SetRandomPatrolPoint()
    {
        Vector2 rand = Random.insideUnitCircle * patrolRadius;
        Vector3 target = homePosition + new Vector3(rand.x, 0, rand.y);
        agent.SetDestination(target);
    }

    void FaceTarget(Vector3 target)
    {
        Vector3 dir = (target - transform.position).normalized;
        dir.y = 0;

        if (dir != Vector3.zero)
        {
            Quaternion rot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, Time.deltaTime * 10f);
        }
    }
}
