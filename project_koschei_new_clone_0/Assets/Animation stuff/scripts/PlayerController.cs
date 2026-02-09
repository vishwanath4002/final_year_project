using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [Header("Rotation")]
    public float rotationSmoothTime = 0.12f;
    private float yawSmoothVel;

    [Header("Movement")]
    public float walkSpeed = 5f;
    public float runSpeed = 10f;
    public float acceleration = 5f;

    [Header("Physics")]
    public float gravity = -9.81f;
    public float jumpHeight = 1.5f;

    [Header("References")]
    public Transform cameraTransform; // Assign this in Inspector! (child camera of the Player prefab)
    public Transform groundCheck;
    public LayerMask groundMask;
    public float groundDistance = 0.2f;

    [Header("Mouse Look")]
    public float mouseSensitivity = 100f;
    private float xRotation = 0f;

    [Header("Debug")]
    public bool debugMode = false;

    private CharacterController controller;
    private Animator anim;

    private Vector3 velocity;
    private Vector3 lastPosition;
    private bool isGrounded;

    private float currentSpeed = 0f;
    private float currentDirection = 0f;
    private float movementMagnitude = 0f;

    // --- NETCODE ADDITION: Use OnNetworkSpawn for initializing ownership stuff.
    public override void OnNetworkSpawn()
    {
        // Always enable/disable camera for the owner (works if ownership changes)
        if (cameraTransform != null)
            cameraTransform.gameObject.SetActive(IsOwner);

        // Only owner locks/hides cursor
        if (IsOwner)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        if (controller == null)
            Debug.LogError("PlayerController: No CharacterController found on the GameObject.");

        anim = GetComponentInChildren<Animator>();
        if (anim == null && debugMode)
            Debug.LogWarning("PlayerController: Animator not found in children. Anim parameters will be skipped.");

        if (cameraTransform == null && debugMode)
            Debug.LogWarning("PlayerController: cameraTransform is not assigned! Assign in Inspector to your player camera (child of prefab).");

        // Enable/disable camera also in Start (in addition to OnNetworkSpawn), in case object was already spawned by the time you join
        if (cameraTransform != null)
            cameraTransform.gameObject.SetActive(IsOwner);

        // Only owner locks/hides cursor (can safely do this in Start too)
        if (IsOwner)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        lastPosition = transform.position;
    }

    private void Update()
    {
        // --- FOR DEBUGGING: See which objects are the owner! ---
        if (debugMode)
        {
            Debug.Log($"[Player:{gameObject.name}] IsOwner={IsOwner}, OwnerClientId={NetworkObject.OwnerClientId}, LocalClientId={NetworkManager.Singleton.LocalClientId}");
        }

        // Only the owner can control movement/input and camera!
        if (!IsOwner) return;

        HandleMouseLook();
        HandleMovement();
        HandleGravityAndJump();
    }

    private void HandleMouseLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        // Rotate player: horizontal axis
        transform.Rotate(Vector3.up * mouseX);

        // Rotate camera: vertical axis
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -60f, 60f);
        if (cameraTransform != null)
            cameraTransform.localEulerAngles = new Vector3(xRotation, 0f, 0f);
    }

    private void HandleMovement()
    {
        if (groundCheck != null)
        {
            isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);
        }
        else
        {
            isGrounded = Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, 0.25f, groundMask);
        }
        if (anim != null) anim.SetBool("IsGrounded", isGrounded);

        float inputX = Input.GetAxis("Horizontal");
        float inputZ = Input.GetAxis("Vertical");
        bool isSprinting = Input.GetKey(KeyCode.LeftShift);

        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        Vector3 rawMove = right * inputX + forward * inputZ;
        rawMove.y = 0f;

        Vector3 move = rawMove;
        if (move.sqrMagnitude > 1f) move = move.normalized;

        float targetSpeed = isSprinting ? runSpeed : walkSpeed;
        Vector3 movementThisFrame = move * targetSpeed * Time.deltaTime;

        if (controller != null)
            controller.Move(movementThisFrame);

        // Velocity for animation
        Vector3 worldVelocity = Vector3.zero;
        if (Time.deltaTime > 0)
            worldVelocity = (transform.position - lastPosition) / Time.deltaTime;
        lastPosition = transform.position;

        Vector3 localVel = cameraTransform != null
            ? cameraTransform.InverseTransformDirection(worldVelocity)
            : transform.InverseTransformDirection(worldVelocity);
        localVel.y = 0f;

        float targetForward = Mathf.Clamp(localVel.z / runSpeed, -1f, 1f);
        float targetRight = Mathf.Clamp(localVel.x / runSpeed, -1f, 1f);

        currentSpeed = Mathf.Lerp(currentSpeed, targetForward, Time.deltaTime * acceleration);
        currentDirection = Mathf.Lerp(currentDirection, targetRight, Time.deltaTime * acceleration);
        movementMagnitude = new Vector2(currentDirection, currentSpeed).magnitude;

        if (anim != null)
        {
            anim.SetFloat("Speed", currentSpeed);
            anim.SetBool("IsMoving", movementMagnitude > 0.1f);
            anim.SetFloat("Direction", currentDirection);
        }

        if (debugMode)
        {
            Debug.DrawRay(transform.position + Vector3.up * 0.5f, move, Color.green);
            if (worldVelocity.sqrMagnitude > 0.0001f)
                Debug.DrawRay(transform.position + Vector3.up * 0.5f, worldVelocity.normalized, Color.blue);
        }
    }

    private void HandleGravityAndJump()
    {
        if (isGrounded && velocity.y < 0f)
            velocity.y = -2f;

        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            if (anim != null) anim.SetTrigger("Jump");
        }
        velocity.y += gravity * Time.deltaTime;
        if (controller != null)
            controller.Move(velocity * Time.deltaTime);
    }
}
