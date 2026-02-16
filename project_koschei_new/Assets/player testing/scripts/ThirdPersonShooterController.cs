using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cinemachine;
using StarterAssets;
using UnityEngine.InputSystem;
using UnityEngine.Animations.Rigging;
using Unity.Netcode;


/// <summary>
/// Multiplayer-ready third person shooter controller
/// Handles aiming, shooting, and interaction systems with proper network synchronization
/// INCLUDES AIM TARGET POSITION SYNCHRONIZATION FOR CORRECT REMOTE PLAYER AIMING
/// </summary>
public class ThirdPersonShooterController : NetworkBehaviour
{
    [Header("Animation Rig")]
    [SerializeField] private Rig aimRig;
    [SerializeField] private Transform aimTargetTransform; // The transform that the rig aims at (debug sphere)


    [Header("Camera Settings")]
    [SerializeField] private CinemachineVirtualCamera aimVirtualCamera;
    [SerializeField] private float normalSensitivity = 1f;
    [SerializeField] private float aimSensitivity = 0.5f;


    [Header("Aiming")]
    [SerializeField] private LayerMask aimColliderLayerMask = ~0;
    [SerializeField] private Transform debugTransform;


    [Header("VFX")]
    [SerializeField] private GameObject vfxHitGreen;
    [SerializeField] private GameObject vfxHitRed;


    [Header("Weapon Settings")]
    [SerializeField] private float damagePerShot = 35f;
    [SerializeField] private float fireRate = 0.1f;
    [SerializeField] private int magazineSize = 30;
    [SerializeField] private float reloadTime = 2f;


    [Header("Interaction Settings")]
    [SerializeField] private float interactRange = 5f;
    [SerializeField] private LayerMask interactLayerMask = ~0;


    // Network synced variables
    private NetworkVariable<int> currentAmmo = new NetworkVariable<int>(
        30,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );


    private NetworkVariable<bool> isReloading = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );


    // CRITICAL: Network synced aim state and target position
    private NetworkVariable<bool> isAiming = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );


    private NetworkVariable<Vector3> networkAimTargetPosition = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );


    // Component references
    private ThirdPersonController thirdPersonController;
    private StarterAssetsInputs starterAssetsInputs;
    private PlayerInventory playerInventory;
    private Animator animator;


    // State variables
    private float aimRigWeight = 0f;
    private float nextTimeToFire = 0f;
    private int bulletsFired = 0;


    // Raycast info shared between shooting and interaction
    private Transform currentHitTransform = null;
    private RaycastHit currentRaycastHit;
    private bool hasValidRaycast = false;


    private void Awake()
    {
        // Get component references
        thirdPersonController = GetComponent<ThirdPersonController>();
        starterAssetsInputs = GetComponent<StarterAssetsInputs>();
        animator = GetComponent<Animator>();
        playerInventory = GetComponent<PlayerInventory>();


        // Debug component availability
        Debug.Log($"[ThirdPersonShooterController] Component Check:");
        Debug.Log($"  ThirdPersonController: {(thirdPersonController != null ? "Found" : "NULL")}");
        Debug.Log($"  StarterAssetsInputs: {(starterAssetsInputs != null ? "Found" : "NULL")}");
        Debug.Log($"  PlayerInventory: {(playerInventory != null ? "Found (Optional)" : "NULL (Optional)")}");
        Debug.Log($"  Animator: {(animator != null ? "Found" : "NULL")}");
        Debug.Log($"  Aim Target Transform: {(aimTargetTransform != null ? "Found" : "NULL - ASSIGN DEBUG SPHERE!")}");
    }


    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();


        if (!IsOwner)
        {
            // Disable input components for non-owned players
            if (aimVirtualCamera != null)
                aimVirtualCamera.gameObject.SetActive(false);


            // Keep debug transform active for remote players so we can see their aim target
            if (debugTransform != null)
                debugTransform.gameObject.SetActive(true);


            // Subscribe to network variable changes for remote players
            isAiming.OnValueChanged += OnAimingChanged;
            networkAimTargetPosition.OnValueChanged += OnAimTargetPositionChanged;


            Debug.Log($"[ThirdPersonShooterController] Remote player spawned - Listening for aim updates");
            return; // Don't disable the script - we need Update for remote player aim updates
        }


        // Initialize ammo for owner
        currentAmmo.Value = magazineSize;
        Debug.Log($"[ThirdPersonShooterController] Local player spawned - Script enabled, Ammo: {currentAmmo.Value}");
    }


    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();


        // Unsubscribe from events
        if (!IsOwner)
        {
            isAiming.OnValueChanged -= OnAimingChanged;
            networkAimTargetPosition.OnValueChanged -= OnAimTargetPositionChanged;
        }
    }


    private void Update()
    {
        if (IsOwner)
        {
            // LOCAL PLAYER UPDATE
            UpdateLocalPlayer();
        }
        else
        {
            // REMOTE PLAYER UPDATE
            UpdateRemotePlayer();
        }
    }


    /// <summary>
    /// Update logic for the local player (who owns this character)
    /// </summary>
    private void UpdateLocalPlayer()
    {
        // Smoothly adjust aim rig weight
        if (aimRig != null)
            aimRig.weight = Mathf.Lerp(aimRig.weight, aimRigWeight, Time.deltaTime * 20f);


        // Perform shared raycast for both shooting and interaction
        PerformAimRaycast();


        // Update the network aim target position
        if (aimTargetTransform != null && hasValidRaycast)
        {
            aimTargetTransform.position = currentRaycastHit.point;


            // Sync the aim target position to network
            networkAimTargetPosition.Value = currentRaycastHit.point;
        }


        // Handle inventory interactions
        if (playerInventory != null)
        {
            HandlePickup();
            HandleDrop();
        }


        // Handle reload input
        HandleReload();


        // Handle aiming and shooting
        HandleAiming();
    }


    /// <summary>
    /// Update logic for remote players (to display their aiming correctly)
    /// </summary>
    private void UpdateRemotePlayer()
    {
        // Smoothly adjust aim rig weight based on network state
        float targetWeight = isAiming.Value ? 1f : 0f;
        if (aimRig != null)
            aimRig.weight = Mathf.Lerp(aimRig.weight, targetWeight, Time.deltaTime * 20f);


        // Update aim target transform position from network
        if (aimTargetTransform != null)
        {
            aimTargetTransform.position = networkAimTargetPosition.Value;
        }


        // Update debug transform if it exists
        if (debugTransform != null)
        {
            debugTransform.position = networkAimTargetPosition.Value;
        }


        // Sync animation layers based on aiming state
        if (animator != null)
        {
            if (isAiming.Value)
            {
                animator.SetLayerWeight(1, Mathf.Lerp(animator.GetLayerWeight(1), 1f, Time.deltaTime * 10f));
                animator.SetLayerWeight(2, Mathf.Lerp(animator.GetLayerWeight(2), 0f, Time.deltaTime * 10f));
            }
            else
            {
                animator.SetLayerWeight(1, Mathf.Lerp(animator.GetLayerWeight(1), 0f, Time.deltaTime * 10f));
                animator.SetLayerWeight(2, Mathf.Lerp(animator.GetLayerWeight(2), 1f, Time.deltaTime * 10f));
            }
        }
    }


    /// <summary>
    /// Called when the aiming state changes on the network
    /// </summary>
    private void OnAimingChanged(bool previousValue, bool newValue)
    {
        Debug.Log($"[Remote Player] Aiming state changed: {previousValue} -> {newValue}");
    }


    /// <summary>
    /// Called when the aim target position changes on the network
    /// </summary>
    private void OnAimTargetPositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        // Immediately update the target position
        if (aimTargetTransform != null)
        {
            aimTargetTransform.position = newValue;
        }
        if (debugTransform != null)
        {
            debugTransform.position = newValue;
        }
    }


    /// <summary>
    /// Performs a raycast from screen center for aiming and interaction
    /// </summary>
    private void PerformAimRaycast()
    {
        Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray ray = Camera.main.ScreenPointToRay(screenCenterPoint);


        currentHitTransform = null;
        hasValidRaycast = false;


        if (Physics.Raycast(ray, out currentRaycastHit, 999f, aimColliderLayerMask))
        {
            currentHitTransform = currentRaycastHit.transform;
            hasValidRaycast = true;
        }
        else
        {
            // Set to a far point if no hit
            currentRaycastHit.point = ray.GetPoint(999f);
            hasValidRaycast = true;
        }
    }


    /// <summary>
    /// Handles aiming mode and shooting
    /// </summary>
    private void HandleAiming()
    {
        if (starterAssetsInputs == null) return;


        // Check if player is aiming
        if (starterAssetsInputs.aim)
        {
            // Update network aiming state
            isAiming.Value = true;


            EnterAimMode();


            // Handle shooting while aiming
            if (!isReloading.Value && starterAssetsInputs.shoot && Time.time >= nextTimeToFire)
            {
                if (currentAmmo.Value > 0)
                {
                    Shoot();
                    nextTimeToFire = Time.time + fireRate;
                }
                else
                {
                    SetAnimationBoolServerRpc("shooting", false);
                    Debug.Log("[Shooting] Out of ammo! Reload needed.");
                }
            }
            else if (!starterAssetsInputs.shoot)
            {
                SetAnimationBoolServerRpc("shooting", false);
            }
        }
        else
        {
            // Update network aiming state
            isAiming.Value = false;


            ExitAimMode();
        }
    }


    /// <summary>
    /// Enter aiming mode with appropriate camera and movement settings
    /// </summary>
    private void EnterAimMode()
    {
        if (starterAssetsInputs == null) return;


        // Disable sprinting while aiming
        starterAssetsInputs.sprint = false;


        // Activate aim camera (only for local player)
        if (aimVirtualCamera != null)
            aimVirtualCamera.gameObject.SetActive(true);


        // Adjust controller settings
        if (thirdPersonController != null)
        {
            thirdPersonController.SetSensitivity(aimSensitivity);
            thirdPersonController.SetRotateOnMove(false);
        }


        // Adjust animation layers
        if (animator != null)
        {
            animator.SetLayerWeight(1, Mathf.Lerp(animator.GetLayerWeight(1), 1f, Time.deltaTime * 10f));
            animator.SetLayerWeight(2, Mathf.Lerp(animator.GetLayerWeight(2), 0f, Time.deltaTime * 10f));
        }


        // Rotate character to face aim target
        if (hasValidRaycast)
        {
            Vector3 worldAimTarget = currentRaycastHit.point;
            worldAimTarget.y = transform.position.y;
            Vector3 aimDirection = (worldAimTarget - transform.position).normalized;


            if (aimDirection != Vector3.zero)
            {
                transform.forward = Vector3.Lerp(transform.forward, aimDirection, Time.deltaTime * 20f);
            }
        }


        aimRigWeight = 1f;
    }


    /// <summary>
    /// Exit aiming mode and return to normal movement
    /// </summary>
    private void ExitAimMode()
    {
        if (starterAssetsInputs == null) return;


        // Deactivate aim camera
        if (aimVirtualCamera != null)
            aimVirtualCamera.gameObject.SetActive(false);


        // Restore controller settings
        if (thirdPersonController != null)
        {
            thirdPersonController.SetSensitivity(normalSensitivity);
            thirdPersonController.SetRotateOnMove(true);
        }


        // Adjust animation layers
        if (animator != null)
        {
            animator.SetLayerWeight(1, Mathf.Lerp(animator.GetLayerWeight(1), 0f, Time.deltaTime * 10f));
            animator.SetLayerWeight(2, Mathf.Lerp(animator.GetLayerWeight(2), 1f, Time.deltaTime * 10f));
            SetAnimationBoolServerRpc("shooting", false);
        }


        aimRigWeight = 0f;
        starterAssetsInputs.shoot = false;
    }


    /// <summary>
    /// Handle reload input and timing
    /// </summary>
    private void HandleReload()
    {
        if (starterAssetsInputs == null) return;


        if (starterAssetsInputs.reload && currentAmmo.Value < magazineSize && !isReloading.Value)
        {
            StartCoroutine(Reload());
        }
    }


    /// <summary>
    /// Handle item pickup (E key)
    /// </summary>
    private void HandlePickup()
    {
        if (starterAssetsInputs == null || playerInventory == null) return;


        if (starterAssetsInputs.interact)
        {
            starterAssetsInputs.interact = false;


            if (!playerInventory.IsHoldingItem())
            {
                if (hasValidRaycast && currentHitTransform != null &&
                    Vector3.Distance(transform.position, currentRaycastHit.point) <= interactRange)
                {
                    PickupObject pickup = currentHitTransform.GetComponent<PickupObject>();
                    if (pickup != null)
                    {
                        pickup.TryPickup(gameObject);
                        Debug.Log($"[Pickup] Picked up: {currentHitTransform.name}");
                    }
                }
            }
            else
            {
                Debug.Log("[Pickup] Already holding an item! Press Q to drop.");
            }
        }
    }


    /// <summary>
    /// Handle item drop (Q key)
    /// </summary>
    private void HandleDrop()
    {
        if (starterAssetsInputs == null || playerInventory == null) return;


        if (starterAssetsInputs.drop)
        {
            starterAssetsInputs.drop = false;


            if (playerInventory.IsHoldingItem())
            {
                Vector3 dropPos = transform.position + Vector3.up * 1f + transform.forward * 2f;
                playerInventory.DropItemServerRpc(dropPos);
                Debug.Log($"[Drop] Dropped item at position: {dropPos}");
            }
        }
    }


    /// <summary>
    /// Shoot weapon and sync with network
    /// </summary>
    private void Shoot()
    {
        if (isReloading.Value) return;


        SetAnimationBoolServerRpc("shooting", true);


        // Decrease ammo
        currentAmmo.Value--;
        bulletsFired++;


        Debug.Log($"[Shooting] Bullet #{bulletsFired} fired! Ammo: {currentAmmo.Value}/{magazineSize}");


        // Call server RPC to handle shooting effects
        if (hasValidRaycast)
        {
            bool isTarget = currentHitTransform != null && currentHitTransform.GetComponent<BulletTarget>() != null;

            // Get NetworkObject ID if target exists
            ulong targetNetworkObjectId = 0;
            if (isTarget && currentHitTransform != null)
            {
                NetworkObject networkObject = currentHitTransform.GetComponent<NetworkObject>();
                if (networkObject != null)
                {
                    targetNetworkObjectId = networkObject.NetworkObjectId;
                }
            }

            ShootServerRpc(currentRaycastHit.point, isTarget, targetNetworkObjectId);
        }
    }


    [ServerRpc]
    private void ShootServerRpc(Vector3 hitPoint, bool isTarget, ulong targetNetworkObjectId)
    {
        // Apply damage on server if target has BulletTarget and NetworkObject
        if (isTarget && targetNetworkObjectId != 0)
        {
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject networkObject))
            {
                Health health = networkObject.GetComponent<Health>();
                if (health != null)
                {
                    health.TakeDamage(damagePerShot);
                    Debug.Log($"[Damage] Applied {damagePerShot} damage to {networkObject.name}");
                }
                else
                {
                    Debug.LogWarning($"[Damage] {networkObject.name} has BulletTarget but no Health component!");
                }
            }
        }

        // Spawn VFX for all clients
        ShootClientRpc(hitPoint, isTarget);
    }


    [ClientRpc]
    private void ShootClientRpc(Vector3 hitPoint, bool isTarget)
    {
        // Spawn appropriate VFX
        if (isTarget && vfxHitGreen != null)
        {
            Instantiate(vfxHitGreen, hitPoint, Quaternion.identity);
        }
        else if (!isTarget && vfxHitRed != null)
        {
            Instantiate(vfxHitRed, hitPoint, Quaternion.identity);
        }
    }


    /// <summary>
    /// Reload coroutine
    /// </summary>
    private IEnumerator Reload()
    {
        isReloading.Value = true;


        SetAnimationTriggerServerRpc("reload");
        SetAnimationBoolServerRpc("shooting", false);


        Debug.Log("[Reload] Reloading...");


        if (starterAssetsInputs != null)
            starterAssetsInputs.shoot = false;


        yield return new WaitForSeconds(reloadTime);


        currentAmmo.Value = magazineSize;
        isReloading.Value = false;


        Debug.Log("[Reload] Complete!");


        ResetAnimationTriggerServerRpc("reload");


        if (starterAssetsInputs != null)
            starterAssetsInputs.reload = false;
    }


    #region Animation Network Synchronization


    [ServerRpc]
    private void SetAnimationBoolServerRpc(string paramName, bool value)
    {
        SetAnimationBoolClientRpc(paramName, value);
    }


    [ClientRpc]
    private void SetAnimationBoolClientRpc(string paramName, bool value)
    {
        if (animator != null)
            animator.SetBool(paramName, value);
    }


    [ServerRpc]
    private void SetAnimationTriggerServerRpc(string paramName)
    {
        SetAnimationTriggerClientRpc(paramName);
    }


    [ClientRpc]
    private void SetAnimationTriggerClientRpc(string paramName)
    {
        if (animator != null)
            animator.SetTrigger(paramName);
    }


    [ServerRpc]
    private void ResetAnimationTriggerServerRpc(string paramName)
    {
        ResetAnimationTriggerClientRpc(paramName);
    }


    [ClientRpc]
    private void ResetAnimationTriggerClientRpc(string paramName)
    {
        if (animator != null)
            animator.ResetTrigger(paramName);
    }


    #endregion


    #region Public Accessors


    public int GetCurrentAmmo() => currentAmmo.Value;
    public int GetMagazineSize() => magazineSize;
    public bool IsReloading() => isReloading.Value;
    public bool IsAiming() => isAiming.Value;
    public Vector3 GetAimTargetPosition() => networkAimTargetPosition.Value;


    #endregion
}