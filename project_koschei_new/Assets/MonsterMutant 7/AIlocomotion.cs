using UnityEngine;
using UnityEngine.AI;
using System.Collections;

public class AILocomotion : MonoBehaviour
{
    enum AIState { Patrol, Chase, Search }
    [SerializeField] AIState state = AIState.Patrol;

    [Header("Agent")]
    NavMeshAgent agent;
    public float agentAcceleration = 8f;
    public float agentAngularSpeed = 720f;
    public float agentStoppingDistance = 0.2f;

    [Header("Detection")]
    public float viewDistance = 15f;
    [Range(0, 180)] public float viewAngle = 90f;
    public float proximityRange = 15f;
    public LayerMask sightMask;
    public string playerTag = "Player";
    public LayerMask targetMask;     // Player layer
    public LayerMask obstacleMask;   // Walls / Environment


    [Header("Search")]
    bool seesPlayer;
    Vector3 lastSeenPlayerPos;
    Transform currentTarget;
    Vector3 homePosition;
    float searchTimer;
    float currentSearchWaitTime;

    [Header("Movement")]
    public float patrolSpeed = 2f;
    public float chaseSpeed = 5f;
    public float patrolRadius = 10f;
    public float attackRange = 2f;

    [Header("Combat")]
    public float attackCooldown = 2f;
    public float attackDuration = 1.2f;
    float lastAttackTime;
    bool isAttacking;
    bool hasDealtDamageThisAttack;

    [Header("Damage")]
    public float attackDamage = 25f;
    [Range(0, 180)] public float attackAngle = 90f; // front cone angle

    [Header("Attack Facing")]
    public float attackTurnSpeed = 10f;

    [Header("Animation")]
    public string speedParam = "Speed";
    Animator animator;

    [Header("Debug Gizmos")]
    public bool showState = true;
    public bool showViewDistance = true;
    public bool showViewCone = true;
    public bool showProximity = true;
    public bool showAttackRange = true;
    public bool showAttackCone = true;
    public bool showForward = true;
    public bool showCurrentTarget = true;
    public bool showLastSeen = true;
    public bool showHome = true;
    public bool showNavDestination = true;
    public bool showSearchArea = true;
    public bool showAttackIndicator = true;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        homePosition = transform.position;
        state = AIState.Patrol;
        animator.speed = 1f; // ensures animation not slowed

        agent.updateRotation = false;
        agent.acceleration = agentAcceleration;
        agent.angularSpeed = agentAngularSpeed;
        agent.stoppingDistance = agentStoppingDistance;

    }
    
    // ===================== BRAIN =====================

    void Update()
    {
        if (agent == null || animator == null)
        {
            return;
        }

        Transform detected = DetectPlayer();
        seesPlayer = detected != null;

        if (seesPlayer)
        {
            currentTarget = detected;
            lastSeenPlayerPos = detected.position;
            state = AIState.Chase;
        }

        UpdateState();

        HandlePhysicalMovement();
        HandleRotation();
    }

    void UpdateState()
    {
        switch (state)
        {
            case AIState.Patrol:
                agent.speed = patrolSpeed;
                UpdatePatrol();
                break;

            case AIState.Chase:
                agent.speed = chaseSpeed;
                UpdateChase(seesPlayer);
                break;

            case AIState.Search:
                agent.speed = patrolSpeed;
                UpdateSearch();
                break;
        }
    }


    // ===================== MOVEMENT CONTROL =====================
   void HandlePhysicalMovement()
    {
        if (IsInLocomotionState())
        {
            agent.isStopped = false;

            float currentSpeed = agent.velocity.magnitude;
            animator.SetFloat(speedParam, currentSpeed, 0.1f, Time.deltaTime);
        }
        else
        {
            agent.isStopped = true;
            animator.SetFloat(speedParam, 0f);
        }
    }


    bool IsInLocomotionState()
    {
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        return stateInfo.IsTag("Locomotion");
    }

    void HandleRotation()
    {
        if (agent.velocity.sqrMagnitude > 0.1f && !isAttacking)
        {
            Quaternion targetRot = Quaternion.LookRotation(agent.velocity.normalized);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRot,
                Time.deltaTime * 10f
            );
        }
    }


    // ===================== DETECTION =====================
    Transform DetectPlayer()
    {
        // Collider[] hits = Physics.OverlapSphere(transform.position, viewDistance, sightMask);
        Collider[] hits = Physics.OverlapSphere(transform.position, viewDistance);


        foreach (Collider col in hits)
        {
            if (!col.CompareTag(playerTag))
            {
                continue;
            }

            Vector3 origin = transform.position + Vector3.up * 1.6f;
            Vector3 target = col.bounds.center;

            Vector3 dir = target - origin;
            float distance = dir.magnitude;

            if (distance > viewDistance)
            {
                continue;
            }

            float angle = Vector3.Angle(transform.forward, dir);

            if (angle > viewAngle * 0.5f)
            {
                continue;
            }
            
            if (Physics.Raycast(origin, dir.normalized, out RaycastHit hit, viewDistance))
            // if (Physics.Raycast(origin, dir.normalized, out RaycastHit hit, viewDistance, sightMask))
            {
                if (hit.collider.CompareTag(playerTag))
                {
                    Debug.DrawLine(origin, hit.point, Color.green);
                    return col.transform;
                }
            }
        }

        return null;
    }

    // ===================== CHASE =====================
    void UpdateChase(bool seesPlayer)
    {
        if (currentTarget == null)
        {
            EnterSearch();
            return;
        }

        float dist = Vector3.Distance(transform.position, currentTarget.position);
        agent.speed = chaseSpeed;

        if (dist <= attackRange)
        {
            if (!isAttacking && Time.time >= lastAttackTime + attackCooldown)
            {
                StartCoroutine(PerformAttack());
            }
        }
        else
        {
            if (!isAttacking)
            {
                agent.SetDestination(currentTarget.position);
            }
        }

        if (!seesPlayer)
        {
            EnterSearch();
        }
    }

    // ===================== ATTACK =====================
    IEnumerator PerformAttack()
    {
        isAttacking = true;
        hasDealtDamageThisAttack = false;
        lastAttackTime = Time.time;

        agent.ResetPath();
        agent.isStopped = true;

        TriggerRandomAttack();

        float timer = 0f;

        while (timer < attackDuration)
        {
            timer += Time.deltaTime;

            if (currentTarget != null)
            {
                FaceTargetAttack(currentTarget.position);
                TryDealDamage();
            }

            yield return null;
        }

        agent.isStopped = false;
        isAttacking = false;
    }


    void TryDealDamage()
    {
        if (hasDealtDamageThisAttack) return;
        if (currentTarget == null) return;

        Vector3 toTarget = currentTarget.position - transform.position;
        float distance = toTarget.magnitude;

        if (distance > attackRange) return;

        // Remove height difference
        toTarget.y = 0f;

        // Check angle
        float angle = Vector3.Angle(transform.forward, toTarget);

        if (angle > attackAngle * 0.5f) return;

        Health health = currentTarget.GetComponent<Health>();

        if (health != null)
        {
            health.TakeDamage(attackDamage);
            hasDealtDamageThisAttack = true;

            Debug.Log($"{name} dealt {attackDamage} damage to {currentTarget.name}");
        }
    }
    
    void FaceTargetAttack(Vector3 target)
    {
        Vector3 dir = (target - transform.position).normalized;
        dir.y = 0;

        if (dir == Vector3.zero)
            return;

        Quaternion targetRot = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRot,
            Time.deltaTime * attackTurnSpeed
        );
    }

    void TriggerRandomAttack()
    {
        int attackIndex = Random.Range(1, 13);

        switch (attackIndex)
        {
            case 1: animator.SetTrigger("Attack1"); break;
            case 2: animator.SetTrigger("Attack1LSpike"); break;
            case 3: animator.SetTrigger("Attack1RSpike"); break;
            case 4: animator.SetTrigger("Attack2"); break;
            case 5: animator.SetTrigger("Attack2LSpike"); break;
            case 6: animator.SetTrigger("Attack2RLSpike"); break;
            case 7: animator.SetTrigger("Attack3"); break;
            case 8: animator.SetTrigger("Attack3RSpike"); break;
            case 9: animator.SetTrigger("Attack4"); break;
            case 10: animator.SetTrigger("Attack4RSpike"); break;
            case 11: animator.SetTrigger("Attack5"); break;
            case 12: animator.SetTrigger("Attack5LSpike"); break;
        }
    }

    // ===================== PATROL =====================
    void UpdatePatrol()
    {
        agent.speed = patrolSpeed;

        if (!agent.pathPending &&
            agent.remainingDistance <= agent.stoppingDistance + 0.1f)
        {
            SetRandomPatrolPoint();
        }
    }

    // ===================== SEARCH =====================
    void UpdateSearch()
    {
        agent.speed = patrolSpeed;

        if (searchPointsVisited >= maxSearchPoints)
        {
            currentTarget = null;
            state = AIState.Patrol;
            return;
        }

        if (!movingToSearchPoint)
        {
            Vector2 randomCircle = Random.insideUnitCircle * 4f;
            Vector3 nextPoint = searchCenter + new Vector3(randomCircle.x, 0, randomCircle.y);

            agent.SetDestination(nextPoint);
            movingToSearchPoint = true;
        }

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f)
        {
            waitTimer += Time.deltaTime;
            transform.Rotate(0, Time.deltaTime * 40f, 0);


            // Stand still briefly like "listening"
            if (waitTimer >= currentSearchWaitTime)
            {
                searchPointsVisited++;
                movingToSearchPoint = false;
                waitTimer = 0f;
            }
        }
    }


    Vector3 searchCenter;
    int searchPointsVisited;
    int maxSearchPoints = 5;
    bool movingToSearchPoint;
    float waitTimer;

    void EnterSearch()
    {
        state = AIState.Search;

        searchCenter = lastSeenPlayerPos;
        searchPointsVisited = 0;
        movingToSearchPoint = false;
        waitTimer = 0f;
        currentSearchWaitTime = Random.Range(0.8f, 1.5f);
    }

    void SetRandomPatrolPoint()
    {
        Vector2 rand = Random.insideUnitCircle * patrolRadius;
        Vector3 target = homePosition + new Vector3(rand.x, 0, rand.y);
        agent.SetDestination(target);
    }

    //===================== ANIMATION ====================

    // void UpdateAnimation()
    // {   
    //     float speedPercent = agent.velocity.magnitude / agent.speed;
    //     animator.SetFloat("Speed", speedPercent * agent.speed, 0.1f, Time.deltaTime);
    // }


    // ===================== GIZMOS =====================
    void OnDrawGizmos()
    {
        Vector3 pos = transform.position;

        // ===== STATE CORE =====
        if (showState)
        {
            switch (state)
            {
                case AIState.Patrol: Gizmos.color = Color.green; break;
                case AIState.Chase: Gizmos.color = Color.red; break;
                case AIState.Search: Gizmos.color = Color.yellow; break;
            }

            Gizmos.DrawWireSphere(pos, 0.5f);
        }

        // ===== VIEW DISTANCE =====
        if (showViewDistance)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(pos, viewDistance);
        }

        // ===== VIEW CONE =====
        if (showViewCone)
        {
            DrawArc(pos, viewDistance, viewAngle, Color.cyan);
        }

        // ===== PROXIMITY =====
        if (showProximity)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(pos, proximityRange);
        }

        // ===== ATTACK RANGE =====
        if (showAttackRange)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(pos, attackRange);
        }

        // ===== ATTACK CONE =====
        if (showAttackCone)
        {
            DrawArc(pos, attackRange, attackAngle, Color.red);
        }

        // ===== FORWARD =====
        if (showForward)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(pos, pos + transform.forward * 2f);
        }

        // ===== CURRENT TARGET =====
        if (showCurrentTarget && currentTarget != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(pos, currentTarget.position);
            Gizmos.DrawSphere(currentTarget.position, 0.25f);
        }

        // ===== LAST SEEN =====
        if (showLastSeen && lastSeenPlayerPos != Vector3.zero)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(lastSeenPlayerPos, 0.3f);
        }

        // ===== HOME =====
        if (showHome)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(homePosition, 0.4f);
        }

        // ===== NAV DESTINATION =====
        if (showNavDestination && agent != null && agent.hasPath)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(pos, agent.destination);
            Gizmos.DrawSphere(agent.destination, 0.2f);
        }

        // ===== SEARCH AREA =====
        if (showSearchArea && state == AIState.Search)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
            Gizmos.DrawWireSphere(pos, 1.5f);
        }

        // ===== ATTACK INDICATOR =====
        if (showAttackIndicator && isAttacking)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawSphere(pos + transform.forward * (attackRange * 0.5f), 0.3f);
        }
    }

    // ================= ARC DRAWER =================
    void DrawArc(Vector3 center, float radius, float angle, Color color)
    {
        Gizmos.color = color;

        int segments = 40;
        float step = angle / segments;

        Vector3 prevPoint = center +
            Quaternion.Euler(0, -angle * 0.5f, 0) * transform.forward * radius;

        for (int i = 1; i <= segments; i++)
        {
            float currentAngle = -angle * 0.5f + step * i;

            Vector3 nextPoint = center +
                Quaternion.Euler(0, currentAngle, 0) * transform.forward * radius;

            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }

        Vector3 leftDir = Quaternion.Euler(0, -angle * 0.5f, 0) * transform.forward;
        Vector3 rightDir = Quaternion.Euler(0, angle * 0.5f, 0) * transform.forward;

        Gizmos.DrawLine(center, center + leftDir * radius);
        Gizmos.DrawLine(center, center + rightDir * radius);
    }

}
