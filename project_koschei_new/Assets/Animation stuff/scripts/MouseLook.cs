using UnityEngine;

public class MouseLook : MonoBehaviour
{
    public Transform cameraHolder;   // Drag your CameraHolder here
    public float mouseSensitivity = 100f;

    float xRotation = 0f;

    void Start()
    {
        // Lock cursor in the middle of the screen
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        // Rotate player left/right (yaw)
        transform.Rotate(Vector3.up * mouseX);

        // Rotate camera up/down (pitch)
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -80f, 80f); // prevent flipping

        cameraHolder.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }
}
