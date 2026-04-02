using System.Collections;
using TMPro;
using UnityEngine;

public class PlayerHUD : MonoBehaviour
{
    public static PlayerHUD Local { get; private set; }

    [Header("Task Panel")]
    [SerializeField] private GameObject taskPanel;
    [SerializeField] private TextMeshProUGUI taskDescription;
    [SerializeField] private TextMeshProUGUI taskCompleteText;

    [Header("Settings")]
    [SerializeField] private float completeLingerDuration = 3f;

    private Coroutine completeCoroutine;

    void Awake()
    {
        Local = this;
    }

    void OnDestroy()
    {
        if (Local == this) Local = null;
    }

    // ================================================================
    // Public API
    // ================================================================

    public void ShowTask(string description)
    {
        if (taskPanel != null) taskPanel.SetActive(true);
        if (taskDescription != null) taskDescription.text = description;
    }

    public void CompleteCurrentTask(string completedTaskName = "Task")
    {
        if (taskDescription != null) taskDescription.text = "";

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
        if (taskPanel != null) taskPanel.SetActive(false);
        if (taskDescription != null) taskDescription.text = "";

        if (taskCompleteText != null)
            taskCompleteText.gameObject.SetActive(false);

        if (completeCoroutine != null)
        {
            StopCoroutine(completeCoroutine);
            completeCoroutine = null;
        }
    }

    // ================================================================

    private IEnumerator HideCompleteLineAfterDelay()
    {
        yield return new WaitForSeconds(completeLingerDuration);

        if (taskCompleteText != null)
            taskCompleteText.gameObject.SetActive(false);

        // Only collapse the panel if no task description is currently showing
        bool hasDescription = taskDescription != null && !string.IsNullOrEmpty(taskDescription.text);
        if (taskPanel != null && !hasDescription)
            taskPanel.SetActive(false);

        completeCoroutine = null;
    }
}