using Unity.Netcode;
using UnityEngine;

public class ProximityChatManager : NetworkBehaviour
{
    public static ProximityChatManager Instance;

    [Header("UI")]
    public GameObject chatMessagePrefab;
    public Transform chatContainer;
    public int maxMessages = 5;

    [Header("Proximity settings")]
    public float chatRadius = 15f; // how close players must be to hear chat

    void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        // Make sure container stays bottom-left
        if (chatContainer != null)
        {
            RectTransform rt = chatContainer.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);   // bottom-left
            rt.anchorMax = new Vector2(0, 0);   // bottom-left
            rt.pivot = new Vector2(0, 0);       // pivot at bottom-left
            rt.anchoredPosition = new Vector2(20, 40); // offset from corner
        }
    }

    // ===== Local UI helper (no networking) =====
    public void AddMessageLocal(string playerName, string message, Color nameColor)
    {
        if (chatMessagePrefab == null || chatContainer == null) return;

        GameObject obj = Instantiate(chatMessagePrefab, chatContainer);
        obj.SetActive(true);

        ProximityChatMessage msg = obj.GetComponent<ProximityChatMessage>();
        msg.Setup(playerName, message, nameColor);

        if (chatContainer.childCount > maxMessages)
        {
            Destroy(chatContainer.GetChild(0).gameObject);
        }
    }
    public void AddMessage(string playerName, string message, Color nameColor)
    {
        AddMessageLocal(playerName, message, nameColor);
    }

    // ===== Networking =====

    // Called by clients (players or alien) to send a message into proximity chat
    [ServerRpc(RequireOwnership = false)]
    public void SendChatMessageServerRpc(string fromName, string message, Vector3 worldPos)
    {
        // On server: forward to nearby clients only
        foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
        {
            var client = kvp.Value;
            var playerObject = client.PlayerObject;
            if (playerObject == null) continue;

            float dist = Vector3.Distance(worldPos, playerObject.transform.position);
            if (dist <= chatRadius)
            {
                ReceiveChatMessageClientRpc(fromName, message, client.ClientId);
            }
        }
    }

    // Runs on selected clients to actually show the message in their UI
    [ClientRpc]
    private void ReceiveChatMessageClientRpc(string fromName, string message, ulong clientId = 0)
    {
        // You can choose a color based on name / team; for now just white
        AddMessageLocal(fromName, message, Color.white);
    }
}
