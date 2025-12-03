using UnityEngine;
using TMPro;
using UnityEngine.Networking;
using System.Collections;
using System;
using Unity.Netcode;

[Serializable]
public class ChatPayload
{
    public string player_id;
    public string message;
}

[Serializable]
public class ChatResponse
{
    public string player_id;
    public string message;
    public string npc_reply;
    public string timestamp;
}

public class ProximityChatInput : MonoBehaviour
{
    public TMP_InputField inputField;
    public ProximityChatManager chatManager;

    [Header("API Settings")]
    [Tooltip("FastAPI endpoint")]
    public string apiUrl = "http://127.0.0.1:8000/chat";

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return))
        {
            if (!string.IsNullOrWhiteSpace(inputField.text))
            {
                string text = inputField.text.Trim();

                var localIdentity = PlayerIdentity.Local;
                if (localIdentity == null)
                {
                    Debug.LogWarning("No PlayerIdentity.Local found yet.");
                    return;
                }

                string displayName = localIdentity.GetDisplayName();

                // Send through proximity chat so everyone (including sender) gets it via RPC
                if (NetworkManager.Singleton != null &&
                    NetworkManager.Singleton.IsClient &&
                    NetworkManager.Singleton.IsConnectedClient)
                {
                    // Server will determine position automatically - just pass name, message, and color
                    chatManager.SendChatMessageServerRpc(displayName, text, Color.green);
                }
                else
                {
                    // fallback: show locally if networking not running
                    chatManager.AddMessage(displayName, text, Color.green);
                }

                // Clear + lose focus
                inputField.text = "";
                inputField.DeactivateInputField();

                // Ask backend (alien AI) for a reply, using the clientId as backend id
                StartCoroutine(SendMessageToServer(localIdentity.OwnerClientId.ToString(), text));
            }
            else
            {
                inputField.ActivateInputField();
            }
        }
    }

    IEnumerator SendMessageToServer(string playerIdForBackend, string message)
    {
        ChatPayload payload = new ChatPayload { player_id = playerIdForBackend, message = message };
        string json = JsonUtility.ToJson(payload);

        using (UnityWebRequest req = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    ChatResponse resp = JsonUtility.FromJson<ChatResponse>(req.downloadHandler.text);
                    if (!string.IsNullOrEmpty(resp.npc_reply))
                    {
                        string npcText = resp.npc_reply;

                        // For now, show reply only locally so you can test behavior.
                        // Later, you can move alien broadcasting to a server-side script.
                        chatManager.AddMessage("Alien-01", npcText, Color.red);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("Failed to parse chat response: " + ex + " raw: " + req.downloadHandler.text);
                }
            }
            else
            {
                Debug.LogError("Chat POST failed: " + req.error);
                chatManager.AddMessage("System", "Connection error", Color.yellow);
            }
        }
    }
}