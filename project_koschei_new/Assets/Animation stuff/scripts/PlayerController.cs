using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 5f;
    public float runSpeed = 10f;
    public float acceleration = 5f;

    [Header("Physics")]
    public float gravity = -9.81f;
    public float jumpHeight = 1.5f;

    [Header("References")]
    public Transform cameraTransform;     // set in inspector or auto-assign to Camera.main
    public Transform groundCheck;         // optional, if not set we use raycast
    public LayerMask groundMask;
    public float groundDistance = 0.2f;

    [Header("Debug")]
    public bool debugMode = true;         // enable to print diagnostics

    private CharacterController controller;
    private Animator anim;

    private Vector3 velocity;
    private Vector3 lastPosition;
    private bool isGrounded;

    private float currentSpeed = 0f;
    private float currentDirection = 0f;
    private float movementMagnitude = 0f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        if (controller == null)
            Debug.LogError("PlayerController: No CharacterController found on the GameObject.");

        anim = GetComponentInChildren<Animator>();
        if (anim == null && debugMode)
            Debug.LogWarning("PlayerController: Animator not found in children. Anim parameters will be skipped.");

        if (cameraTransform == null)
        {
            if (Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
                if (debugMode) Debug.Log("PlayerController: cameraTransform not set — using Camera.main");
            }
            else if (debugMode)
            {
                Debug.LogWarning("PlayerController: No cameraTransform assigned and Camera.main is null.");
            }
        }

        lastPosition = transform.position;
    }

    void Update()
    {
        // Quick early-out if compile/runtime errors exist
        // (if you see exceptions in console fix them first)
        HandleMovement();
        HandleGravityAndJump();
    }

    private void HandleMovement()
    {
        // --- Ground check (safe)
        if (groundCheck != null)
        {
            isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);
        }
        else
        {
            // fallback: small raycast down from feet
            isGrounded = Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, 0.25f, groundMask);
        }

        if (anim != null) anim.SetBool("IsGrounded", isGrounded);

        // --- Read input
        float inputX = Input.GetAxis("Horizontal");
        float inputZ = Input.GetAxis("Vertical");
        bool isSprinting = Input.GetKey(KeyCode.LeftShift);

        // --- Movement vector relative to camera (safe fallback to world axes)
        Vector3 right, forward;
        if (cameraTransform != null)
        {
            right = cameraTransform.right;
            forward = cameraTransform.forward;
        }
        else
        {
            right = transform.right;
            forward = transform.forward;
        }

        Vector3 rawMove = right * inputX + forward * inputZ;
        rawMove.y = 0f;

        // if diagonal, normalize so diagonal speed == walkSpeed
        Vector3 move = rawMove;
        if (move.sqrMagnitude > 1f) move = move.normalized;

        float targetSpeed = isSprinting ? runSpeed : walkSpeed;
        Vector3 movementThisFrame = move * targetSpeed * Time.deltaTime;

        // --- Move the controller
        if (controller != null)
        {
            controller.Move(movementThisFrame);
        }

        // --- Velocity (based on displacement)
        Vector3 worldVelocity = Vector3.zero;
        if (Time.deltaTime > 0)
        {
            worldVelocity = (transform.position - lastPosition) / Time.deltaTime;
        }
        lastPosition = transform.position;

        // convert to camera local for animation parameter alignment
        Vector3 localVel = Vector3.zero;
        if (cameraTransform != null)
            localVel = cameraTransform.InverseTransformDirection(worldVelocity);
        else
            localVel = transform.InverseTransformDirection(worldVelocity);

        localVel.y = 0f;

        // normalize with runSpeed (so values in [-1,1])
        float targetForward = Mathf.Clamp(localVel.z / runSpeed, -1f, 1f);
        float targetRight = Mathf.Clamp(localVel.x / runSpeed, -1f, 1f);

        // Smoothed parameters
        currentSpeed = Mathf.Lerp(currentSpeed, targetForward, Time.deltaTime * acceleration);
        currentDirection = Mathf.Lerp(currentDirection, targetRight, Time.deltaTime * acceleration);
        movementMagnitude = new Vector2(currentDirection, currentSpeed).magnitude;


        // update animator safely
        if (anim != null)
        {
            anim.SetFloat("Speed", currentSpeed);
            anim.SetBool("IsMoving", movementMagnitude > 0.1f);
            anim.SetFloat("Direction", currentDirection);
        }

        // debug output
        if (debugMode)
        {
            Debug.DrawRay(transform.position + Vector3.up * 0.5f, move, Color.green); // intended movement
            Debug.DrawRay(transform.position + Vector3.up * 0.5f, worldVelocity.normalized, Color.blue); // actual velocity direction
            if (Time.frameCount % 10 == 0) // reduce spam
            {
                Debug.Log($"Inputs H:{inputX:F2} V:{inputZ:F2} move:{move.magnitude:F2} targetSpeed:{targetSpeed:F2} planarVel:{new Vector2(localVel.x, localVel.z).magnitude:F2} grounded:{isGrounded}");
            }
        }
    }

    private void HandleGravityAndJump()
    {
        // Reset gravity when grounded
        if (isGrounded && velocity.y < 0f)
            velocity.y = -2f;

        // Jump
        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            if (anim != null) anim.SetTrigger("Jump");
        }

        // Apply gravity
        velocity.y += gravity * Time.deltaTime;
        if (controller != null)
            controller.Move(velocity * Time.deltaTime);
    }
}
