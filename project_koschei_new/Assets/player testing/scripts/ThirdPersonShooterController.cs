using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cinemachine;
using StarterAssets;
using UnityEngine.InputSystem;
using UnityEngine.Animations.Rigging;
using Unity.Netcode;

public class ThirdPersonShooterController : NetworkBehaviour
{
    [SerializeField] private Rig aimRig;
    [SerializeField] private CinemachineVirtualCamera aimVirtualCamera;
    [SerializeField] private float normalSensitivity = 1f;
    [SerializeField] private float aimSensitivity = 0.5f;
    [SerializeField] private LayerMask aimColliderLayerMask;
    [SerializeField] private Transform debugTransform;
    [SerializeField] private GameObject vfxHitGreen;
    [SerializeField] private GameObject vfxHitRed;

    [Header("Weapon Settings")]
    [SerializeField] private float fireRate = 0.1f;
    [SerializeField] private int magazineSize = 30;
    [SerializeField] private float reloadTime = 2f;

    [Header("Interaction Settings")]
    [SerializeField] private float interactRange = 5f;
    [SerializeField] private LayerMask interactLayerMask;

    // Network synced variables
    private NetworkVariable<int> currentAmmo = new NetworkVariable<int>(30, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> isReloading = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private ThirdPersonController thirdPersonController;
    private StarterAssetsInputs starterAssetsInputs;
    private PlayerInventory playerInventory;
    private Animator animator;
    private float aimRigWeight = 0f;
    private float nextTimeToFire = 0f;
    private int bulletsFired = 0;

    // Raycast info shared between shooting and interaction
    private Transform currentHitTransform = null;
    private RaycastHit currentRaycastHit;

    private void Awake()
    {
        thirdPersonController = GetComponent<ThirdPersonController>();
        starterAssetsInputs = GetComponent<StarterAssetsInputs>();
        animator = GetComponent<Animator>();
        playerInventory = GetComponent<PlayerInventory>();

        Debug.Log($"ThirdPersonController: {(thirdPersonController != null ? "Found" : "NULL")}");
        Debug.Log($"StarterAssetsInputs: {(starterAssetsInputs != null ? "Found" : "NULL")}");
        Debug.Log($"PlayerInventory: {(playerInventory != null ? "Found" : "NULL")}");
        Debug.Log($"Animator: {(animator != null ? "Found" : "NULL")}");
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Disable input for non-owned players
        if (!IsOwner)
        {
            if (aimVirtualCamera != null)
                aimVirtualCamera.gameObject.SetActive(false);

            if (debugTransform != null)
                debugTransform.gameObject.SetActive(false);

            enabled = false; // Disable this script for non-owned players
            return;
        }

        // Initialize ammo for owner
        if (IsOwner)
        {
            currentAmmo.Value = magazineSize;
        }
    }

    private void Start()
    {
        if (!IsOwner) return;
    }

    private void Update()
    {
        if (!IsOwner) return;

        aimRig.weight = Mathf.Lerp(aimRig.weight, aimRigWeight, Time.deltaTime * 20f);

        // SHARED RAYCAST for both shooting and interaction
        Vector3 mouseWorldPosition = Vector3.zero;
        Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray ray = Camera.main.ScreenPointToRay(screenCenterPoint);
        currentHitTransform = null;

        if (Physics.Raycast(ray, out currentRaycastHit, 999f, aimColliderLayerMask))
        {
            debugTransform.position = currentRaycastHit.point;
            mouseWorldPosition = currentRaycastHit.point;
            currentHitTransform = currentRaycastHit.transform;
        }

        // PICKUP SYSTEM (E key) - only if inventory exists
        if (playerInventory != null)
        {
            HandlePickup();
        }

        // DROP SYSTEM (Q key) - only if inventory exists
        if (playerInventory != null)
        {
            HandleDrop();
        }

        // RELOAD INPUT
        if (starterAssetsInputs != null && starterAssetsInputs.reload && currentAmmo.Value < magazineSize && !isReloading.Value)
        {
            StartCoroutine(Reload());
        }

        // AIMING / NOT AIMING
        if (starterAssetsInputs != null && starterAssetsInputs.aim)
        {
            starterAssetsInputs.sprint = false;

            if (aimVirtualCamera != null)
                aimVirtualCamera.gameObject.SetActive(true);

            if (thirdPersonController != null)
            {
                thirdPersonController.SetSensitivity(aimSensitivity);
                thirdPersonController.SetRotateOnMove(false);
            }

            if (animator != null)
            {
                animator.SetLayerWeight(1, Mathf.Lerp(animator.GetLayerWeight(1), 1f, Time.deltaTime * 10f));
                animator.SetLayerWeight(2, Mathf.Lerp(animator.GetLayerWeight(2), 0f, Time.deltaTime * 10f));
            }

            Vector3 worldAimTarget = mouseWorldPosition;
            worldAimTarget.y = transform.position.y;
            Vector3 aimDirection = (worldAimTarget - transform.position).normalized;

            transform.forward = Vector3.Lerp(transform.forward, aimDirection, Time.deltaTime * 20f);
            aimRigWeight = 1f;
        }
        else if (starterAssetsInputs != null)
        {
            if (aimVirtualCamera != null)
                aimVirtualCamera.gameObject.SetActive(false);

            if (thirdPersonController != null)
            {
                thirdPersonController.SetSensitivity(normalSensitivity);
                thirdPersonController.SetRotateOnMove(true);
            }

            if (animator != null)
            {
                animator.SetLayerWeight(1, Mathf.Lerp(animator.GetLayerWeight(1), 0f, Time.deltaTime * 10f));
                animator.SetLayerWeight(2, Mathf.Lerp(animator.GetLayerWeight(2), 1f, Time.deltaTime * 10f));
                SetAnimationBoolServerRpc("shooting", false);
            }

            aimRigWeight = 0f;
            starterAssetsInputs.shoot = false;
        }

        // SHOOTING (full auto while button held)
        if (starterAssetsInputs != null && starterAssetsInputs.aim && !isReloading.Value)
        {
            starterAssetsInputs.sprint = false;
            if (starterAssetsInputs.shoot && Time.time >= nextTimeToFire)
            {
                if (currentAmmo.Value > 0)
                {
                    Shoot(currentHitTransform, currentRaycastHit);
                    nextTimeToFire = Time.time + fireRate;
                }
                else
                {
                    SetAnimationBoolServerRpc("shooting", false);
                    Debug.Log("Out of ammo! Reload needed.");
                }
            }
            else if (!starterAssetsInputs.shoot)
            {
                SetAnimationBoolServerRpc("shooting", false);
            }
        }
        else if (starterAssetsInputs != null)
        {
            starterAssetsInputs.shoot = false;
            SetAnimationBoolServerRpc("shooting", false);
        }
    }

    private void HandlePickup()
    {
        if (starterAssetsInputs == null)
        {
            Debug.LogError("starterAssetsInputs is NULL in HandlePickup!");
            return;
        }

        if (starterAssetsInputs.interact)
        {
            Debug.Log("Interact button pressed!");
            starterAssetsInputs.interact = false;

            if (playerInventory == null)
            {
                Debug.LogError("PlayerInventory component missing! Add it to " + gameObject.name);
                return;
            }

            Debug.Log($"PlayerInventory.IsHoldingItem: {playerInventory.IsHoldingItem()}");

            if (!playerInventory.IsHoldingItem())
            {
                Debug.Log($"currentHitTransform: {(currentHitTransform != null ? currentHitTransform.name : "NULL")}");
                Debug.Log($"Distance: {(currentHitTransform != null ? Vector3.Distance(transform.position, currentRaycastHit.point).ToString() : "N/A")}");

                if (currentHitTransform != null && Vector3.Distance(transform.position, currentRaycastHit.point) <= interactRange)
                {
                    PickupObject pickup = currentHitTransform.GetComponent<PickupObject>();
                    Debug.Log($"PickupObject component: {(pickup != null ? "Found" : "NULL")}");

                    if (pickup != null)
                    {
                        pickup.TryPickup(gameObject);
                        Debug.Log($"Picked up: {currentHitTransform.name}");
                    }
                    else
                    {
                        Debug.Log("Object is not pickupable");
                    }
                }
                else
                {
                    Debug.Log("No item in range to pickup");
                }
            }
            else
            {
                Debug.Log("Already holding an item! Press Q to drop.");
            }
        }
    }

    private void HandleDrop()
    {
        if (starterAssetsInputs == null)
        {
            Debug.LogError("starterAssetsInputs is NULL in HandleDrop!");
            return;
        }

        if (starterAssetsInputs.drop)
        {
            starterAssetsInputs.drop = false;

            if (playerInventory == null)
            {
                Debug.LogError("PlayerInventory component missing! Add it to " + gameObject.name);
                return;
            }

            Debug.Log($"Drop pressed. Holding item: {playerInventory.IsHoldingItem()}");

            if (playerInventory.IsHoldingItem())
            {
                Vector3 dropPos = transform.position + Vector3.up * 1f + transform.forward * 2f;
                Debug.Log($"Calling DropItemServerRpc at position: {dropPos}");
                playerInventory.DropItemServerRpc(dropPos);
            }
            else
            {
                Debug.Log("Not holding any item to drop");
            }
        }
    }

    private void Shoot(Transform hitTransform, RaycastHit raycastHit)
    {
        if (isReloading.Value) return;

        SetAnimationBoolServerRpc("shooting", true);

        currentAmmo.Value--;
        bulletsFired++;

        Debug.Log($"Bullet #{bulletsFired} fired! Ammo remaining: {currentAmmo.Value}/{magazineSize}");

        // Call server RPC to handle shooting effects
        if (hitTransform != null)
        {
            ShootServerRpc(raycastHit.point, hitTransform.GetComponent<BulletTarget>() != null);
        }
        else
        {
            ShootServerRpc(raycastHit.point, false);
        }
    }

    [ServerRpc]
    private void ShootServerRpc(Vector3 hitPoint, bool isTarget)
    {
        // Spawn VFX for all clients
        ShootClientRpc(hitPoint, isTarget);
    }

    [ClientRpc]
    private void ShootClientRpc(Vector3 hitPoint, bool isTarget)
    {
        if (isTarget && vfxHitGreen != null)
        {
            Instantiate(vfxHitGreen, hitPoint, Quaternion.identity);
        }
        else if (!isTarget && vfxHitRed != null)
        {
            Instantiate(vfxHitRed, hitPoint, Quaternion.identity);
        }
    }

    private IEnumerator Reload()
    {
        isReloading.Value = true;

        SetAnimationTriggerServerRpc("reload");
        SetAnimationBoolServerRpc("shooting", false);

        Debug.Log("Reloading...");

        if (starterAssetsInputs != null)
            starterAssetsInputs.shoot = false;

        yield return new WaitForSeconds(reloadTime);

        currentAmmo.Value = magazineSize;
        isReloading.Value = false;

        Debug.Log("Reload complete!");

        ResetAnimationTriggerServerRpc("reload");

        if (starterAssetsInputs != null)
            starterAssetsInputs.reload = false;
    }

    // Network RPCs for animation synchronization
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
}