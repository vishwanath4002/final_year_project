using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class PlayerHUD : NetworkBehaviour
{
    [Header("HUD Root")]
    [SerializeField] private GameObject hudPanel;
    [SerializeField] private Canvas playerCanvas;

    [Header("Health (Bottom Center Bar)")]
    [SerializeField] private Image healthBarFill;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private float maxHealth = 100f;

    [Header("Ammo (Top Right Box)")]
    [SerializeField] private TextMeshProUGUI ammoText;
    [SerializeField] private TextMeshProUGUI reloadText;

    [Header("Inventory (Right Side of Bottom Bar)")]
    [SerializeField] private GameObject inventoryGroup;
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private TextMeshProUGUI holdHintText;

    [Header("Task Display")]
    [SerializeField] private GameObject taskPanel;            // Parent panel — toggle this to show/hide entire task UI
    [SerializeField] private TextMeshProUGUI taskDescription; // Current task sentence, e.g. "Collect 5 logs and light the fire."
    [SerializeField] private TextMeshProUGUI taskCompleteText; // "Task complete" line — fades out after completion
    [SerializeField] private float completeLingerDuration = 3f; // How long the line stays visible

    // Static accessor so TaskManager can always find the local player's HUD
    public static PlayerHUD Local { get; private set; }

    // Component refs
    private PlayerHealthHandler healthHandler;
    private ThirdPersonShooterController shooterController;
    private PlayerInventory playerInventory;

    // Inventory change tracking
    private bool lastHoldingState = false;
    private string lastItemName = "";

    private Coroutine completeCoroutine;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
        {
            if (playerCanvas != null) playerCanvas.enabled = false;
            if (hudPanel != null) hudPanel.SetActive(false);
            enabled = false;
            return;
        }

        Local = this;

        if (playerCanvas != null) playerCanvas.enabled = true;

        healthHandler = GetComponent<PlayerHealthHandler>();
        shooterController = GetComponent<ThirdPersonShooterController>();
        playerInventory = GetComponent<PlayerInventory>();

        if (hudPanel != null) hudPanel.SetActive(true);
        if (reloadText != null) reloadText.gameObject.SetActive(false);

        // Hide task UI until a task is set
        if (taskPanel != null) taskPanel.SetActive(false);
        if (taskCompleteText != null) taskCompleteText.gameObject.SetActive(false);

        RefreshInventoryUI(force: true);
        UpdateHealthUI();
        UpdateAmmoUI();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (Local == this) Local = null;
    }

    private void Update()
    {
        UpdateHealthUI();
        UpdateAmmoUI();
        RefreshInventoryUI(force: false);
    }

    // ================================================================
    // TASK UI — call these from your TaskManager
    // ================================================================

    /// <summary>
    /// Shows a new current task description on the HUD.
    /// Call this when a task begins.
    /// </summary>
    public void ShowTask(string description)
    {
        if (taskPanel != null) taskPanel.SetActive(true);
        if (taskDescription != null) taskDescription.text = description;
        if (taskCompleteText != null) taskCompleteText.gameObject.SetActive(false);
    }

    /// <summary>
    /// Marks the current task as complete. Shows a line briefly, then clears the task display.
    /// </summary>
    public void CompleteCurrentTask(string completedTaskName = "Task")
    {
        if (taskDescription != null) taskDescription.text = "";

        if (taskCompleteText != null)
        {
            taskCompleteText.text = $"{completedTaskName} complete";
            taskCompleteText.gameObject.SetActive(true);
        }

        if (completeCoroutine != null) StopCoroutine(completeCoroutine);
        completeCoroutine = StartCoroutine(HideCompleteLineAfterDelay());
    }

    /// <summary>
    /// Clears all task text immediately.
    /// </summary>
    public void ClearTask()
    {
        if (taskDescription != null) taskDescription.text = "";
        if (taskCompleteText != null) taskCompleteText.gameObject.SetActive(false);
        if (taskPanel != null) taskPanel.SetActive(false);
    }

    private IEnumerator HideCompleteLineAfterDelay()
    {
        yield return new WaitForSeconds(completeLingerDuration);

        if (taskCompleteText != null) taskCompleteText.gameObject.SetActive(false);
        if (taskPanel != null) taskPanel.SetActive(false);
    }

    // ================================================================
    // HEALTH
    // ================================================================

    private void UpdateHealthUI()
    {
        if (healthHandler == null) return;

        float current = healthHandler.GetCurrentHealth();
        float pct = Mathf.Clamp01(current / maxHealth);

        if (healthBarFill != null)
        {
            float fullWidth = healthBarFill.rectTransform.parent.GetComponent<RectTransform>().rect.width - 4f;
            Vector2 size = healthBarFill.rectTransform.sizeDelta;
            size.x = fullWidth * pct;
            healthBarFill.rectTransform.sizeDelta = size;
        }

        if (healthText != null)
            healthText.text = $"{Mathf.CeilToInt(current)}  /  {Mathf.CeilToInt(maxHealth)}";
    }

    // ================================================================
    // AMMO
    // ================================================================

    private void UpdateAmmoUI()
    {
        if (shooterController == null) return;

        bool reloading = shooterController.IsReloading();
        int current = shooterController.GetCurrentAmmo();
        int mag = shooterController.GetMagazineSize();

        if (ammoText != null)
            ammoText.text = reloading ? "- - -" : $"{current}  /  {mag}";

        if (reloadText != null)
            reloadText.gameObject.SetActive(reloading);
    }

    // ================================================================
    // INVENTORY
    // ================================================================

    private void RefreshInventoryUI(bool force)
    {
        if (playerInventory == null) return;

        bool holding = playerInventory.IsHoldingItem();
        GameObject prefab = holding ? playerInventory.GetHeldPrefab() : null;
        string itemName = prefab != null ? prefab.name : "";

        if (!force && holding == lastHoldingState && itemName == lastItemName) return;

        lastHoldingState = holding;
        lastItemName = itemName;

        if (holding && prefab != null)
        {
            if (itemNameText != null) itemNameText.text = itemName;
            if (holdHintText != null) holdHintText.text = "[Q] Drop";
        }
        else
        {
            if (itemNameText != null) itemNameText.text = "Empty";
            if (holdHintText != null) holdHintText.text = "[E] Pick Up";
        }
    }
}