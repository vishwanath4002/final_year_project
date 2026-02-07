using Unity.Netcode;
using UnityEngine;
using Cinemachine;

public class NetworkCameraSetup : NetworkBehaviour
{
    [SerializeField] private CinemachineVirtualCamera aimCamera;
    [SerializeField] private GameObject playerFollowCamera; // Main cinemachine camera
    [SerializeField] private AudioListener audioListener;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsOwner)
        {
            // This is the local player - enable cameras
            Debug.Log($"[{OwnerClientId}] Setting up LOCAL player camera");

            if (aimCamera != null)
            {
                aimCamera.Priority = 11; // High priority for local player
                aimCamera.gameObject.SetActive(true);
            }

            if (playerFollowCamera != null)
            {
                // Set priority on the main virtual camera
                CinemachineVirtualCamera mainVCam = playerFollowCamera.GetComponent<CinemachineVirtualCamera>();
                if (mainVCam != null)
                {
                    mainVCam.Priority = 10;
                }
                playerFollowCamera.SetActive(true);
            }

            // Enable audio listener for local player only
            if (audioListener != null)
            {
                audioListener.enabled = true;
            }
        }
        else
        {
            // This is a remote player - disable cameras
            Debug.Log($"[{OwnerClientId}] Setting up REMOTE player camera (disabled)");

            if (aimCamera != null)
            {
                aimCamera.Priority = 0;
                aimCamera.gameObject.SetActive(false);
            }

            if (playerFollowCamera != null)
            {
                CinemachineVirtualCamera mainVCam = playerFollowCamera.GetComponent<CinemachineVirtualCamera>();
                if (mainVCam != null)
                {
                    mainVCam.Priority = 0;
                }
                playerFollowCamera.SetActive(false);
            }

            // Disable audio listener for remote players
            if (audioListener != null)
            {
                audioListener.enabled = false;
            }
        }
    }
}
