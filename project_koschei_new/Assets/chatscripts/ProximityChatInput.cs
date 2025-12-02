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
    public TMP_InputField inputField;          // assign in Inspector
    public ProximityChatManager chatManager;   // assign in Inspector

    [Tooltip("FastAPI endpoint (keep localhost in editor)")]
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
                Transform localTransform = localIdentity.transform;

                // 1) Send through proximity chat so everyone (including sender) gets it via RPC
                if (NetworkManager.Singleton != null &&
                    NetworkManager.Singleton.IsClient &&
                    NetworkManager.Singleton.IsConnectedClient &&
                    localTransform != null)
                {
                    Vector3 pos = localTransform.position;
                    chatManager.SendChatMessageServerRpc(displayName, text, pos);
                }
                else
                {
                    // fallback: show locally if networking not running
                    chatManager.AddMessage(displayName, text, Color.green);
                }

                // 2) Clear + lose focus
                inputField.text = "";
                inputField.DeactivateInputField();

                // 3) Ask backend (alien AI) for a reply, using the clientId as backend id
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
