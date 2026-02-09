using Unity.Netcode;
using UnityEngine;

public class MouseLook : NetworkBehaviour
{
    public Transform cameraHolder;
    public float mouseSensitivity = 100f;
    private float xRotation = 0f;

    void Start()
    {
        // Only the owner has camera/mouse control.
        if (!IsOwner)
        {
            if (cameraHolder != null) cameraHolder.gameObject.SetActive(false);
            enabled = false;
            return;
        }
        // Lock cursor for local player only
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        transform.Rotate(Vector3.up * mouseX);

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -80f, 80f);

        if (cameraHolder != null)
            cameraHolder.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }
}
