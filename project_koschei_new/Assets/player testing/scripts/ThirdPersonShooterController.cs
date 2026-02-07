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
    [SerializeField] private int currentAmmo;
    [SerializeField] private float reloadTime = 2f;

    [Header("Interaction Settings")]
    [SerializeField] private float interactRange = 5f;
    [SerializeField] private LayerMask interactLayerMask;

    private bool isReloading = false;

    private ThirdPersonController thirdPersonController;
    private StarterAssetsInputs starterAssetsInputs;
    private PlayerInventory playerInventory;
    private Animator animator;

    // Network variables - WRITABLE ONLY BY SERVER
    private NetworkVariable<bool> isAiming = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<float> networkAimRigWeight = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private float localAimRigWeight = 0f;
    private float nextTimeToFire = 0f;
    private int bulletsFired = 0;

    private Transform currentHitTransform = null;
    private RaycastHit currentRaycastHit;

    private void Awake()
    {
        thirdPersonController = GetComponent<ThirdPersonController>();
        starterAssetsInputs = GetComponent<StarterAssetsInputs>();
        animator = GetComponent<Animator>();
        playerInventory = GetComponent<PlayerInventory>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
        {
            // Disable aim camera for non-owners
            if (aimVirtualCamera != null)
            {
                aimVirtualCamera.Priority = 0;
                aimVirtualCamera.gameObject.SetActive(false);
            }

            // Disable debug sphere for non-owners
            if (debugTransform != null)
            {
                debugTransform.gameObject.SetActive(false);
            }
        }
        else
        {
            // Enable debug sphere only for owner
            if (debugTransform != null)
            {
                debugTransform.gameObject.SetActive(true);
            }
        }
    }

    private void Start()
    {
        currentAmmo = magazineSize;
    }

    private void Update()
    {
        // Apply networked aim rig weight for ALL players
        if (aimRig != null)
        {
            float targetWeight = IsOwner ? localAimRigWeight : networkAimRigWeight.Value;
            aimRig.weight = Mathf.Lerp(aimRig.weight, targetWeight, Time.deltaTime * 20f);
        }

        // Apply networked animator layers for ALL players
        if (animator != null)
        {
            bool shouldAim = IsOwner ? (starterAssetsInputs?.aim ?? false) : isAiming.Value;

            if (shouldAim)
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

        // CRITICAL: Only owner runs input/shooting logic
        if (!IsOwner) return;

        // RAYCAST for shooting and interaction
        Vector3 mouseWorldPosition = Vector3.zero;
        Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray ray = Camera.main.ScreenPointToRay(screenCenterPoint);
        currentHitTransform = null;

        if (Physics.Raycast(ray, out currentRaycastHit, 999f, aimColliderLayerMask))
        {
            if (debugTransform != null && debugTransform.gameObject.activeSelf)
            {
                debugTransform.position = currentRaycastHit.point;
            }
            mouseWorldPosition = currentRaycastHit.point;
            currentHitTransform = currentRaycastHit.transform;
        }

        // Pickup/Drop
        if (playerInventory != null)
        {
            HandlePickup();
            HandleDrop();
        }

        // Reload
        if (starterAssetsInputs != null && starterAssetsInputs.reload && currentAmmo < magazineSize && !isReloading)
        {
            StartCoroutine(Reload());
        }

        // Aiming logic
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

            // Rotate player to aim direction
            Vector3 worldAimTarget = mouseWorldPosition;
            worldAimTarget.y = transform.position.y;
            Vector3 aimDirection = (worldAimTarget - transform.position).normalized;

            if (aimDirection != Vector3.zero)
            {
                transform.forward = Vector3.Lerp(transform.forward, aimDirection, Time.deltaTime * 20f);
            }

            localAimRigWeight = 1f;

            // Update network variables via ServerRpc
            UpdateAimStateServerRpc(true, 1f);
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

            localAimRigWeight = 0f;
            starterAssetsInputs.shoot = false;

            // Update network variables via ServerRpc
            UpdateAimStateServerRpc(false, 0f);
        }

        // Shooting
        if (starterAssetsInputs != null && starterAssetsInputs.aim && !isReloading)
        {
            if (starterAssetsInputs.shoot && Time.time >= nextTimeToFire)
            {
                if (currentAmmo > 0)
                {
                    Shoot(currentHitTransform, currentRaycastHit);
                    nextTimeToFire = Time.time + fireRate;
                }
            }
        }
    }

    [ServerRpc]
    private void UpdateAimStateServerRpc(bool aiming, float rigWeight)
    {
        isAiming.Value = aiming;
        networkAimRigWeight.Value = rigWeight;
    }

    private void HandlePickup()
    {
        if (starterAssetsInputs == null) return;

        if (starterAssetsInputs.interact)
        {
            starterAssetsInputs.interact = false;

            if (playerInventory == null) return;

            if (!playerInventory.IsHoldingItem())
            {
                if (currentHitTransform != null && Vector3.Distance(transform.position, currentRaycastHit.point) <= interactRange)
                {
                    PickupObject pickup = currentHitTransform.GetComponent<PickupObject>();
                    if (pickup != null)
                    {
                        pickup.TryPickup(gameObject);
                    }
                }
            }
        }
    }

    private void HandleDrop()
    {
        if (starterAssetsInputs == null) return;

        if (starterAssetsInputs.drop)
        {
            starterAssetsInputs.drop = false;

            if (playerInventory == null) return;

            if (playerInventory.IsHoldingItem())
            {
                Vector3 dropPos = transform.position + Vector3.up * 1f + transform.forward * 2f;
                playerInventory.DropItemServerRpc(dropPos);
            }
        }
    }

    private void Shoot(Transform hitTransform, RaycastHit raycastHit)
    {
        if (isReloading) return;

        currentAmmo--;
        bulletsFired++;

        // Tell server to show shooting for everyone
        bool isTarget = hitTransform != null && hitTransform.GetComponent<BulletTarget>() != null;
        ShootServerRpc(raycastHit.point, isTarget);
    }

    [ServerRpc]
    private void ShootServerRpc(Vector3 hitPoint, bool isTarget)
    {
        // Server tells all clients to show shooting
        ShootClientRpc(hitPoint, isTarget);
    }

    [ClientRpc]
    private void ShootClientRpc(Vector3 hitPoint, bool isTarget)
    {
        // All clients play animation and spawn VFX
        if (animator != null)
        {
            animator.SetBool("shooting", true);
        }

        GameObject vfx = isTarget ? vfxHitGreen : vfxHitRed;
        if (vfx != null)
        {
            Instantiate(vfx, hitPoint, Quaternion.identity);
        }

        // Stop shooting animation after short delay
        StartCoroutine(StopShootingAnimation());
    }

    private IEnumerator StopShootingAnimation()
    {
        yield return new WaitForSeconds(0.1f);
        if (animator != null)
        {
            animator.SetBool("shooting", false);
        }
    }

    private IEnumerator Reload()
    {
        isReloading = true;

        if (animator != null)
        {
            animator.SetTrigger("reload");
            animator.SetBool("shooting", false);
        }

        if (starterAssetsInputs != null)
            starterAssetsInputs.shoot = false;

        yield return new WaitForSeconds(reloadTime);

        currentAmmo = magazineSize;
        isReloading = false;

        if (animator != null)
            animator.ResetTrigger("reload");

        if (starterAssetsInputs != null)
            starterAssetsInputs.reload = false;
    }
}
