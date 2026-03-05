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
    public float attack1Duration = 0.8f;  // movement lock time for attack1
    public float attack2Duration = 1.0f;  // movement lock time for attack2

    [Header("Rotation")]
    public float turnSpeed = 10f;

    NavMeshAgent agent;
    AIState state = AIState.Idle;
    float stateTimer = 0f;
    Transform currentTargetPlayer;
    Vector3 lastSeenPlayerPos;
    Vector3 homePosition;

    float attackTimer = 0f;        // cooldown before next attack
    bool isAttacking = false;      // true while attack animation is considered "running"
    float attackMoveLockTimer = 0f; // how long movement is locked

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (sensor == null)
            sensor = GetComponentInChildren<AlienSensor>();

        homePosition = transform.position;

        agent.updateRotation = false;

        state = AIState.Patrol;
        agent.speed = patrolSpeed;
        SetRandomPatrolDestination();
    }

    void Update()
    {
        // Sense only when not currently locked in an attack
        if (!isAttacking)
        {
            Transform player = sensor != null ? sensor.GetClosestTarget("Player") : null;
            if (player != null)
            {
                currentTargetPlayer = player;
                lastSeenPlayerPos = player.position;
                SwitchToChase();
            }
        }

        attackTimer -= Time.deltaTime;

        // Count down attack movement lock
        if (isAttacking)
        {
            attackMoveLockTimer -= Time.deltaTime;
            if (attackMoveLockTimer <= 0f)
            {
                isAttacking = false;
            }
        }

        // State machine
        switch (state)
        {
            case AIState.Patrol:
                UpdatePatrol();
                break;
            case AIState.Idle:
                UpdateIdle();
                break;
            case AIState.Chase:
                UpdateChase();
                break;
            case AIState.Search:
                UpdateSearch();
                break;
        }

        UpdateAnimationFromAgent();
    }

    // ========== Random Patrol ==========
    void UpdatePatrol()
    {
        if (isAttacking) return; // don't move while attacking

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

    // ========== Idle ==========
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

    // ========== Chase + Attack ==========
    void SwitchToChase()
    {
        if (state == AIState.Chase) return;
        state = AIState.Chase;
        agent.speed = chaseSpeed;
        if (!isAttacking)
            agent.isStopped = false;
    }

    void UpdateChase()
    {
        if (isAttacking)
    {
        agent.isStopped = true;
        agent.velocity = Vector3.zero;

        // Keep facing player while attacking
        if (currentTargetPlayer != null)
            FaceTarget(currentTargetPlayer);

        return;
    }

        if (currentTargetPlayer != null)
        {
            FaceTarget(currentTargetPlayer);

            float dist = Vector3.Distance(transform.position, currentTargetPlayer.position);

            if (dist > attackRange)
            {
                // Too far -> keep chasing
                agent.isStopped = false;
                agent.SetDestination(currentTargetPlayer.position);
                lastSeenPlayerPos = currentTargetPlayer.position;
            }
            else
            {
                // In range -> stop and attack
                agent.isStopped = true;
                FaceTarget(currentTargetPlayer);
                TryAttack();
            }
        }
        else
        {
            // No target reference -> go to last seen position
            agent.isStopped = false;
            agent.SetDestination(lastSeenPlayerPos);

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

        if (dir.sqrMagnitude < 0.001f)
            return;

        Quaternion lookRot = Quaternion.LookRotation(dir);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            lookRot,
            Time.deltaTime * turnSpeed
        );
    }

    void TryAttack()
    {
        if (attackTimer > 0f) return;
        if (animator == null) return;
        if (isAttacking) return;

        isAttacking = true;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;

        int which = Random.Range(0, 2);

        if (which == 0)
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

        // Later you can call a damage method here or from an Animation Event:
        // public void OnAttackHit() { ... }
    }

    // ========== Search ==========
    void UpdateSearch()
    {
        if (isAttacking) return;

        agent.SetDestination(lastSeenPlayerPos);

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f)
        {
            agent.isStopped = true;
            stateTimer -= Time.deltaTime;
            transform.Rotate(Vector3.up, 60f * Time.deltaTime);

            if (stateTimer <= 0f)
            {
                currentTargetPlayer = null;
                agent.isStopped = false;
                state = AIState.Patrol;
                agent.speed = patrolSpeed;
                SetRandomPatrolDestination();
            }
        }
    }

    // ========== Animation from NavMeshAgent velocity ==========
    void UpdateAnimationFromAgent()
    {
        if (animator == null) return;

        Vector3 worldVel = isAttacking ? Vector3.zero : agent.velocity;
        Vector3 localVel = transform.InverseTransformDirection(worldVel);

        float maxSpeed = agent.speed > 0 ? agent.speed : 1f;
        float moveX = Mathf.Clamp(localVel.x / maxSpeed, -1f, 1f);
        float moveZ = Mathf.Clamp(localVel.z / maxSpeed, -1f, 1f);

        animator.SetFloat(moveXParam, moveX);
        animator.SetFloat(moveZParam, moveZ);
    }
}
