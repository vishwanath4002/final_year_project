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

    [Header("Target Group Settings")]
    public float groupArrivalDistance = 10f; // How close to get to group center before socializing
    
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

    // STATE MACHINE
    private enum AIState
    {
        MovingToGroup,   // NEW: Going to target group (ignores other players)
        Wandering,       // Random wandering (when no target)
        Approaching,     // Approaching a specific player
        Socializing,     // Near players, having conversation
        Stopping,        // Very close, stopped
        Leaving          // NEW: Conversation done, moving away
    }

    private AIState currentState = AIState.MovingToGroup;
    private Transform targetPlayer;
    private Vector3 wanderTarget;
    private float stateTimer;
    private float socialTimer;
    private bool hasApproachedPlayer = false;
    private bool isInitialized = false;

    // TARGET GROUP TRACKING
    private Vector3 targetGroupCenter = Vector3.zero;
    private bool hasTargetGroup = false;
    private bool hasReachedGroup = false;
    
    // NEW: Track which players are in target group (to ignore others)
    private HashSet<string> targetGroupPlayerNames = new HashSet<string>();

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

    /// <summary>
    /// Call this from ImpostorAlienSpawner to tell impostor where to go
    /// NEW: Also receives target group member names to ignore other groups
    /// </summary>
    public void SetTargetGroup(Vector3 groupCenter, string[] groupMemberNames = null)
    {
        targetGroupCenter = groupCenter;
        hasTargetGroup = true;
        hasReachedGroup = false;

        // Store target group member names
        targetGroupPlayerNames.Clear();
        if (groupMemberNames != null)
        {
            foreach (string name in groupMemberNames)
            {
                if (!string.IsNullOrEmpty(name))
                    targetGroupPlayerNames.Add(name);
            }
        }

        if (showDebugInfo)
        {
            Debug.Log($"[ImpostorAI] 🎯 Target group set at {groupCenter:F1}");
            Debug.Log($"[ImpostorAI] Distance: {Vector3.Distance(transform.position, groupCenter):F1}m");
            Debug.Log($"[ImpostorAI] Target group members: {string.Join(", ", targetGroupPlayerNames)}");
        }

        // Immediately start moving toward group
        if (isInitialized)
        {
            ChangeState(AIState.MovingToGroup);
            agent.speed = walkSpeed;
            agent.SetDestination(targetGroupCenter);
        }
    }

    void InitializeAI()
    {
        if (isInitialized) return;

        stateTimer = idleWanderInterval;
        socialTimer = 0f;

        // If we have a target group, go there immediately
        if (hasTargetGroup)
        {
            ChangeState(AIState.MovingToGroup);
            agent.speed = walkSpeed;
            agent.SetDestination(targetGroupCenter);
            if (showDebugInfo)
                Debug.Log($"[ImpostorAI] INITIALIZED - Moving to target group at {targetGroupCenter:F1}");
        }
        else
        {
            // No target group yet, just wander
            PickWanderPoint(transform.position, farWanderRadius);
            agent.speed = walkSpeed;
            agent.SetDestination(wanderTarget);
            ChangeState(AIState.Wandering);
            if (showDebugInfo)
                Debug.Log($"[ImpostorAI] INITIALIZED - No target, wandering");
        }

        isInitialized = true;
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
            Debug.Log($"[ImpostorAI] State={currentState} Pos={transform.position:F1} Target={(targetPlayer ? targetPlayer.name : "NONE")} HasGroup={hasTargetGroup} ReachedGroup={hasReachedGroup}");
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
        // PRIORITY 1: Moving to target group (IGNORES OTHER PLAYERS)
        if (hasTargetGroup && !hasReachedGroup && currentState != AIState.Leaving)
        {
            HandleMovingToGroup();
            return; // Don't check for other players while moving to target
        }

        // PRIORITY 2: Check for nearby players for social interaction (only after reaching target)
        targetPlayer = FindNearestTargetGroupPlayer();
        
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

    void HandleMovingToGroup()
    {
        if (currentState != AIState.MovingToGroup)
        {
            ChangeState(AIState.MovingToGroup);
            agent.speed = walkSpeed;
            agent.SetDestination(targetGroupCenter);
        }

        float distToGroup = Vector3.Distance(transform.position, targetGroupCenter);

        if (showDebugInfo && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[ImpostorAI] 🚶 Moving to group... Distance: {distToGroup:F1}m (ignoring other players)");
        }

        // Check if we've arrived at the group area
        if (distToGroup < groupArrivalDistance)
        {
            hasReachedGroup = true;
            ChangeState(AIState.Wandering); // Switch to wandering/socializing mode
            
            if (showDebugInfo)
            {
                Debug.Log($"[ImpostorAI] ✅ ARRIVED at target group!");
                Debug.Log($"[ImpostorAI] Now switching to social behavior...");
            }

            // Start wandering near the group center
            PickWanderPoint(targetGroupCenter, nearPlayerWanderRadius);
            agent.SetDestination(wanderTarget);
        }
        else
        {
            // Keep moving toward group (ignore all other players)
            agent.speed = walkSpeed;
            agent.SetDestination(targetGroupCenter);
        }
    }

    void HandlePlayerInteraction(float distToPlayer)
    {
        // Don't socialize if we're supposed to be leaving
        if (currentState == AIState.Leaving)
            return;

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
        if (currentState != AIState.Wandering && currentState != AIState.MovingToGroup)
            HandleSoloWandering();
    }

    void HandleSoloWandering()
    {
        // Don't interfere if we're moving to group or leaving
        if (currentState == AIState.MovingToGroup || currentState == AIState.Leaving)
            return;

        if (currentState != AIState.Wandering)
        {
            ChangeState(AIState.Wandering);

            // If we have a target group and reached it, wander near it
            if (hasTargetGroup && hasReachedGroup)
            {
                PickWanderPoint(targetGroupCenter, nearPlayerWanderRadius);
            }
            else
            {
                PickWanderPoint(transform.position, farWanderRadius);
            }

            stateTimer = idleWanderInterval;
            socialTimer = 0f;
            hasApproachedPlayer = false;
        }

        float distToWander = Vector3.Distance(transform.position, wanderTarget);

        if (float.IsNaN(wanderTarget.x) || wanderTarget == Vector3.zero)
        {
            if (hasTargetGroup && hasReachedGroup)
                PickWanderPoint(targetGroupCenter, nearPlayerWanderRadius);
            else
                PickWanderPoint(transform.position, farWanderRadius);

            stateTimer = idleWanderInterval;
            return;
        }

        if (distToWander < stopMovingThreshold)
        {
            if (stateTimer <= 0f)
            {
                if (hasTargetGroup && hasReachedGroup)
                    PickWanderPoint(targetGroupCenter, nearPlayerWanderRadius);
                else
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

    /// <summary>
    /// Tell impostor to leave the area (conversation ended)
    /// </summary>
    public void LeaveArea()
    {
        if (showDebugInfo)
            Debug.Log($"[ImpostorAI] 👋 Leaving area...");

        ChangeState(AIState.Leaving);
        hasTargetGroup = false;
        hasReachedGroup = false;

        // Pick a far away point to walk to
        PickWanderPoint(transform.position, farWanderRadius * 2f);
        agent.speed = walkSpeed;
        agent.SetDestination(wanderTarget);
    }

    void ChangeState(AIState newState)
    {
        if (currentState == newState) return;

        if (showDebugInfo)
            Debug.Log($"[ImpostorAI] State: {currentState} -> {newState}");

        currentState = newState;
    }

    /// <summary>
    /// NEW: Only find players in TARGET GROUP (ignore other players)
    /// </summary>
    Transform FindNearestTargetGroupPlayer()
    {
        // While moving to group, don't detect ANY players
        if (!hasReachedGroup)
            return null;

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return null;

        Transform closest = null;
        float closestDist = detectionRadius;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            if (client.PlayerObject.gameObject == gameObject) continue;

            // Only track real player objects
            if (client.PlayerObject.GetComponent<CharacterController>() == null)
                continue;

            // NEW: Check if player is in target group
            string playerName = client.PlayerObject.name;
            
            // If we have target group info, only interact with those players
            if (targetGroupPlayerNames.Count > 0)
            {
                bool isInTargetGroup = false;
                foreach (string targetName in targetGroupPlayerNames)
                {
                    if (playerName.Contains(targetName))
                    {
                        isInTargetGroup = true;
                        break;
                    }
                }

                if (!isInTargetGroup)
                {
                    // This player is NOT in target group, ignore them
                    if (showDebugInfo && Time.frameCount % 120 == 0)
                        Debug.Log($"[ImpostorAI] Ignoring {playerName} (not in target group)");
                    continue;
                }
            }

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
