using System.Collections;
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

    // Task1 progress state
    private int _wood, _requiredWood;
    private int _mushrooms, _requiredMushrooms;
    private int _cans, _requiredCans;
    private bool _fireIsLit = false;
    private bool _task1Active = false;

    void Awake()
    {
        Local = this;
    }

    void OnDestroy()
    {
        if (Local == this) Local = null;
    }

    // ================================================================
    // Simple one-line task text (used by non-Task1 phases)
    // ================================================================

    public void ShowTask(string description)
    {
        _task1Active = false;

        if (taskPanel != null) taskPanel.SetActive(true);
        if (taskCompleteText != null) taskCompleteText.gameObject.SetActive(false);

        if (taskText != null)
        {
            taskText.gameObject.SetActive(true);
            taskText.text = description;
        }
    }

    // ================================================================
    // Task1 progress tracking (called by Task1_FieldObjectives via ClientRpc)
    // ================================================================

    public void ShowTask1(int requiredWood, int requiredMushrooms, int requiredCans)
    {
        _task1Active = true;
        _fireIsLit = false;
        _wood = 0;
        _requiredWood = requiredWood;
        _mushrooms = 0;
        _requiredMushrooms = requiredMushrooms;
        _cans = 0;
        _requiredCans = requiredCans;

        if (taskPanel != null) taskPanel.SetActive(true);
        if (taskCompleteText != null) taskCompleteText.gameObject.SetActive(false);

        RefreshTask1Text();
    }

    public void SetFirewoodProgress(int current, int required)
    {
        _wood = current;
        _requiredWood = required;
        if (_task1Active) RefreshTask1Text();
    }

    public void UnlockMushroomProgress(int required)
    {
        _fireIsLit = true;
        _requiredMushrooms = required;
        if (_task1Active) RefreshTask1Text();
    }

    public void SetMushroomProgress(int current, int required)
    {
        _mushrooms = current;
        _requiredMushrooms = required;
        if (_task1Active) RefreshTask1Text();
    }

    public void SetCanProgress(int current, int required)
    {
        _cans = current;
        _requiredCans = required;
        if (_task1Active) RefreshTask1Text();
    }

    // ================================================================
    // Completion / clear
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

        if (completeCoroutine != null)
        {
            StopCoroutine(completeCoroutine);
            completeCoroutine = null;
        }
    }

    // ================================================================

    private void RefreshTask1Text()
    {
        if (taskText == null) return;
        taskText.gameObject.SetActive(true);

        string text = $"• Firewood: {_wood}/{_requiredWood}\n" +
                      $"• Food cans: {_cans}/{_requiredCans}";

        if (_fireIsLit)
            text += $"\n• Mushrooms burned: {_mushrooms}/{_requiredMushrooms}";

        taskText.text = text;
    }

    private IEnumerator HideCompleteLineAfterDelay()
    {
        yield return new WaitForSeconds(completeLingerDuration);
        if (taskCompleteText != null) taskCompleteText.gameObject.SetActive(false);
        if (taskPanel != null) taskPanel.SetActive(false);
        completeCoroutine = null;
    }
}