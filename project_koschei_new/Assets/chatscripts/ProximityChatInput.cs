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
    public int backendPort = 8000;  // Changed from 8443 to match FastAPI default
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
                    chatManager.SendChatMessageServerRpc(displayName, text, Color.green);
                }
                else
                {
                    chatManager.AddMessage(displayName, text, Color.green);
                }

                inputField.text = "";
                inputField.DeactivateInputField();

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

        ChatPayload payload = new ChatPayload { player_id = playerIdForBackend, message = message };
        string json = JsonUtility.ToJson(payload);

        Debug.Log($"Sending message to: {url}");

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
                        chatManager.AddMessage(resp.impostor_message.player_id, resp.impostor_message.message, Color.red);
                        Debug.Log($"Impostor message received from {resp.impostor_message.player_id}: {resp.impostor_message.message}");
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