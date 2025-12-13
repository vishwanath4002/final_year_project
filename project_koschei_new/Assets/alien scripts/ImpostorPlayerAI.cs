using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class ImpostorPlayerAI : NetworkBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 3f;
    public float runSpeed = 5f;
    public float acceleration = 5f;

    [Header("AI Behavior - Social Distances")]
    public float detectionRadius = 25f;
    public float approachDistance = 8f;
    public float conversationDistance = 2.5f;
    public float personalSpaceDistance = 1.2f;

    [Header("AI Behavior - Timers")]
    public float minSocialTime = 8f;
    public float maxSocialTime = 20f;
    public float idleWanderInterval = 5f;
    public float socialWanderInterval = 8f;

    [Header("AI Behavior - Movement")]
    public float nearPlayerWanderRadius = 4f;
    public float farWanderRadius = 15f;
    public float arrivalThreshold = 0.8f;
    public float stopMovingThreshold = 0.3f;

    [Header("Ground Check")]
    public Transform groundCheck;
    public LayerMask groundMask;
    public float groundDistance = 0.2f;

    [Header("Obstacle / Sensor")]
    public LayerMask obstacleMask;
    public float obstacleCheckDistance = 1f;

    [Header("Debug")]
    public bool showDebugInfo = true;

    private NavMeshAgent agent;
    private Animator anim;

    private Vector3 lastPosition;
    private bool isGrounded;

    private float currentSpeed = 0f;
    private float currentDirection = 0f;
    private float movementMagnitude = 0f;

    private enum AIState { Wandering, Approaching, Socializing, Stopping }
    private AIState currentState = AIState.Wandering;

    private Transform targetPlayer;
    private Vector3 wanderTarget;
    private float stateTimer;
    private float socialTimer;
    private bool hasApproachedPlayer = false;
    private bool isInitialized = false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
            Invoke(nameof(InitializeAI), 0.2f);
    }

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            Debug.LogError("ImpostorPlayerAI: NavMeshAgent missing.");
            enabled = false;
            return;
        }

        anim = GetComponentInChildren<Animator>();
        if (anim == null)
            Debug.LogWarning("ImpostorPlayerAI: Animator not found in children.");

        agent.updatePosition = true;
        agent.updateRotation = false;

        lastPosition = transform.position;
    }

    void InitializeAI()
    {
        if (isInitialized) return;

        stateTimer = idleWanderInterval;
        socialTimer = 0f;

        PickWanderPoint(transform.position, farWanderRadius);
        agent.speed = walkSpeed;
        agent.SetDestination(wanderTarget);

        isInitialized = true;
        if (showDebugInfo)
            Debug.Log($"[ImpostorAI] INITIALIZED at {transform.position:F1}, wander: {wanderTarget:F1}");
    }

    void Update()
    {
        if (!IsServer || !isInitialized) return;

        CheckGrounded();
        UpdateAIBehavior();
        ApplyRaycastSensor();
        UpdateAnimations();

        if (showDebugInfo && Time.frameCount % 120 == 0)
        {
            Debug.Log($"[ImpostorAI] State={currentState} Pos={transform.position:F1} Target={(targetPlayer ? targetPlayer.name : "NONE")}");
        }
    }

    void CheckGrounded()
    {
        if (groundCheck != null)
            isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);
        else
            isGrounded = Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, 0.3f, groundMask);
    }

    void UpdateAIBehavior()
    {
        targetPlayer = FindNearestPlayer();

        if (targetPlayer != null)
        {
            float dist = Vector3.Distance(transform.position, targetPlayer.position);
            HandlePlayerInteraction(dist);
        }
        else
        {
            HandleSoloWandering();
        }

        stateTimer -= Time.deltaTime;
    }

    void HandlePlayerInteraction(float distToPlayer)
    {
        // PERSONAL SPACE
        if (distToPlayer < personalSpaceDistance)
        {
            if (currentState != AIState.Stopping)
                ChangeState(AIState.Stopping);

            agent.ResetPath();
            FaceTarget(targetPlayer.position);
            socialTimer += Time.deltaTime;
            return;
        }

        // CONVERSATION
        if (distToPlayer < conversationDistance)
        {
            socialTimer += Time.deltaTime;

            if (currentState != AIState.Socializing)
            {
                ChangeState(AIState.Socializing);
                socialTimer = 0f;
                stateTimer = socialWanderInterval;
                PickWanderPoint(targetPlayer.position, nearPlayerWanderRadius);
            }

            if (socialTimer < minSocialTime)
            {
                agent.ResetPath();
                FaceTarget(targetPlayer.position);
            }
            else
            {
                float distToWander = Vector3.Distance(transform.position, wanderTarget);
                if (distToWander < stopMovingThreshold)
                {
                    FaceTarget(targetPlayer.position);
                    if (stateTimer <= 0f)
                    {
                        PickWanderPoint(targetPlayer.position, nearPlayerWanderRadius);
                        stateTimer = socialWanderInterval;
                    }
                    agent.ResetPath();
                }
                else
                {
                    agent.speed = walkSpeed * 0.6f;
                    agent.SetDestination(wanderTarget);
                }
            }

            if (socialTimer > maxSocialTime)
            {
                ChangeState(AIState.Wandering);
                PickWanderPoint(transform.position, farWanderRadius);
                agent.speed = walkSpeed;
                agent.SetDestination(wanderTarget);
                hasApproachedPlayer = false;
                socialTimer = 0f;
            }

            return;
        }

        // APPROACH
        if (distToPlayer < approachDistance)
        {
            if (currentState != AIState.Approaching && currentState != AIState.Socializing)
            {
                ChangeState(AIState.Approaching);
                hasApproachedPlayer = true;
            }

            if (currentState == AIState.Approaching)
            {
                agent.speed = walkSpeed;
                agent.SetDestination(targetPlayer.position);
            }

            return;
        }

        // DETECTION RANGE
        if (distToPlayer < detectionRadius)
        {
            if (currentState == AIState.Wandering && !hasApproachedPlayer)
            {
                ChangeState(AIState.Approaching);
                hasApproachedPlayer = true;
            }
            else if (currentState == AIState.Approaching)
            {
                agent.speed = walkSpeed;
                agent.SetDestination(targetPlayer.position);
            }

            return;
        }

        // PLAYER LEFT RANGE
        if (currentState != AIState.Wandering)
            HandleSoloWandering();
    }

    void HandleSoloWandering()
    {
        if (currentState != AIState.Wandering)
        {
            ChangeState(AIState.Wandering);
            PickWanderPoint(transform.position, farWanderRadius);
            stateTimer = idleWanderInterval;
            socialTimer = 0f;
            hasApproachedPlayer = false;
        }

        float distToWander = Vector3.Distance(transform.position, wanderTarget);

        if (float.IsNaN(wanderTarget.x) || wanderTarget == Vector3.zero)
        {
            PickWanderPoint(transform.position, farWanderRadius);
            stateTimer = idleWanderInterval;
            return;
        }

        if (distToWander < stopMovingThreshold)
        {
            if (stateTimer <= 0f)
            {
                PickWanderPoint(transform.position, farWanderRadius);
                stateTimer = idleWanderInterval;
            }
            agent.ResetPath();
        }
        else
        {
            agent.speed = walkSpeed;
            agent.SetDestination(wanderTarget);
        }
    }

    void ChangeState(AIState newState)
    {
        if (currentState == newState) return;
        if (showDebugInfo)
            Debug.Log($"[ImpostorAI] State: {currentState} -> {newState}");
        currentState = newState;
    }

    // Uses network players + coordinates
    Transform FindNearestPlayer()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return null;

        Transform closest = null;
        float closestDist = detectionRadius;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            if (client.PlayerObject.gameObject == gameObject) continue;

            // Only track real player objects
            if (client.PlayerObject.GetComponent<PlayerController>() == null)
                continue;

            float d = Vector3.Distance(transform.position, client.PlayerObject.transform.position);
            if (d < closestDist)
            {
                closestDist = d;
                closest = client.PlayerObject.transform;
            }
        }

        return closest;
    }

    void PickWanderPoint(Vector3 centerPos, float radius)
    {
        float minDistance = 2f;
        int maxAttempts = 10;

        for (int i = 0; i < maxAttempts; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle * Random.Range(radius * 0.5f, radius);
            Vector3 candidate = centerPos + new Vector3(randomCircle.x, 0f, randomCircle.y);

            NavMeshHit hit;
            if (NavMesh.SamplePosition(candidate, out hit, 2f, NavMesh.AllAreas))
            {
                if (Vector3.Distance(transform.position, hit.position) >= minDistance)
                {
                    wanderTarget = hit.position;
                    if (showDebugInfo)
                        Debug.Log($"[ImpostorAI] New wander target: {wanderTarget:F1}");
                    return;
                }
            }
        }

        wanderTarget = centerPos;
    }

    void FaceTarget(Vector3 targetPos)
    {
        Vector3 direction = targetPos - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5f);
        }
    }

    // Uses existing-style raycast sensor for local steering
    void ApplyRaycastSensor()
    {
        if (!agent.hasPath) return;

        Vector3 desired = agent.desiredVelocity;
        desired.y = 0f;
        if (desired.sqrMagnitude < 0.0001f) return;

        desired.Normalize();

        Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;
        LayerMask maskToUse = obstacleMask.value == 0 ? groundMask : obstacleMask;

        if (Physics.Raycast(rayOrigin, desired, obstacleCheckDistance, maskToUse))
        {
            Vector3 right = Vector3.Cross(Vector3.up, desired);

            if (!Physics.Raycast(rayOrigin, right, obstacleCheckDistance, maskToUse))
                desired = right;
            else if (!Physics.Raycast(rayOrigin, -right, obstacleCheckDistance, maskToUse))
                desired = -right;
            else
                desired = agent.desiredVelocity.normalized;
        }

        if (desired.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(desired);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 8f);
        }

        if (showDebugInfo)
            Debug.DrawRay(rayOrigin, desired * 2f, Color.green, 0.1f);
    }

    void UpdateAnimations()
    {
        if (anim == null) return;

        anim.SetBool("IsGrounded", isGrounded);

        Vector3 worldVelocity = (transform.position - lastPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
        lastPosition = transform.position;

        Vector3 localVel = transform.InverseTransformDirection(worldVelocity);
        localVel.y = 0f;

        float targetForward = Mathf.Clamp(localVel.z / runSpeed, -1f, 1f);
        float targetRight = Mathf.Clamp(localVel.x / runSpeed, -1f, 1f);

        currentSpeed = Mathf.Lerp(currentSpeed, targetForward, Time.deltaTime * acceleration);
        currentDirection = Mathf.Lerp(currentDirection, targetRight, Time.deltaTime * acceleration);

        movementMagnitude = new Vector2(currentDirection, currentSpeed).magnitude;

        anim.SetFloat("Speed", currentSpeed);
        anim.SetBool("IsMoving", movementMagnitude > 0.05f);
        anim.SetFloat("Direction", currentDirection);
    }
}
