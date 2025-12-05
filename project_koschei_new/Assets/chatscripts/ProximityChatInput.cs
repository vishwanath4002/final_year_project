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
public class ImpostorMessageData
{
    public string player_id;
    public string message;
    public string timestamp;
}

[Serializable]
public class ChatResponse
{
    public string player_id;
    public string message;
    public string timestamp;
    public ImpostorMessageData impostor_message; // NEW: impostor can inject messages
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

                // Send to impostor backend
                StartCoroutine(SendMessageToImpostorBackend(displayName, text));
            }
            else
            {
                inputField.ActivateInputField();
            }
        }
    }

    IEnumerator SendMessageToImpostorBackend(string playerName, string message)
    {
        ChatPayload payload = new ChatPayload
        {
            player_id = playerName,
            message = message
        };

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

                    // Check if impostor responded
                    if (resp.impostor_message != null &&
                        !string.IsNullOrEmpty(resp.impostor_message.message))
                    {
                        string impostorName = resp.impostor_message.player_id;
                        string impostorMessage = resp.impostor_message.message;

                        Debug.Log($"?? Impostor message detected: {impostorName}: {impostorMessage}");

                        // Broadcast impostor message through proximity system
                        // Only if we're connected to network
                        if (NetworkManager.Singleton != null &&
                            NetworkManager.Singleton.IsClient &&
                            NetworkManager.Singleton.IsConnectedClient)
                        {
                            // Use ServerRpc to broadcast from server (ensures all clients get it)
                            chatManager.BroadcastImpostorMessageServerRpc(
                                impostorName,
                                impostorMessage,
                                Color.red
                            );
                        }
                        else
                        {
                            // Fallback: local only
                            chatManager.AddMessage(impostorName, impostorMessage, Color.red);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to parse chat response: {ex}\nRaw response: {req.downloadHandler.text}");
                }
            }
            else
            {
                Debug.LogWarning($"Chat POST failed: {req.error}");
            }
        }
    }
}