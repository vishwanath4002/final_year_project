using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;

public class PlayerHUD : MonoBehaviour
{
    public static PlayerHUD Local { get; private set; }

    [Header("Task Panel")]
    [SerializeField] private GameObject taskPanel;
    [SerializeField] private TextMeshProUGUI taskText;
    [SerializeField] private TextMeshProUGUI taskCompleteText;

    [Header("Settings")]
    [SerializeField] private float completeLingerDuration = 3f;

    private Coroutine completeCoroutine;

    // Task1 checklist state
    private bool _task1Active = false;
    private bool _showFirewoodLine = false;
    private bool _showLightLine = false;
    private bool _showMushroomLine = false;
    private bool _showCanLine = false;

    private int _wood, _requiredWood;
    private int _mushrooms, _requiredMushrooms;
    private int _cans, _requiredCans;

    void Awake() { Local = this; }
    void OnDestroy() { if (Local == this) Local = null; }

    // ================================================================
    // Simple one-liner (used for non-Task1 phases by TaskManager)
    // ================================================================

    public void ShowTask(string description)
    {
        _task1Active = false;
        if (taskPanel != null) taskPanel.SetActive(true);
        if (taskCompleteText != null) taskCompleteText.gameObject.SetActive(false);
        if (taskText != null) { taskText.gameObject.SetActive(true); taskText.text = description; }
    }

    // ================================================================
    // Task1 checklist
    // ================================================================

    public void ShowTask1(int requiredWood, int requiredMushrooms, int requiredCans)
    {
        _task1Active = true;
        _wood = 0; _requiredWood = requiredWood;
        _mushrooms = 0; _requiredMushrooms = requiredMushrooms;
        _cans = 0; _requiredCans = requiredCans;

        _showFirewoodLine = true;
        _showLightLine = false;
        _showMushroomLine = false;
        _showCanLine = true;

        if (taskPanel != null) taskPanel.SetActive(true);
        if (taskCompleteText != null) taskCompleteText.gameObject.SetActive(false);

        RefreshTask1Text();
    }

    public void SetFirewoodProgress(int current, int required)
    {
        _wood = current; _requiredWood = required;
        if (_task1Active) RefreshTask1Text();
    }

    // Firewood fully deposited -- swap deposit line for light-fire line
    public void OnFirewoodDepositComplete()
    {
        _showFirewoodLine = false;
        _showLightLine = true;
        if (_task1Active) RefreshTask1Text();
    }

    // Fire lit -- swap light-fire line for mushroom line
    public void OnFireLit(int requiredMushrooms)
    {
        _showLightLine = false;
        _showMushroomLine = true;
        _requiredMushrooms = requiredMushrooms;
        _mushrooms = 0;
        if (_task1Active) RefreshTask1Text();
    }

    public void SetMushroomProgress(int current, int required)
    {
        _mushrooms = current; _requiredMushrooms = required;
        if (_task1Active) RefreshTask1Text();
    }

    // All mushrooms burned -- remove mushroom line
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

    // All cans delivered -- remove can line
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
        if (taskPanel != null) taskPanel.SetActive(false);
        if (taskText != null) taskText.gameObject.SetActive(false);
        if (taskCompleteText != null) taskCompleteText.gameObject.SetActive(false);

        if (completeCoroutine != null) { StopCoroutine(completeCoroutine); completeCoroutine = null; }
    }

    // ================================================================

    private void RefreshTask1Text()
    {
        if (taskText == null) return;
        taskText.gameObject.SetActive(true);

        StringBuilder sb = new StringBuilder();

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