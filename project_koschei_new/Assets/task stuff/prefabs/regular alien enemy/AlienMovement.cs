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
    public float searchDuration = 3f;        // time to look around at last seen pos
    public float idleTimeAtPatrolPoint = 2f; // stand still before picking next random point

    [Header("Random Patrol")]
    public float patrolRadius = 10f;  // how far from home it can wander

    [Header("Animation")]
    public Animator animator;
    public string moveXParam = "MoveX";
    public string moveZParam = "MoveZ";

    [Header("Sensor")]
    public AlienSensor sensor;  // assign in Inspector (on root or Eye child)

    NavMeshAgent agent;
    AIState state = AIState.Idle;
    float stateTimer = 0f;
    Transform currentTargetPlayer;
    Vector3 lastSeenPlayerPos;
    Vector3 homePosition; // center of random patrol area

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (sensor == null)
            sensor = GetComponentInChildren<AlienSensor>();

        homePosition = transform.position; // use spawn position as patrol center

        state = AIState.Patrol;
        agent.speed = patrolSpeed;
        SetRandomPatrolDestination();
    }

    void Update()
    {
        // Use the external raycast sensor to find the closest visible Player
        Transform player = sensor != null ? sensor.GetClosestTarget("Player") : null;
        if (player != null)
        {
            currentTargetPlayer = player;
            lastSeenPlayerPos = player.position;
            SwitchToChase();
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
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f)
        {
            // Arrived at random patrol point -> idle for a bit, then pick a new one
            state = AIState.Idle;
            stateTimer = idleTimeAtPatrolPoint;
            agent.isStopped = true;
        }
    }

    void SetRandomPatrolDestination()
    {
        // Pick a random point in a circle on XZ, around homePosition
        Vector2 randomCircle = Random.insideUnitCircle * patrolRadius;
        Vector3 candidate = homePosition + new Vector3(randomCircle.x, 0f, randomCircle.y);

        // Project to NavMesh
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
        else
        {
            agent.SetDestination(candidate);
        }

        agent.isStopped = false;
    }

    // ========== Idle ==========
    void UpdateIdle()
    {
        stateTimer -= Time.deltaTime;

        // slowly look around
        transform.Rotate(Vector3.up, 30f * Time.deltaTime);

        if (stateTimer <= 0f)
        {
            state = AIState.Patrol;
            agent.speed = patrolSpeed;
            SetRandomPatrolDestination();
        }
    }

    // ========== Chase ==========
    void SwitchToChase()
    {
        if (state == AIState.Chase) return;
        state = AIState.Chase;
        agent.speed = chaseSpeed;
        agent.isStopped = false;
    }

    void UpdateChase()
    {
        if (currentTargetPlayer != null)
        {
            agent.SetDestination(currentTargetPlayer.position);
            lastSeenPlayerPos = currentTargetPlayer.position;
        }
        else
        {
            agent.SetDestination(lastSeenPlayerPos);
        }

        // At last seen point -> search
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f)
        {
            state = AIState.Search;
            stateTimer = searchDuration;
        }
    }

    // ========== Search ==========
    void UpdateSearch()
    {
        agent.SetDestination(lastSeenPlayerPos);

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f)
        {
            agent.isStopped = true;
            stateTimer -= Time.deltaTime;

            // look around faster
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

        Vector3 worldVel = agent.velocity;
        Vector3 localVel = transform.InverseTransformDirection(worldVel);

        float maxSpeed = agent.speed > 0 ? agent.speed : 1f;
        float moveX = Mathf.Clamp(localVel.x / maxSpeed, -1f, 1f);
        float moveZ = Mathf.Clamp(localVel.z / maxSpeed, -1f, 1f);

        animator.SetFloat(moveXParam, moveX);
        animator.SetFloat(moveZParam, moveZ);
    }
}
