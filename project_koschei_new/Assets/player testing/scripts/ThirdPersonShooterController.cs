using System.Collections;
using UnityEngine;
using Cinemachine;
using StarterAssets;
using UnityEngine.Animations.Rigging;
using Unity.Netcode;

public class ThirdPersonShooterController : NetworkBehaviour
{
    [Header("Animation Rig")]
    [SerializeField] private Rig       aimRig;
    [SerializeField] private Transform aimTargetTransform;

    [Header("Camera Settings")]
    [SerializeField] private CinemachineVirtualCamera aimVirtualCamera;
    [SerializeField] private float normalSensitivity = 1f;
    [SerializeField] private float aimSensitivity    = 0.5f;

    [Header("Aiming")]
    [SerializeField] private LayerMask aimColliderLayerMask = ~0;
    [SerializeField] private Transform debugTransform;

    [Header("VFX")]
    [SerializeField] private GameObject vfxHitGreen;
    [SerializeField] private GameObject vfxHitRed;
    [SerializeField] private GameObject muzzleFlashPrefab;
    [SerializeField] private Transform  muzzlePoint;

    [Header("Audio")]
    [SerializeField] private AudioClip   gunshotClip;
    [SerializeField] private AudioClip   reloadClip;
    [SerializeField] private AudioSource weaponAudioSource;
    [Range(0f, 1f)][SerializeField] private float gunshotVolume = 0.8f;
    [Range(0f, 1f)][SerializeField] private float reloadVolume  = 0.8f;

    [Header("Weapon Settings")]
    [SerializeField] private float damagePerShot = 35f;
    [SerializeField] private float fireRate      = 0.1f;
    [SerializeField] private int   magazineSize  = 30;
    [SerializeField] private float reloadTime    = 2f;
    [Tooltip("Speed multiplier while reloading")]
    [SerializeField][Range(0f, 1f)] private float reloadSpeedMultiplier = 0.4f;

    [Header("Interaction Settings")]
    [SerializeField] private float     interactRange      = 5f;
    [SerializeField] private LayerMask interactLayerMask  = ~0;

    private NetworkVariable<int>     currentAmmo              = new NetworkVariable<int>(30,         NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool>    isReloading              = new NetworkVariable<bool>(false,      NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool>    isAiming                 = new NetworkVariable<bool>(false,      NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Vector3> networkAimTargetPosition = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private ThirdPersonController thirdPersonController;
    private StarterAssetsInputs   starterAssetsInputs;
    private PlayerInventory       playerInventory;
    private Animator              animator;

    private float aimRigWeight   = 0f;
    private float nextTimeToFire = 0f;
    private int   bulletsFired   = 0;

    private Transform  currentHitTransform = null;
    private RaycastHit currentRaycastHit;
    private bool       hasValidRaycast = false;

    private PickupObject _currentAimedPickup = null;

    // ================================================================

    private void Awake()
    {
        thirdPersonController = GetComponent<ThirdPersonController>();
        starterAssetsInputs   = GetComponent<StarterAssetsInputs>();
        animator              = GetComponent<Animator>();
        playerInventory       = GetComponent<PlayerInventory>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
        {
            if (aimVirtualCamera != null) aimVirtualCamera.gameObject.SetActive(false);
            if (debugTransform   != null) debugTransform.gameObject.SetActive(true);

            isAiming.OnValueChanged                 += OnAimingChanged;
            networkAimTargetPosition.OnValueChanged += OnAimTargetPositionChanged;
            return;
        }

        currentAmmo.Value = magazineSize;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (!IsOwner)
        {
            isAiming.OnValueChanged                 -= OnAimingChanged;
            networkAimTargetPosition.OnValueChanged -= OnAimTargetPositionChanged;
        }
    }

    // ================================================================

    private void Update()
    {
        if (IsOwner) UpdateLocalPlayer();
        else         UpdateRemotePlayer();
    }

    private void UpdateLocalPlayer()
    {
        if (aimRig != null)
            aimRig.weight = Mathf.Lerp(aimRig.weight, aimRigWeight, Time.deltaTime * 20f);

        PerformAimRaycast();

        if (aimTargetTransform != null && hasValidRaycast)
        {
            aimTargetTransform.position    = currentRaycastHit.point;
            networkAimTargetPosition.Value = currentRaycastHit.point;
        }

        UpdatePickupPrompt();

        if (playerInventory != null)
        {
            HandlePickup();
            HandleDrop();
        }

        HandleReload();
        HandleAiming();
    }

    // Shows prompt only when inventory can actually receive the item
    private void UpdatePickupPrompt()
    {
        PickupObject aimed = null;

        if (hasValidRaycast && currentHitTransform != null &&
            Vector3.Distance(transform.position, currentRaycastHit.point) <= interactRange)
        {
            PickupObject candidate = currentHitTransform.GetComponent<PickupObject>();
            if (candidate != null && (playerInventory == null || candidate.CanBePickedUpBy(playerInventory)))
                aimed = candidate;
        }

        if (aimed != _currentAimedPickup)
        {
            if (_currentAimedPickup != null) _currentAimedPickup.ShowPickupPrompt(false);
            if (aimed               != null) aimed.ShowPickupPrompt(true);
            _currentAimedPickup = aimed;
        }
    }

    private void UpdateRemotePlayer()
    {
        float targetWeight = isAiming.Value ? 1f : 0f;
        if (aimRig != null)
            aimRig.weight = Mathf.Lerp(aimRig.weight, targetWeight, Time.deltaTime * 20f);

        if (aimTargetTransform != null) aimTargetTransform.position = networkAimTargetPosition.Value;
        if (debugTransform     != null) debugTransform.position     = networkAimTargetPosition.Value;

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

    private void OnAimingChanged(bool prev, bool next) =>
        Debug.Log($"[Remote] Aiming: {prev} → {next}");

    private void OnAimTargetPositionChanged(Vector3 prev, Vector3 next)
    {
        if (aimTargetTransform != null) aimTargetTransform.position = next;
        if (debugTransform     != null) debugTransform.position     = next;
    }

    // ================================================================

    private void PerformAimRaycast()
    {
        Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray ray = Camera.main.ScreenPointToRay(screenCenter);

        currentHitTransform = null;
        hasValidRaycast     = false;

        if (Physics.Raycast(ray, out currentRaycastHit, 999f, aimColliderLayerMask))
        {
            currentHitTransform = currentRaycastHit.transform;
            hasValidRaycast     = true;
        }
        else
        {
            currentRaycastHit.point = ray.GetPoint(999f);
            hasValidRaycast         = true;
        }
    }

    private void HandleAiming()
    {
        if (starterAssetsInputs == null) return;

        if (starterAssetsInputs.aim)
        {
            isAiming.Value = true;
            EnterAimMode();

            if (!isReloading.Value && starterAssetsInputs.shoot && Time.time >= nextTimeToFire)
            {
                if (currentAmmo.Value > 0) { Shoot(); nextTimeToFire = Time.time + fireRate; }
                else { SetAnimationBoolServerRpc("shooting", false); Debug.Log("[Shooting] Out of ammo!"); }
            }
            else if (!starterAssetsInputs.shoot)
            {
                SetAnimationBoolServerRpc("shooting", false);
            }
        }
        else
        {
            isAiming.Value = false;
            ExitAimMode();
        }
    }

    private void EnterAimMode()
    {
        if (starterAssetsInputs == null) return;
        starterAssetsInputs.sprint = false;

        if (aimVirtualCamera != null) aimVirtualCamera.gameObject.SetActive(true);

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

        if (hasValidRaycast)
        {
            Vector3 worldAimTarget = currentRaycastHit.point;
            worldAimTarget.y       = transform.position.y;
            Vector3 aimDir         = (worldAimTarget - transform.position).normalized;
            if (aimDir != Vector3.zero)
                transform.forward = Vector3.Lerp(transform.forward, aimDir, Time.deltaTime * 20f);
        }

        aimRigWeight = 1f;
    }

    private void ExitAimMode()
    {
        if (starterAssetsInputs == null) return;

        if (aimVirtualCamera != null) aimVirtualCamera.gameObject.SetActive(false);

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

    private void HandleReload()
    {
        if (starterAssetsInputs == null) return;
        if (starterAssetsInputs.reload && currentAmmo.Value < magazineSize && !isReloading.Value)
            StartCoroutine(Reload());
    }

    // Delegates pickup eligibility check to PickupObject.CanBePickedUpBy
    private void HandlePickup()
    {
        if (starterAssetsInputs == null || playerInventory == null) return;

        if (starterAssetsInputs.interact)
        {
            starterAssetsInputs.interact = false;

            if (hasValidRaycast && currentHitTransform != null &&
                Vector3.Distance(transform.position, currentRaycastHit.point) <= interactRange)
            {
                PickupObject pickup = currentHitTransform.GetComponent<PickupObject>();
                if (pickup != null && pickup.CanBePickedUpBy(playerInventory))
                {
                    pickup.ShowPickupPrompt(false);
                    _currentAimedPickup = null;
                    pickup.TryPickup(gameObject);
                    Debug.Log($"[Pickup] Picked up: {currentHitTransform.name}");
                }
                else if (pickup != null)
                {
                    Debug.Log("[Pickup] Cannot pick up — inventory full or incompatible item held.");
                }
            }
        }
    }

    private void HandleDrop()
    {
        if (starterAssetsInputs == null || playerInventory == null) return;

        if (starterAssetsInputs.drop)
        {
            starterAssetsInputs.drop = false;

            if (playerInventory.IsHoldingItem())
            {
                Vector3 dropPos = transform.position + Vector3.up * 1f + transform.forward * 2f;
                playerInventory.TryDropItem(dropPos);
                Debug.Log($"[Drop] Dropped item at {dropPos}");
            }
        }
    }

    private void Shoot()
    {
        if (isReloading.Value) return;

        SetAnimationBoolServerRpc("shooting", true);
        currentAmmo.Value--;
        bulletsFired++;

        Debug.Log($"[Shooting] Bullet #{bulletsFired}. Ammo: {currentAmmo.Value}/{magazineSize}");
        PlayMuzzleEffectsLocal();

        if (hasValidRaycast)
        {
            bool  isTarget = currentHitTransform != null &&
                             currentHitTransform.GetComponent<Health>() != null;
            ulong targetId = 0;

            if (isTarget)
            {
                var netObj = currentHitTransform.GetComponent<NetworkObject>();
                if (netObj != null) targetId = netObj.NetworkObjectId;
            }
            ShootServerRpc(currentRaycastHit.point, isTarget, targetId);
        }
    }

    private void PlayMuzzleEffectsLocal()
    {
        if (muzzleFlashPrefab != null && muzzlePoint != null)
        {
            GameObject flash = Instantiate(muzzleFlashPrefab, muzzlePoint.position, muzzlePoint.rotation);
            Destroy(flash, 0.1f);
        }
        if (weaponAudioSource != null && gunshotClip != null)
            weaponAudioSource.PlayOneShot(gunshotClip, gunshotVolume);
    }

    [ServerRpc]
    private void ShootServerRpc(Vector3 hitPoint, bool isTarget, ulong targetNetworkObjectId)
    {
        if (isTarget && targetNetworkObjectId != 0)
        {
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                    targetNetworkObjectId, out NetworkObject netObj))
            {
                Health health = netObj.GetComponent<Health>();
                if (health != null) health.TakeDamage(damagePerShot);
            }
        }
        ShootClientRpc(hitPoint, isTarget);
    }

    [ClientRpc]
    private void ShootClientRpc(Vector3 hitPoint, bool isTarget)
    {
        if      (isTarget  && vfxHitGreen != null) Instantiate(vfxHitGreen, hitPoint, Quaternion.identity);
        else if (!isTarget && vfxHitRed   != null) Instantiate(vfxHitRed,   hitPoint, Quaternion.identity);

        if (!IsOwner)
        {
            if (muzzleFlashPrefab != null && muzzlePoint != null)
            {
                GameObject flash = Instantiate(muzzleFlashPrefab, muzzlePoint.position, muzzlePoint.rotation);
                Destroy(flash, 0.1f);
            }
            if (weaponAudioSource != null && gunshotClip != null)
                weaponAudioSource.PlayOneShot(gunshotClip, gunshotVolume);
        }
    }

    private IEnumerator Reload()
    {
        isReloading.Value = true;
        SetAnimationTriggerServerRpc("reload");
        SetAnimationBoolServerRpc("shooting", false);
        if (starterAssetsInputs != null) starterAssetsInputs.shoot = false;

        float origMove = 0f, origSprint = 0f;
        if (thirdPersonController != null)
        {
            origMove   = thirdPersonController.MoveSpeed;
            origSprint = thirdPersonController.SprintSpeed;
            thirdPersonController.MoveSpeed   = origMove   * reloadSpeedMultiplier;
            thirdPersonController.SprintSpeed = origSprint * reloadSpeedMultiplier;
        }

        if (weaponAudioSource != null && reloadClip != null)
            weaponAudioSource.PlayOneShot(reloadClip, reloadVolume);
        PlayReloadSoundClientRpc();

        yield return new WaitForSeconds(reloadTime);

        currentAmmo.Value = magazineSize;
        isReloading.Value = false;

        if (thirdPersonController != null)
        {
            thirdPersonController.MoveSpeed   = origMove;
            thirdPersonController.SprintSpeed = origSprint;
        }

        ResetAnimationTriggerServerRpc("reload");
        if (starterAssetsInputs != null) starterAssetsInputs.reload = false;
        Debug.Log("[Reload] Complete!");
    }

    [ClientRpc]
    private void PlayReloadSoundClientRpc()
    {
        if (!IsOwner && weaponAudioSource != null && reloadClip != null)
            weaponAudioSource.PlayOneShot(reloadClip, reloadVolume);
    }

    #region Animation Network Sync
    [ServerRpc] private void SetAnimationBoolServerRpc(string p, bool v)  => SetAnimationBoolClientRpc(p, v);
    [ClientRpc] private void SetAnimationBoolClientRpc(string p, bool v)  { if (animator != null) animator.SetBool(p, v); }
    [ServerRpc] private void SetAnimationTriggerServerRpc(string p)       => SetAnimationTriggerClientRpc(p);
    [ClientRpc] private void SetAnimationTriggerClientRpc(string p)       { if (animator != null) animator.SetTrigger(p); }
    [ServerRpc] private void ResetAnimationTriggerServerRpc(string p)     => ResetAnimationTriggerClientRpc(p);
    [ClientRpc] private void ResetAnimationTriggerClientRpc(string p)     { if (animator != null) animator.ResetTrigger(p); }
    #endregion

    #region Public Accessors
    public int     GetCurrentAmmo()       => currentAmmo.Value;
    public int     GetMagazineSize()      => magazineSize;
    public bool    IsReloading()          => isReloading.Value;
    public bool    IsAiming()             => isAiming.Value;
    public Vector3 GetAimTargetPosition() => networkAimTargetPosition.Value;
    #endregion
}