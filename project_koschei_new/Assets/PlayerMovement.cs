using Unity.Netcode;
using UnityEngine;

public class PlayerMovement : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private Transform playerCamera;

    private float verticalRotation = 0f;
    private CharacterController controller;

    void Awake()
    {
        // Get existing CharacterController or add one
        controller = GetComponent<CharacterController>();
        if (controller == null)
        {
            controller = gameObject.AddComponent<CharacterController>();
            Debug.Log("CharacterController added in Awake");
        }
    }

    void Start()
    {
        // Double-check controller exists
        if (controller == null)
        {
            Debug.LogError("CharacterController is still null!");
            return;
        }

        // Only lock cursor for the local player
        if (IsOwner)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // Disable camera for non-owned players
        if (playerCamera != null && !IsOwner)
        {
            playerCamera.gameObject.SetActive(false);
        }
    }

    void Update()
    {
        // Only process input for the player you own
        if (!IsOwner) return;

        // Safety check
        if (controller == null)
        {
            Debug.LogError("Controller is null in Update!");
            return;
        }

        HandleMovement();
        HandleMouseLook();
    }

    void HandleMovement()
    {
        float horizontal = Input.GetAxis("Horizontal"); // A/D or Left/Right arrows
        float vertical = Input.GetAxis("Vertical");     // W/S or Up/Down arrows

        Vector3 movement = transform.right * horizontal + transform.forward * vertical;
        controller.Move(movement * moveSpeed * Time.deltaTime);

        // Simple gravity
        controller.Move(Vector3.down * 9.81f * Time.deltaTime);
    }

    void HandleMouseLook()
    {
        // Safety check for camera
        if (playerCamera == null)
        {
            Debug.LogError("Player camera is not assigned!");
            return;
        }

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // Rotate player body left/right
        transform.Rotate(Vector3.up * mouseX);

        // Rotate camera up/down
        verticalRotation -= mouseY;
        verticalRotation = Mathf.Clamp(verticalRotation, -90f, 90f);
        playerCamera.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
    }
}
