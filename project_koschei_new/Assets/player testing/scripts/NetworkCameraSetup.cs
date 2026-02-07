using Unity.Netcode;
using UnityEngine;
using Cinemachine;

public class NetworkCameraSetup : NetworkBehaviour
{
    [Header("Camera References")]
    [SerializeField] private GameObject mainCamera; // The Main Camera child object
    [SerializeField] private GameObject playerFollowCamera; // PlayerFollow virtual camera
    [SerializeField] private GameObject aimCamera; // Aim virtual camera
    [SerializeField] private AudioListener audioListener;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsOwner)
        {
            // LOCAL PLAYER - Enable everything
            Debug.Log($"[NetworkCameraSetup] LOCAL player {OwnerClientId} - enabling cameras");

            // Enable Main Camera
            if (mainCamera != null)
                mainCamera.SetActive(true);

            // Enable PlayerFollow Camera
            if (playerFollowCamera != null)
            {
                playerFollowCamera.SetActive(true);
                CinemachineVirtualCamera vCam = playerFollowCamera.GetComponent<CinemachineVirtualCamera>();
                if (vCam != null)
                    vCam.Priority = 10;
            }

            // Aim camera starts disabled, will be enabled when aiming
            if (aimCamera != null)
            {
                aimCamera.SetActive(false);
                CinemachineVirtualCamera vCam = aimCamera.GetComponent<CinemachineVirtualCamera>();
                if (vCam != null)
                    vCam.Priority = 11;
            }

            // Enable audio listener
            if (audioListener != null)
                audioListener.enabled = true;

            // Lock cursor for local player
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            // REMOTE PLAYER - Disable everything
            Debug.Log($"[NetworkCameraSetup] REMOTE player {OwnerClientId} - disabling cameras");

            // Disable Main Camera
            if (mainCamera != null)
                mainCamera.SetActive(false);

            // Disable PlayerFollow Camera
            if (playerFollowCamera != null)
            {
                playerFollowCamera.SetActive(false);
                CinemachineVirtualCamera vCam = playerFollowCamera.GetComponent<CinemachineVirtualCamera>();
                if (vCam != null)
                    vCam.Priority = 0;
            }

            // Disable Aim Camera
            if (aimCamera != null)
            {
                aimCamera.SetActive(false);
                CinemachineVirtualCamera vCam = aimCamera.GetComponent<CinemachineVirtualCamera>();
                if (vCam != null)
                    vCam.Priority = 0;
            }

            // Disable audio listener
            if (audioListener != null)
                audioListener.enabled = false;
        }
    }
}
