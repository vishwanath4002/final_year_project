using UnityEngine;
using Unity.Netcode;

public class ScientistNPCDialogue : NetworkBehaviour
{
    [Header("Dialogue Lines")]
    [SerializeField, TextArea]
    private string[] dialogueLines = new string[]
    {
        "So. They actually sent someone. I was not holding my breath.",
        "I am what remains of the advance team. We entered three days ago. There were four of us. Now there is me.",
        "This place — Koschei Station. Soviet research post. Abandoned. Flooded. Whatever happened here, it is not finished happening. You understand?",
        "Your objective is not complicated. Find the researchers still alive in this zone. Stay with your group. Do not wander. This place does not forgive stupidity.",
        "There is a woman waiting for you ahead. She has been in this zone longer than any of us. She knows the layout, the dangers, all of it. She will brief you properly.",
        "Go. And try not to die before you even reach her."
    };

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string talkTrigger = "Talk";

    // Synced over network so all clients know current state
    private NetworkVariable<ulong> _interactorClientId = new NetworkVariable<ulong>(ulong.MaxValue);
    private NetworkVariable<int> _currentLineIndex = new NetworkVariable<int>(0);
    private NetworkVariable<bool> _isTalking = new NetworkVariable<bool>(false);

    public void RequestInteract(ulong clientId)
    {
        if (!IsServer) return;

        Debug.Log($"[NPC] RequestInteract from client {clientId}, isTalking={_isTalking.Value}");

        if (!_isTalking.Value)
            StartDialogueServer(clientId);
        else if (clientId == _interactorClientId.Value)
            AdvanceDialogueServer();
    }

    private void StartDialogueServer(ulong clientId)
    {
        _isTalking.Value = true;
        _interactorClientId.Value = clientId;
        _currentLineIndex.Value = 0;

        // Trigger anim + show line to ALL clients
        ShowLineToAllClientRpc(dialogueLines[0]);
    }

    private void AdvanceDialogueServer()
    {
        _currentLineIndex.Value++;

        if (_currentLineIndex.Value >= dialogueLines.Length)
        {
            _isTalking.Value = false;
            _interactorClientId.Value = ulong.MaxValue;
            HideDialogueAllClientRpc();
            return;
        }

        ShowLineToAllClientRpc(dialogueLines[_currentLineIndex.Value]);
    }

    [ClientRpc]
    private void ShowLineToAllClientRpc(string line)
    {
        Debug.Log($"[NPC] ShowLine to all: {line}");

        if (animator != null)
            animator.SetTrigger(talkTrigger);

        if (NPCDialogueUI.Instance == null)
        {
            Debug.LogError("[NPC] NPCDialogueUI.Instance is NULL!");
            return;
        }

        // Only show panel to players who are near this NPC
        if (NPCDialogueUI.Instance.IsNearNPC)
            NPCDialogueUI.Instance.ShowLine(line);
    }

    [ClientRpc]
    private void HideDialogueAllClientRpc()
    {
        if (animator != null)
            animator.SetTrigger(talkTrigger);

        if (NPCDialogueUI.Instance != null)
            NPCDialogueUI.Instance.HideDialogue();
    }

    // Called by ScientistNPCController when player enters/exits range
    public void SetPlayerNearby(bool nearby)
    {
        if (NPCDialogueUI.Instance == null) return;
        NPCDialogueUI.Instance.IsNearNPC = nearby;

        // If dialogue is already in progress and player just walked in, show current line
        if (nearby && _isTalking.Value)
            NPCDialogueUI.Instance.ShowLine(dialogueLines[_currentLineIndex.Value]);

        if (!nearby)
            NPCDialogueUI.Instance.HideDialogue();
    }

    public bool IsTalking() => _isTalking.Value;
}
