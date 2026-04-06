using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;

public class PlayerHUD : MonoBehaviour
{
    public static PlayerHUD Local { get; private set; }

    [Header("Task Panel")]
    [SerializeField] private GameObject      taskPanel;
    [SerializeField] private TextMeshProUGUI taskText;
    [SerializeField] private TextMeshProUGUI taskCompleteText;

    [Header("Held Item Display")]
    [SerializeField] private GameObject      heldItemPanel;
    [SerializeField] private TextMeshProUGUI heldItemText;
    [SerializeField] private float           normalFontSize   = 18f;
    [SerializeField] private float           canStackFontSize = 13f;

    [Header("Settings")]
    [SerializeField] private float completeLingerDuration = 3f;

    private Coroutine completeCoroutine;

    private bool _task1Active      = false;
    private bool _showFirewoodLine = false;
    private bool _showLightLine    = false;
    private bool _showMushroomLine = false;
    private bool _showCanLine      = false;

    private int _wood,      _requiredWood;
    private int _mushrooms, _requiredMushrooms;
    private int _cans,      _requiredCans;

    void Awake()
    {
        Local = this;
        if (heldItemPanel != null) heldItemPanel.SetActive(false);
    }

    void OnDestroy() { if (Local == this) Local = null; }

    // ================================================================
    // Held Item Display
    // ================================================================

    // Single item (firewood, mushroom, etc.) — normal font size
    public void ShowHeldItem(string itemName)
    {
        if (heldItemPanel != null) heldItemPanel.SetActive(true);
        if (heldItemText  == null) return;

        heldItemText.fontSize = normalFontSize;
        heldItemText.text     = itemName;
    }

    // Can stack — smaller font, one name per line
    public void UpdateCanInventory(string[] names)
    {
        int count = names != null ? names.Length : 0;

        if (heldItemPanel != null) heldItemPanel.SetActive(count > 0);
        if (heldItemText  == null) return;

        if (count == 0)
        {
            heldItemText.text = "";
            return;
        }

        heldItemText.fontSize = canStackFontSize;

        var sb = new StringBuilder();
        for (int i = count - 1; i >= 0; i--)  // top of stack first
            sb.AppendLine(names[i]);

        heldItemText.text = sb.ToString().TrimEnd();
    }

    // Called when inventory is empty
    public void ClearHeldItem()
    {
        if (heldItemPanel != null) heldItemPanel.SetActive(false);
        if (heldItemText  != null) heldItemText.text = "";
    }

    // ================================================================
    // Simple one-liner
    // ================================================================

    public void ShowTask(string description)
    {
        _task1Active = false;
        if (taskPanel        != null) taskPanel.SetActive(true);
        if (taskCompleteText != null) taskCompleteText.gameObject.SetActive(false);
        if (taskText         != null) { taskText.gameObject.SetActive(true); taskText.text = description; }
    }

    // ================================================================
    // Task1 checklist
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

        if (taskPanel        != null) taskPanel.SetActive(true);
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
    // Completion / Clear
    // ================================================================

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
        if (taskPanel        != null) taskPanel.SetActive(false);
        if (taskText         != null) taskText.gameObject.SetActive(false);
        if (taskCompleteText != null) taskCompleteText.gameObject.SetActive(false);

        if (completeCoroutine != null) { StopCoroutine(completeCoroutine); completeCoroutine = null; }
    }

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
        if (taskPanel        != null) taskPanel.SetActive(false);
        completeCoroutine = null;
    }
}