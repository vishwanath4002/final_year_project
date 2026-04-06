using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHUD : NetworkBehaviour
{
    public static PlayerHUD Local { get; private set; }

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

    [Header("Task Panel")]
    [SerializeField] private GameObject taskPanel;
    [SerializeField] private TextMeshProUGUI taskText;          // maps to taskDescription in old script
    [SerializeField] private TextMeshProUGUI taskCompleteText;

    [Header("Settings")]
    [SerializeField] private float completeLingerDuration = 3f;

    // Component refs
    private PlayerHealthHandler healthHandler;
    private ThirdPersonShooterController shooterController;
    private PlayerInventory playerInventory;

    // Inventory change tracking
    private bool lastHoldingState = false;
    private string lastItemName = "";
    private bool _canMode = false; // true when displaying a can stack — blocks RefreshInventoryUI overwrite

    private Coroutine completeCoroutine;

    // Task1 checklist state
    private bool _task1Active      = false;
    private bool _showFirewoodLine = false;
    private bool _showLightLine    = false;
    private bool _showMushroomLine = false;
    private bool _showCanLine      = false;

    private int _wood,      _requiredWood;
    private int _mushrooms, _requiredMushrooms;
    private int _cans,      _requiredCans;

    // ================================================================
    // NETWORK SPAWN
    // ================================================================

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

        healthHandler     = GetComponent<PlayerHealthHandler>();
        shooterController = GetComponent<ThirdPersonShooterController>();
        playerInventory   = GetComponent<PlayerInventory>();

        if (hudPanel != null) hudPanel.SetActive(true);
        if (reloadText != null) reloadText.gameObject.SetActive(false);
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

    // ================================================================
    // UPDATE
    // ================================================================

    private void Update()
    {
        UpdateHealthUI();
        UpdateAmmoUI();
        RefreshInventoryUI(force: false);
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
        int current    = shooterController.GetCurrentAmmo();
        int mag        = shooterController.GetMagazineSize();

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

        // Can stack display is managed entirely by UpdateCanInventory — don't overwrite it
        if (_canMode) return;

        bool holding = playerInventory.IsHoldingItem();
        GameObject prefab = holding ? playerInventory.GetHeldPrefab() : null;
        string itemName = prefab != null ? prefab.name : "";

        if (!force && holding == lastHoldingState && itemName == lastItemName) return;

        lastHoldingState = holding;
        lastItemName = itemName;

        // Restore normal font size for single items
        if (itemNameText != null) itemNameText.fontSize = 18f;

        if (holding && prefab != null)
        {
            if (inventoryGroup != null) inventoryGroup.SetActive(true);
            if (itemNameText != null) itemNameText.text = itemName;
            if (holdHintText != null) holdHintText.text = "[Q] Drop";
        }
        else
        {
            if (inventoryGroup != null) inventoryGroup.SetActive(true);
            if (itemNameText != null) itemNameText.text = "Empty";
            if (holdHintText != null) holdHintText.text = "[E] Pick Up";
        }
    }

    // ================================================================
    // HELD ITEM DISPLAY (legacy — kept for compatibility)
    // ================================================================

    public void ShowHeldItem(string itemName)
    {
        if (inventoryGroup != null) inventoryGroup.SetActive(true);
        if (itemNameText != null) itemNameText.text = itemName;
    }

    public void UpdateCanInventory(string[] names)
    {
        int count = names != null ? names.Length : 0;

        if (count == 0)
        {
            _canMode = false;
            if (inventoryGroup != null) inventoryGroup.SetActive(false);
            if (itemNameText != null) itemNameText.text = "";
            return;
        }

        _canMode = true;
        if (inventoryGroup != null) inventoryGroup.SetActive(true);
        if (itemNameText == null) return;

        // Smaller font so all three names fit in the slot
        itemNameText.fontSize = 13f;

        var sb = new StringBuilder();
        for (int i = count - 1; i >= 0; i--)  // top of stack first
            sb.AppendLine(names[i]);
        itemNameText.text = sb.ToString().TrimEnd();
    }

    public void ClearHeldItem()
    {
        _canMode = false;
        if (inventoryGroup != null) inventoryGroup.SetActive(false);
        if (itemNameText != null) itemNameText.text = "";
    }

    // ================================================================
    // TASK UI — simple one-liner
    // ================================================================

    public void ShowTask(string description)
    {
        _task1Active = false;
        if (taskPanel != null) taskPanel.SetActive(true);
        if (taskCompleteText != null) taskCompleteText.gameObject.SetActive(false);
        if (taskText != null) { taskText.gameObject.SetActive(true); taskText.text = description; }
    }

    public void CompleteCurrentTask(string completedTaskName = "Task")
    {
        _task1Active = false;
        if (taskText != null) taskText.gameObject.SetActive(false);

        if (taskCompleteText != null)
        {
            taskCompleteText.text = $"[Done]  {completedTaskName}";
            taskCompleteText.gameObject.SetActive(true);
        }

        if (completeCoroutine != null) StopCoroutine(completeCoroutine);
        completeCoroutine = StartCoroutine(HideCompleteLineAfterDelay());
    }

    public void ClearTask()
    {
        _task1Active = false;
        if (taskPanel != null) taskPanel.SetActive(false);
        if (taskText != null) taskText.gameObject.SetActive(false);
        if (taskCompleteText != null) taskCompleteText.gameObject.SetActive(false);
        if (completeCoroutine != null) { StopCoroutine(completeCoroutine); completeCoroutine = null; }
    }

    // ================================================================
    // TASK1 CHECKLIST
    // ================================================================

    public void ShowTask1(int requiredWood, int requiredMushrooms, int requiredCans)
    {
        _task1Active       = true;
        _wood              = 0; _requiredWood      = requiredWood;
        _mushrooms         = 0; _requiredMushrooms = requiredMushrooms;
        _cans              = 0; _requiredCans      = requiredCans;

        _showFirewoodLine  = true;
        _showLightLine     = false;
        _showMushroomLine  = false;
        _showCanLine       = true;

        if (taskPanel != null) taskPanel.SetActive(true);
        if (taskCompleteText != null) taskCompleteText.gameObject.SetActive(false);

        RefreshTask1Text();
    }

    public void SetFirewoodProgress(int current, int required)
    {
        _wood = current; _requiredWood = required;
        if (_task1Active) RefreshTask1Text();
    }

    public void OnFirewoodDepositComplete()
    {
        _showFirewoodLine = false;
        _showLightLine    = true;
        if (_task1Active) RefreshTask1Text();
    }

    public void OnFireLit(int requiredMushrooms)
    {
        _showLightLine     = false;
        _showMushroomLine  = true;
        _requiredMushrooms = requiredMushrooms;
        _mushrooms         = 0;
        if (_task1Active) RefreshTask1Text();
    }

    public void SetMushroomProgress(int current, int required)
    {
        _mushrooms = current; _requiredMushrooms = required;
        if (_task1Active) RefreshTask1Text();
    }

    public void OnMushroomBurnComplete()
    {
        _showMushroomLine = false;
        if (_task1Active) RefreshTask1Text();
    }

    public void SetCanProgress(int current, int required)
    {
        _cans = current; _requiredCans = required;
        if (_task1Active) RefreshTask1Text();
    }

    public void OnCanDeliveryComplete()
    {
        _showCanLine = false;
        if (_task1Active) RefreshTask1Text();
    }

    // ================================================================
    // PRIVATE HELPERS
    // ================================================================

    private void RefreshTask1Text()
    {
        if (taskText == null) return;
        taskText.gameObject.SetActive(true);

        var sb = new StringBuilder();
        if (_showFirewoodLine)
            sb.AppendLine($"- Bring logs to the fire pit  ({_wood}/{_requiredWood})");
        if (_showLightLine)
            sb.AppendLine("- Light the fire at the fire pit");
        if (_showMushroomLine)
            sb.AppendLine($"- Burn mushrooms in the fire  ({_mushrooms}/{_requiredMushrooms})");
        if (_showCanLine)
            sb.AppendLine($"- Deliver food cans to the church  ({_cans}/{_requiredCans})");

        taskText.text = sb.ToString().TrimEnd();
    }

    private IEnumerator HideCompleteLineAfterDelay()
    {
        yield return new WaitForSeconds(completeLingerDuration);
        if (taskCompleteText != null) taskCompleteText.gameObject.SetActive(false);
        if (taskPanel != null) taskPanel.SetActive(false);
        completeCoroutine = null;
    }
}