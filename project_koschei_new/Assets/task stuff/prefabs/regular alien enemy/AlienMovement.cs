using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class AlienMovement : MonoBehaviour
{
    enum AIState { Idle, Patrol, Chase, Search }

    [Header("Movement Speeds")]
    public float patrolSpeed = 2f;
    public float chaseSpeed = 4f;

    [Header("Search / Idle")]
    public float searchDuration = 3f;
    public float idleTimeAtPatrolPoint = 2f;

    [Header("Random Patrol")]
    public float patrolRadius = 10f;

    [Header("Animation")]
    public Animator animator;
    public string moveXParam = "MoveX";
    public string moveZParam = "MoveZ";

    [Header("Sensor")]
    public AlienSensor sensor;

    [Header("Attack")]
    public float attackRange = 2f;
    public string attack1Trigger = "Attack1Trigger";
    public string attack2Trigger = "Attack2Trigger";
    public float attack1Cooldown = 1.2f;
    public float attack2Cooldown = 1.5f;
    public float attack1Duration = 0.8f;
    public float attack2Duration = 1.0f;

    // ?? Assigned by ScavengerRaidTask at runtime — null outside the task ??
    // NPC1 is never referenced here, so it is never targeted under any circumstance.
    [HideInInspector] public Transform scientistTarget;

    NavMeshAgent agent;
    AIState state = AIState.Idle;
    float stateTimer = 0f;

    Transform currentTarget;             // player OR scientist, whoever is active
    bool currentTargetIsScientist = false;
    Vector3 lastSeenTargetPos;
    Vector3 homePosition;

    float attackTimer = 0f;
    bool isAttacking = false;
    float attackMoveLockTimer = 0f;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (sensor == null) sensor = GetComponentInChildren<AlienSensor>();

        homePosition = transform.position;
        state = AIState.Patrol;
        agent.speed = patrolSpeed;
        SetRandomPatrolDestination();
    }

    void Update()
    {
        if (!isAttacking)
        {
            // ?? Priority 1: Player via sensor cone ??
            Transform player = sensor != null ? sensor.GetClosestTarget("Player") : null;

            if (player != null)
            {
                currentTarget = player;
                currentTargetIsScientist = false;
                lastSeenTargetPos = player.position;
                SwitchToChase();
            }
            // ?? Priority 2: Scientist (only when task has assigned her) ??
            // Direct reference — the alien always "tracks" the scientist's position
            // regardless of sensor cone, but only when scientistTarget is set.
            else if (scientistTarget != null && scientistTarget.gameObject.activeInHierarchy)
            {
                currentTarget = scientistTarget;
                currentTargetIsScientist = true;
                lastSeenTargetPos = scientistTarget.position;
                SwitchToChase();
            }
        }

        attackTimer -= Time.deltaTime;

        if (isAttacking)
        {
            attackMoveLockTimer -= Time.deltaTime;
            if (attackMoveLockTimer <= 0f)
                isAttacking = false;
        }

        switch (state)
        {
            case AIState.Patrol: UpdatePatrol(); break;
            case AIState.Idle: UpdateIdle(); break;
            case AIState.Chase: UpdateChase(); break;
            case AIState.Search: UpdateSearch(); break;
        }

        UpdateAnimationFromAgent();
    }

    // ??????????????????????????????????????????????
    // Patrol
    // ??????????????????????????????????????????????
    void UpdatePatrol()
    {
        if (isAttacking) return;
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f)
        {
            state = AIState.Idle;
            stateTimer = idleTimeAtPatrolPoint;
            agent.isStopped = true;
        }
    }

    void SetRandomPatrolDestination()
    {
        if (isAttacking) return;
        Vector2 randomCircle = Random.insideUnitCircle * patrolRadius;
        Vector3 candidate = homePosition + new Vector3(randomCircle.x, 0f, randomCircle.y);

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
        else
            agent.SetDestination(candidate);

        agent.isStopped = false;
    }

    // ??????????????????????????????????????????????
    // Idle
    // ??????????????????????????????????????????????
    void UpdateIdle()
    {
        if (isAttacking) return;
        stateTimer -= Time.deltaTime;
        transform.Rotate(Vector3.up, 30f * Time.deltaTime);

        if (stateTimer <= 0f)
        {
            state = AIState.Patrol;
            agent.speed = patrolSpeed;
            SetRandomPatrolDestination();
        }
    }

    // ??????????????????????????????????????????????
    // Chase + Attack
    // ??????????????????????????????????????????????
    void SwitchToChase()
    {
        if (state == AIState.Chase) return;
        state = AIState.Chase;
        agent.speed = chaseSpeed;
        if (!isAttacking) agent.isStopped = false;
    }

    void UpdateChase()
    {
        if (isAttacking)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            return;
        }

        if (currentTarget != null)
        {
            float dist = Vector3.Distance(transform.position, currentTarget.position);
            if (dist > attackRange)
            {
                agent.isStopped = false;
                agent.SetDestination(currentTarget.position);
                lastSeenTargetPos = currentTarget.position;
            }
            else
            {
                agent.isStopped = true;
                FaceTarget(currentTarget);
                TryAttack();
            }
        }
        else
        {
            // Lost sight — go to last known position
            agent.isStopped = false;
            agent.SetDestination(lastSeenTargetPos);

            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f)
            {
                state = AIState.Search;
                stateTimer = searchDuration;
            }
        }
    }

    void FaceTarget(Transform target)
    {
        Vector3 dir = target.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
        {
            Quaternion lookRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * 10f);
        }
    }

    void TryAttack()
    {
        if (attackTimer > 0f || animator == null || isAttacking) return;

        isAttacking = true;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;

        if (Random.Range(0, 2) == 0)
        {
            animator.SetTrigger(attack1Trigger);
            attackTimer = attack1Cooldown;
            attackMoveLockTimer = attack1Duration;
        }
        else
        {
            animator.SetTrigger(attack2Trigger);
            attackTimer = attack2Cooldown;
            attackMoveLockTimer = attack2Duration;
        }
    }

    /// <summary>
    /// Hook this up from an Animation Event on the attack animation.
    /// It applies damage to whichever target the alien is currently attacking.
    /// </summary>
    public void OnAttackHit()
    {
        if (currentTarget == null) return;

        if (currentTargetIsScientist)
        {
            currentTarget.GetComponent<ScientistHealth>()?.TakeDamage(10f);
        }
        else
        {
            // Plug in your existing PlayerHealth system here:
            // currentTarget.GetComponent<PlayerHealth>()?.TakeDamage(10f);
        }
    }

    // ??????????????????????????????????????????????
    // Search
    // ??????????????????????????????????????????????
    void UpdateSearch()
    {
        if (isAttacking) return;

        agent.SetDestination(lastSeenTargetPos);

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f)
        {
            agent.isStopped = true;
            stateTimer -= Time.deltaTime;
            transform.Rotate(Vector3.up, 60f * Time.deltaTime);

            if (stateTimer <= 0f)
            {
                currentTarget = null;
                currentTargetIsScientist = false;
                agent.isStopped = false;
                state = AIState.Patrol;
                agent.speed = patrolSpeed;
                SetRandomPatrolDestination();
            }
        }
    }

    // ??????????????????????????????????????????????
    // Animation
    // ??????????????????????????????????????????????
    void UpdateAnimationFromAgent()
    {
        if (animator == null) return;

        Vector3 worldVel = isAttacking ? Vector3.zero : agent.velocity;
        Vector3 localVel = transform.InverseTransformDirection(worldVel);

        float maxSpeed = agent.speed > 0 ? agent.speed : 1f;
        animator.SetFloat(moveXParam, Mathf.Clamp(localVel.x / maxSpeed, -1f, 1f));
        animator.SetFloat(moveZParam, Mathf.Clamp(localVel.z / maxSpeed, -1f, 1f));
    }
}
