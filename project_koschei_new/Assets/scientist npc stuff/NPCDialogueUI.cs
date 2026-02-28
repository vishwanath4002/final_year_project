using UnityEngine;
using TMPro;

public class NPCDialogueUI : MonoBehaviour
{
    public static NPCDialogueUI Instance;

    [Header("UI")]
    public GameObject dialoguePanel;
    public TextMeshProUGUI dialogueText;

    // Tracks whether the local player is near an NPC
    public bool IsNearNPC { get; set; } = false;

    void Awake()
    {
        Instance = this;
        HideDialogue();
    }

    public void ShowLine(string text)
    {
        if (dialoguePanel == null || dialogueText == null) return;
        dialoguePanel.SetActive(true);
        dialogueText.text = text;
    }

    public void HideDialogue()
    {
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);
    }
}