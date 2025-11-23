using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
        [Header("Rotation")]
        public float rotationSmoothTime = 0.12f;  // smoothing for body rotation
        private float yawSmoothVel;

        [Header("Movement")]
        public float walkSpeed = 5f;
        public float runSpeed = 10f;
        public float acceleration = 5f;

        [Header("Physics")]
        public float gravity = -9.81f;
        public float jumpHeight = 1.5f;

        [Header("References")]
        public Transform cameraTransform;        // set in inspector or auto-assign to Camera.main
        public Transform groundCheck;            // optional, if not set we use raycast
        public LayerMask groundMask;
        public float groundDistance = 0.2f;

        [Header("Debug")]
        public bool debugMode = true;            // enable to print diagnostics

        // --- Mouse look ---
        private float yaw;
        private float pitch;
        public float mouseSensitivity = 3f;        // horizontal rotation sensitivity
        public float mouseSmoothing = 0.05f;          // smoothing factor
        private Vector2 currentMouseDelta;
        private Vector2 mouseDeltaVel;

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
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        
                
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
                HandleMouseLook();
                HandleMovement();
                HandleGravityAndJump();
        }

        private void HandleMouseLook()
        {
            // --- Read raw mouse input ---
            Vector2 rawMouse = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * mouseSensitivity;

            // Smooth it out
            currentMouseDelta = Vector2.SmoothDamp(currentMouseDelta, rawMouse, ref mouseDeltaVel, mouseSmoothing);

            // --- Apply yaw rotation to player (horizontal mouse) ---
            yaw += currentMouseDelta.x;
            float smoothedYaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, yaw, ref yawSmoothVel, rotationSmoothTime);
            transform.rotation = Quaternion.Euler(0f, smoothedYaw, 0f);

            // --- Apply pitch to camera (vertical mouse) ---
            pitch -= currentMouseDelta.y;
            pitch = Mathf.Clamp(pitch, -60f, 60f);

            if (cameraTransform != null)
                cameraTransform.localEulerAngles = new Vector3(pitch, 0f, 0f);
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

        // Move relative to the player's facing direction (player's transform is rotated by mouse)
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        Vector3 rawMove = right * inputX + forward * inputZ;
        rawMove.y = 0f;

        // if diagonal, normalize so diagonal speed == walkSpeed
        Vector3 move = rawMove;
        if (move.sqrMagnitude > 1f) move = move.normalized;

        // --- movement
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
            if (worldVelocity.sqrMagnitude > 0.0001f)
                Debug.DrawRay(transform.position + Vector3.up * 0.5f, worldVelocity.normalized, Color.blue); // actual velocity direction
            if (Time.frameCount % 10 == 0) // reduce spam
            {
                //Debug.Log($"Inputs H:{inputX:F2} V:{inputZ:F2} move:{move.magnitude:F2} targetSpeed:{targetSpeed:F2} planarVel:{new Vector2(localVel.x, localVel.z).magnitude:F2} grounded:{isGrounded}");
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
