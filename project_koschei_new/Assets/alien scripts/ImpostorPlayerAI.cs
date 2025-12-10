using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class ImpostorPlayerAI : NetworkBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 3f;
    public float runSpeed = 5f;
    public float acceleration = 5f;

    [Header("Physics")]
    public float gravity = -9.81f;
    public float groundedGravity = -5f;

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

    [Header("Obstacle Avoidance")]
    public LayerMask obstacleMask;       // separate from ground if you like
    public float obstacleCheckDistance = 1.0f;

    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool verboseLogging = false;

    private CharacterController controller;
    private Animator anim;
    private Vector3 velocity;
    private Vector3 lastPosition;
    private Vector3 lastLoggedPosition;
    private bool isGrounded;

    private float currentSpeed = 0f;
    private float currentDirection = 0f;
    private float movementMagnitude = 0f;

    private enum AIState
    {
        Wandering,
        Approaching,
        Socializing,
        Stopping
    }

    private AIState currentState = AIState.Wandering;
    private Transform targetPlayer;
    private Vector3 wanderTarget;
    private float stateTimer;
    private float socialTimer;
    private bool hasApproachedPlayer = false;
    private bool isInitialized = false;
    private int framesSinceLastMove = 0;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        if (controller == null)
        {
            Debug.LogError("ImpostorPlayerAI: CharacterController not found!");
            enabled = false;
            return;
        }

        Debug.Log($"[ImpostorAI] CharacterController found. Enabled: {controller.enabled}, Height: {controller.height}, Radius: {controller.radius}, Center: {controller.center}");

        anim = GetComponentInChildren<Animator>();
        if (anim == null)
            Debug.LogWarning("ImpostorPlayerAI: Animator not found in children.");

        lastPosition = transform.position;
        lastLoggedPosition = transform.position;

        if (IsSpawned && IsServer)
        {
            InitializeAI();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        Debug.Log($"[ImpostorAI] OnNetworkSpawn called. IsServer: {IsServer}, IsClient: {IsClient}");

        if (IsServer && !isInitialized)
        {
            Invoke(nameof(InitializeAI), 0.2f);
        }
    }

    void InitializeAI()
    {
        if (isInitialized)
        {
            Debug.LogWarning("[ImpostorAI] Already initialized, skipping.");
            return;
        }

        stateTimer = idleWanderInterval;
        socialTimer = 0f;

        if (controller != null && controller.enabled)
        {
            controller.Move(Vector3.zero);
            Debug.Log("[ImpostorAI] CharacterController physics initialized with Move(Vector3.zero)");
        }

        PickWanderPoint(transform.position, farWanderRadius);
        isInitialized = true;

        Debug.Log($"[ImpostorAI] INITIALIZED at {transform.position:F1}, State: {currentState}, WanderTarget: {wanderTarget:F1}, Distance: {Vector3.Distance(transform.position, wanderTarget):F1}");
    }

    void Update()
    {
        if (!IsServer)
        {
            if (verboseLogging && Time.frameCount % 300 == 0)
                Debug.Log("[ImpostorAI] Not server, skipping Update");
            return;
        }

        if (!isInitialized)
        {
            if (verboseLogging && Time.frameCount % 300 == 0)
                Debug.Log("[ImpostorAI] Not initialized yet, skipping Update");
            return;
        }

        if (controller == null)
        {
            Debug.LogError("[ImpostorAI] Controller is NULL!");
            return;
        }

        if (!controller.enabled)
        {
            Debug.LogWarning("[ImpostorAI] Controller is DISABLED!");
            return;
        }

        float distMoved = Vector3.Distance(transform.position, lastLoggedPosition);
        if (distMoved < 0.001f)
        {
            framesSinceLastMove++;
        }
        else
        {
            framesSinceLastMove = 0;
            lastLoggedPosition = transform.position;
        }

        if (framesSinceLastMove > 300 && currentState == AIState.Wandering)
        {
            Debug.LogWarning($"[ImpostorAI] STUCK. Forcing new wander target.");
            framesSinceLastMove = 0;
            PickWanderPoint(transform.position, farWanderRadius);
        }

        CheckGrounded();
        UpdateAIBehavior();
        HandleGravity();
        UpdateAnimations();

        if (showDebugInfo && Time.frameCount % 120 == 0)
        {
            Debug.Log($"[ImpostorAI] State: {currentState} | Player: {(targetPlayer ? targetPlayer.name : "NONE")} | Pos: {transform.position:F1} | Wander: {wanderTarget:F1} | Dist: {Vector3.Distance(transform.position, wanderTarget):F1} | Grounded: {isGrounded} | StateTimer: {stateTimer:F1}");
        }

        if (verboseLogging)
        {
            Debug.Log($"[ImpostorAI VERBOSE] Frame: {Time.frameCount}, Pos: {transform.position:F2}, State: {currentState}, DistToWander: {Vector3.Distance(transform.position, wanderTarget):F2}");
        }
    }

    void CheckGrounded()
    {
        if (groundCheck != null)
        {
            isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);
        }
        else
        {
            isGrounded = Physics.Raycast(
                transform.position + Vector3.up * 0.1f,
                Vector3.down,
                0.3f,
                groundMask
            );
        }

        if (verboseLogging && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[ImpostorAI] Ground check: {isGrounded}");
        }
    }

    void UpdateAIBehavior()
    {
        targetPlayer = FindNearestPlayer();

        if (targetPlayer != null)
        {
            float distToPlayer = Vector3.Distance(transform.position, targetPlayer.position);

            if (verboseLogging && Time.frameCount % 60 == 0)
            {
                Debug.Log($"[ImpostorAI] Player detected: {targetPlayer.name}, Distance: {distToPlayer:F1}");
            }

            HandlePlayerInteraction(distToPlayer);
        }
        else
        {
            if (verboseLogging && Time.frameCount % 60 == 0)
            {
                Debug.Log("[ImpostorAI] No player detected, solo wandering");
            }

            HandleSoloWandering();
        }

        stateTimer -= Time.deltaTime;
    }

    void HandlePlayerInteraction(float distToPlayer)
    {
        if (distToPlayer < personalSpaceDistance)
        {
            if (currentState != AIState.Stopping)
            {
                ChangeState(AIState.Stopping);
                Debug.Log("[ImpostorAI] Entered personal space - stopping");
            }
            FaceTarget(targetPlayer.position);
            socialTimer += Time.deltaTime;
            return;
        }

        if (distToPlayer < conversationDistance)
        {
            socialTimer += Time.deltaTime;

            if (currentState != AIState.Socializing)
            {
                ChangeState(AIState.Socializing);
                socialTimer = 0f;
                stateTimer = socialWanderInterval;
                PickWanderPoint(targetPlayer.position, nearPlayerWanderRadius);
                Debug.Log("[ImpostorAI] Started socializing");
            }

            if (socialTimer < minSocialTime)
            {
                // new: sometimes stand, sometimes gently follow
                if (Random.value < 0.5f)
                {
                    FaceTarget(targetPlayer.position);
                }
                else
                {
                    MoveTowards(targetPlayer.position, walkSpeed * 0.5f);
                }
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
                }
                else
                {
                    MoveTowards(wanderTarget, walkSpeed * 0.6f);
                }
            }

            if (socialTimer > maxSocialTime)
            {
                Debug.Log("[ImpostorAI] Social time exceeded, wandering off");
                ChangeState(AIState.Wandering);
                PickWanderPoint(transform.position, farWanderRadius);
                stateTimer = idleWanderInterval;
                hasApproachedPlayer = false;
                socialTimer = 0f;
            }
            return;
        }

        if (distToPlayer < approachDistance)
        {
            if (currentState != AIState.Approaching && currentState != AIState.Socializing)
            {
                ChangeState(AIState.Approaching);
                hasApproachedPlayer = true;
                Debug.Log("[ImpostorAI] Approaching player");
            }

            if (currentState == AIState.Approaching)
            {
                MoveTowards(targetPlayer.position, walkSpeed);
            }
            return;
        }

        if (distToPlayer < detectionRadius)
        {
            if (currentState == AIState.Wandering)
            {
                // new: stronger initial approach
                if (!hasApproachedPlayer)
                {
                    if (Random.value < 0.8f * Time.deltaTime)
                    {
                        ChangeState(AIState.Approaching);
                        hasApproachedPlayer = true;
                        Debug.Log("[ImpostorAI] First time seeing player, approaching");
                    }
                }
                else
                {
                    if (Random.value < 0.15f * Time.deltaTime)
                    {
                        ChangeState(AIState.Approaching);
                        Debug.Log("[ImpostorAI] Approaching player again");
                    }
                }
            }
            else if (currentState == AIState.Approaching)
            {
                MoveTowards(targetPlayer.position, walkSpeed);
            }
            return;
        }

        if (currentState != AIState.Wandering)
        {
            Debug.Log("[ImpostorAI] Player left range, resuming wandering");
            HandleSoloWandering();
        }
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
            Debug.LogWarning("[ImpostorAI] Invalid wander target, picking new one");
            return;
        }

        if (distToWander < stopMovingThreshold)
        {
            if (stateTimer <= 0f)
            {
                PickWanderPoint(transform.position, farWanderRadius);
                stateTimer = idleWanderInterval;
                Debug.Log("[ImpostorAI] Reached wander point, picking new target");
            }
        }
        else if (distToWander < arrivalThreshold)
        {
            MoveTowards(wanderTarget, walkSpeed * 0.6f);
        }
        else
        {
            MoveTowards(wanderTarget, walkSpeed);
        }
    }

    void ChangeState(AIState newState)
    {
        if (currentState == newState) return;

        Debug.Log($"[ImpostorAI] State: {currentState} -> {newState}");
        currentState = newState;
    }

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
            Vector3 potentialTarget = centerPos + new Vector3(randomCircle.x, 0f, randomCircle.y);
            potentialTarget.y = centerPos.y;

            if (Vector3.Distance(transform.position, potentialTarget) >= minDistance)
            {
                wanderTarget = potentialTarget;

                Debug.Log($"[ImpostorAI] New wander target: {wanderTarget:F1}");
                Debug.DrawLine(transform.position, wanderTarget, Color.cyan, 3f);
                return;
            }
        }

        Vector2 fallbackCircle = Random.insideUnitCircle * radius;
        wanderTarget = centerPos + new Vector3(fallbackCircle.x, 0f, fallbackCircle.y);
        wanderTarget.y = centerPos.y;

        Debug.LogWarning($"[ImpostorAI] Used fallback wander target: {wanderTarget:F1}");
    }

    void FaceTarget(Vector3 targetPos)
    {
        Vector3 direction = targetPos - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                Time.deltaTime * 5f
            );
        }
    }

    void MoveTowards(Vector3 target, float speed)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;

        float distance = direction.magnitude;
        if (distance < 0.1f)
        {
            if (verboseLogging)
                Debug.Log("[ImpostorAI] Too close to target, not moving");
            return;
        }

        direction.Normalize();

        // --- Obstacle avoidance ray ---
        Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;
        LayerMask maskToUse = obstacleMask.value == 0 ? groundMask : obstacleMask;

        if (Physics.Raycast(rayOrigin, direction, out RaycastHit hit, obstacleCheckDistance, maskToUse))
        {
            // try right, then left
            Vector3 avoidDirRight = Vector3.Cross(Vector3.up, direction);
            if (!Physics.Raycast(rayOrigin, avoidDirRight, obstacleCheckDistance, maskToUse))
            {
                direction = avoidDirRight;
            }
            else
            {
                Vector3 avoidDirLeft = -avoidDirRight;
                if (!Physics.Raycast(rayOrigin, avoidDirLeft, obstacleCheckDistance, maskToUse))
                {
                    direction = avoidDirLeft;
                }
                else
                {
                    if (verboseLogging)
                        Debug.Log("[ImpostorAI] Obstacle ahead, no clear side, not moving this frame");
                    return;
                }
            }
        }

        // Rotate toward move direction
        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                Time.deltaTime * 8f
            );
        }

        Vector3 posBeforeMove = transform.position;

        if (isGrounded)
        {
            bool moveSuccess = controller.SimpleMove(direction * speed);
            Vector3 posAfterMove = transform.position;
            float actualDistance = Vector3.Distance(posBeforeMove, posAfterMove);

            if (showDebugInfo && Time.frameCount % 60 == 0)
            {
                Debug.Log($"[ImpostorAI] SimpleMove. Success: {moveSuccess}, Speed: {speed:F1}, DistMoved: {actualDistance:F3}, TargetDist: {distance:F1}");
            }
        }
        else
        {
            Vector3 movement = direction * speed * Time.deltaTime;
            controller.Move(movement);

            if (verboseLogging)
            {
                Debug.Log($"[ImpostorAI] Move (not grounded): {movement:F3}");
            }
        }

        if (showDebugInfo)
        {
            Debug.DrawRay(rayOrigin, direction * 2f, Color.green, 0.1f);
        }
    }

    void HandleGravity()
    {
        if (!isGrounded)
        {
            velocity.y += gravity * Time.deltaTime;
            controller.Move(velocity * Time.deltaTime);
        }
        else
        {
            velocity.y = groundedGravity * Time.deltaTime;
        }
    }

    void UpdateAnimations()
    {
        if (anim == null) return;

        anim.SetBool("IsGrounded", isGrounded);

        Vector3 worldVelocity = Vector3.zero;
        if (Time.deltaTime > 0)
        {
            worldVelocity = (transform.position - lastPosition) / Time.deltaTime;
        }
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

    [ContextMenu("Toggle Verbose Logging")]
    void ToggleVerboseLogging()
    {
        verboseLogging = !verboseLogging;
        Debug.Log($"[ImpostorAI] Verbose logging: {verboseLogging}");
    }

    [ContextMenu("Force New Wander Target")]
    void ForceNewWanderTarget()
    {
        if (!IsServer)
        {
            Debug.LogWarning("Must be server");
            return;
        }

        PickWanderPoint(transform.position, farWanderRadius);
        Debug.Log($"[ImpostorAI] Manually forced new wander target: {wanderTarget:F1}");
    }

    void OnDrawGizmos()
    {
        if (!showDebugInfo) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, approachDistance);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, conversationDistance);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, personalSpaceDistance);

        if (wanderTarget != Vector3.zero)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(wanderTarget, 0.5f);
            Gizmos.DrawLine(transform.position, wanderTarget);
        }

        if (targetPlayer != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(transform.position, targetPlayer.position);
        }
    }
}
