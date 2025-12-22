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
public class ChatPayloadWithGroup
{
    public string player_id;
    public string message;
    public string group_id;  // Track which group message is from
}

[Serializable]
public class ImpostorMessage
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
    public ImpostorMessage impostor_message;
}

public class ProximityChatInput : MonoBehaviour
{
    public TMP_InputField inputField;
    public ProximityChatManager chatManager;

    [Header("API Settings (optional override)")]
    [Tooltip("If empty, we derive the URL from NetworkManager's UnityTransport address/port")]
    public string apiUrlOverride;
    public int backendPort = 8000;
    public string backendPath = "/chat";

    string ResolvedApiUrl
    {
        get
        {
            if (!string.IsNullOrEmpty(apiUrlOverride))
                return apiUrlOverride;

            // Derive from NetworkManager / UnityTransport
            return NetworkHostAddressHelper.GetChatApiUrlFromNetworkManager(backendPort, backendPath);
        }
    }

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

                if (NetworkManager.Singleton != null &&
                    NetworkManager.Singleton.IsClient &&
                    NetworkManager.Singleton.IsConnectedClient)
                {
                    // Send through proximity chat system
                    // This will call NotifyBackendOfMessage with proper group info
                    chatManager.SendChatMessageServerRpc(displayName, text, Color.green);
                }
                else
                {
                    chatManager.AddMessage(displayName, text, Color.green);
                }

                inputField.text = "";
                inputField.DeactivateInputField();

                // REMOVED: Don't call backend here - let ProximityChatManager handle it
                // This prevents duplicate messages with wrong group info
            }
            else
            {
                inputField.ActivateInputField();
            }
        }
    }

    /// <summary>
    /// Called by ProximityChatManager to notify backend of messages with group info
    /// </summary>
    public void NotifyBackendOfMessage(string playerName, string message, string groupId)
    {
        Debug.Log($"[ChatInput] NotifyBackendOfMessage called: player={playerName}, msg={message}");

        var localIdentity = PlayerIdentity.Local;
        if (localIdentity == null)
        {
            Debug.LogWarning("[ChatInput] No PlayerIdentity.Local found!");
            return;
        }

        string myDisplayName = localIdentity.GetDisplayName();
        Debug.Log($"[ChatInput] My display name: {myDisplayName}, Message from: {playerName}");

        // Only send if this is the actual sender (don't echo messages from others)
        if (myDisplayName != playerName)
        {
            Debug.Log($"[ChatInput] Skipping backend notify - not my message (I am {myDisplayName}, message from {playerName})");
            return;
        }

        Debug.Log($"[ChatInput] This is my message! Starting coroutine to send to backend...");
        StartCoroutine(SendMessageToServerWithGroup(
            playerName,  // FIXED: Use display name, not ClientID
            message,
            groupId
        ));
    }

    /// <summary>
    /// Sends message to backend including group information
    /// </summary>
    IEnumerator SendMessageToServerWithGroup(string playerDisplayName, string message, string groupId)
    {
        string url = ResolvedApiUrl;
        if (string.IsNullOrEmpty(url))
        {
            Debug.LogWarning("ProximityChatInput: could not resolve backend URL from NetworkManager; skipping LLM call.");
            yield break;
        }

        // Ensure URL uses HTTP (not HTTPS) for local development
        if (url.StartsWith("https://127.0.0.1") || url.StartsWith("https://localhost"))
        {
            url = url.Replace("https://", "http://");
            Debug.Log($"Converted HTTPS to HTTP for local development: {url}");
        }

        ChatPayloadWithGroup payload = new ChatPayloadWithGroup
        {
            player_id = playerDisplayName,  // Use display name consistently
            message = message,
            group_id = groupId
        };

        string json = JsonUtility.ToJson(payload);

        Debug.Log($"📤 Sending message to backend: player='{playerDisplayName}' group='{groupId}' msg='{message}'");

        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
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

                    // Check if impostor sent a message
                    if (resp.impostor_message != null && !string.IsNullOrEmpty(resp.impostor_message.message))
                    {
                        // Broadcast impostor message through the server
                        if (NetworkManager.Singleton != null &&
                            NetworkManager.Singleton.IsClient &&
                            chatManager != null)
                        {
                            chatManager.BroadcastImpostorMessageServerRpc(
                                resp.impostor_message.player_id,
                                resp.impostor_message.message,
                                Color.red
                            );
                        }

                        Debug.Log($"🎭 Impostor message received from {resp.impostor_message.player_id}: {resp.impostor_message.message}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("Failed to parse chat response: " + ex + " raw: " + req.downloadHandler.text);
                }
            }
            else
            {
                Debug.LogError($"Chat POST failed to {url}: {req.error}");
                chatManager.AddMessage("System", "Connection error - Is the backend server running?", Color.yellow);
            }
        }
    }
}